using System.Net.Sockets;
using MessagePack;
using Gauniv.GameServer.Models;

namespace Gauniv.GameServer.Examples;

/// <summary>
/// Exemple de client TCP pour se connecter au serveur de jeu
/// Ce fichier sert de référence pour implémenter un client dans votre application
/// </summary>
public class GameClient : IDisposable
{
    private readonly TcpClient _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _receiveTokenSource;
    
    public string? PlayerId { get; private set; }
    public bool IsConnected => _client.Connected;
    
    public event EventHandler<GameMessage>? MessageReceived;
    
    public GameClient()
    {
        _client = new TcpClient();
    }
    
    /// <summary>
    /// Se connecte au serveur
    /// </summary>
    public async Task ConnectAsync(string host, int port, string playerName)
    {
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
        
        Console.WriteLine($"[Client] Connecté au serveur {host}:{port}");
        
        // Envoyer le message de connexion
        var connectData = new PlayerConnectData
        {
            PlayerName = playerName
        };
        
        var message = new GameMessage
        {
            Type = MessageType.PlayerConnect,
            Data = MessagePackSerializer.Serialize(connectData)
        };
        
        await SendMessageAsync(message);
        
        // Démarrer la réception des messages
        _receiveTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveMessagesAsync(_receiveTokenSource.Token));
    }
    
    /// <summary>
    /// Déconnecte du serveur
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (IsConnected)
        {
            var message = new GameMessage
            {
                Type = MessageType.PlayerDisconnect,
                PlayerId = PlayerId
            };
            
            await SendMessageAsync(message);
        }
        
        _receiveTokenSource?.Cancel();
        _client.Close();
        
        Console.WriteLine("[Client] Déconnecté du serveur");
    }
    
    /// <summary>
    /// Envoie un message "prêt"
    /// </summary>
    public async Task SendReadyAsync()
    {
        var message = new GameMessage
        {
            Type = MessageType.PlayerReady,
            PlayerId = PlayerId
        };
        
        await SendMessageAsync(message);
        Console.WriteLine("[Client] Envoi du signal 'Prêt'");
    }
    
    /// <summary>
    /// Envoie une action de jeu
    /// </summary>
    public async Task SendActionAsync(string actionType, Dictionary<string, object>? parameters = null)
    {
        var actionData = new PlayerActionData
        {
            ActionType = actionType,
            Parameters = parameters
        };
        
        var message = new GameMessage
        {
            Type = MessageType.PlayerAction,
            PlayerId = PlayerId,
            Data = MessagePackSerializer.Serialize(actionData)
        };
        
        await SendMessageAsync(message);
        Console.WriteLine($"[Client] Action envoyée: {actionType}");
    }
    
    /// <summary>
    /// Envoie un message au serveur
    /// </summary>
    private async Task SendMessageAsync(GameMessage message)
    {
        if (_stream == null) return;
        
        await _sendLock.WaitAsync();
        try
        {
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            var data = MessagePackSerializer.Serialize(message);
            var lengthBytes = BitConverter.GetBytes(data.Length);
            
            await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }
    
    /// <summary>
    /// Reçoit les messages du serveur en continu
    /// </summary>
    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        if (_stream == null) return;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                var lengthBytes = new byte[4];
                var bytesRead = await _stream.ReadAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
                
                if (bytesRead == 0) break;
                
                var length = BitConverter.ToInt32(lengthBytes, 0);
                var data = new byte[length];
                var totalRead = 0;
                
                while (totalRead < length)
                {
                    bytesRead = await _stream.ReadAsync(data, totalRead, length - totalRead, cancellationToken);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }
                
                var message = MessagePackSerializer.Deserialize<GameMessage>(data);
                
                // Traiter le message
                HandleMessage(message);
                
                // Notifier les écouteurs
                MessageReceived?.Invoke(this, message);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client] Erreur lors de la réception: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Traite un message reçu du serveur
    /// </summary>
    private void HandleMessage(GameMessage message)
    {
        switch (message.Type)
        {
            case MessageType.ServerWelcome:
                PlayerId = message.PlayerId;
                Console.WriteLine($"[Client] Bienvenue! ID reçu: {PlayerId}");
                break;
                
            case MessageType.ServerError:
                if (message.Data != null)
                {
                    var error = MessagePackSerializer.Deserialize<ErrorData>(message.Data);
                    Console.WriteLine($"[Client] Erreur serveur: {error.ErrorCode} - {error.Message}");
                }
                break;
                
            case MessageType.GameState:
                if (message.Data != null)
                {
                    var state = MessagePackSerializer.Deserialize<GameStateData>(message.Data);
                    Console.WriteLine($"[Client] État du jeu: {state.Status}, {state.Players.Count} joueur(s)");
                }
                break;
                
            case MessageType.PlayerJoined:
                if (message.Data != null)
                {
                    var player = MessagePackSerializer.Deserialize<PlayerInfo>(message.Data);
                    Console.WriteLine($"[Client] Joueur rejoint: {player.PlayerName}");
                }
                break;
                
            case MessageType.PlayerLeft:
                if (message.Data != null)
                {
                    var player = MessagePackSerializer.Deserialize<PlayerInfo>(message.Data);
                    Console.WriteLine($"[Client] Joueur parti: {player.PlayerName}");
                }
                break;
                
            case MessageType.GameStarted:
                Console.WriteLine("[Client] La partie commence !");
                break;
                
            case MessageType.GameEnded:
                Console.WriteLine("[Client] La partie est terminée !");
                break;
        }
    }
    
    public void Dispose()
    {
        _receiveTokenSource?.Cancel();
        _receiveTokenSource?.Dispose();
        _sendLock.Dispose();
        _stream?.Dispose();
        _client.Dispose();
    }
}

/// <summary>
/// Programme d'exemple pour tester le client
/// USAGE: Copiez ce code dans une nouvelle application console pour tester le client
/// </summary>
public class ClientExample
{
    public static async Task RunExampleAsync(string[] args)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("    Gauniv Game Client - Exemple         ");
        Console.WriteLine("===========================================");
        Console.WriteLine();
        
        // Configuration
        string host = "localhost";
        int port = 7777;
        string playerName = "TestPlayer";
        
        if (args.Length > 0) host = args[0];
        if (args.Length > 1) int.TryParse(args[1], out port);
        if (args.Length > 2) playerName = args[2];
        
        using var client = new GameClient();
        
        try
        {
            // Connexion au serveur
            await client.ConnectAsync(host, port, playerName);
            
            // Attendre un peu
            await Task.Delay(2000);
            
            // Envoyer "prêt"
            await client.SendReadyAsync();
            
            // Envoyer quelques actions
            await Task.Delay(1000);
            await client.SendActionAsync("move", new Dictionary<string, object>
            {
                ["x"] = 10,
                ["y"] = 20
            });
            
            await Task.Delay(1000);
            await client.SendActionAsync("attack", new Dictionary<string, object>
            {
                ["target"] = "enemy1"
            });
            
            // Rester connecté pendant 10 secondes
            Console.WriteLine();
            Console.WriteLine("[Client] Appuyez sur une touche pour déconnecter...");
            Console.ReadKey();
            
            // Déconnexion
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client] Erreur: {ex.Message}");
        }
    }
}
