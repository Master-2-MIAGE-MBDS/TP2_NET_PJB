using System.Net;
using System.Net.Sockets;
using MessagePack;
using Gauniv.GameServer.Models;

namespace Gauniv.GameServer.Network;

/// <summary>
/// Serveur TCP qui gère les connexions des joueurs
/// </summary>
public class TcpGameServer
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly List<PlayerConnection> _connections;
    private readonly object _connectionsLock = new();
    private readonly int _port;
    
    public event EventHandler<PlayerConnection>? ClientConnected;
    public event EventHandler<PlayerConnection>? ClientDisconnected;
    public event EventHandler<(PlayerConnection Connection, GameMessage Message)>? MessageReceived;
    
    public TcpGameServer(int port)
    {
        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _cancellationTokenSource = new CancellationTokenSource();
        _connections = new List<PlayerConnection>();
    }
    
    /// <summary>
    /// Démarre le serveur
    /// </summary>
    public async Task StartAsync()
    {
        _listener.Start();
        Console.WriteLine($"[Server] Démarré sur le port {_port}");
        
        // Boucle d'acceptation des connexions
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                var connection = new PlayerConnection(client);
                
                lock (_connectionsLock)
                {
                    _connections.Add(connection);
                }
                
                Console.WriteLine($"[Server] Nouveau client connecté: {connection.RemoteEndPoint}");
                ClientConnected?.Invoke(this, connection);
                
                // Gestion du client dans une tâche séparée
                _ = Task.Run(() => HandleClientAsync(connection), _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Erreur lors de l'acceptation d'un client: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Arrête le serveur
    /// </summary>
    public async Task StopAsync()
    {
        Console.WriteLine("[Server] Arrêt en cours...");
        
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        
        // Fermer toutes les connexions
        List<PlayerConnection> connectionsCopy;
        lock (_connectionsLock)
        {
            connectionsCopy = new List<PlayerConnection>(_connections);
        }
        
        foreach (var connection in connectionsCopy)
        {
            connection.Dispose();
        }
        
        _connections.Clear();
        
        await Task.CompletedTask;
        Console.WriteLine("[Server] Arrêté");
    }
    
    /// <summary>
    /// Gère la communication avec un client
    /// </summary>
    private async Task HandleClientAsync(PlayerConnection connection)
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested && connection.IsConnected)
            {
                var message = await connection.ReceiveMessageAsync(_cancellationTokenSource.Token);
                if (message != null)
                {
                    MessageReceived?.Invoke(this, (connection, message));
                }
                else
                {
                    break; // Connexion fermée
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Erreur avec le client {connection.PlayerId ?? connection.RemoteEndPoint}: {ex.Message}");
        }
        finally
        {
            lock (_connectionsLock)
            {
                _connections.Remove(connection);
            }
            
            ClientDisconnected?.Invoke(this, connection);
            connection.Dispose();
            
            Console.WriteLine($"[Server] Client déconnecté: {connection.PlayerId ?? connection.RemoteEndPoint}");
        }
    }
    
    /// <summary>
    /// Envoie un message à tous les clients connectés
    /// </summary>
    public async Task BroadcastMessageAsync(GameMessage message)
    {
        List<PlayerConnection> connectionsCopy;
        lock (_connectionsLock)
        {
            connectionsCopy = new List<PlayerConnection>(_connections);
        }
        
        var tasks = connectionsCopy.Select(c => c.SendMessageAsync(message));
        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Envoie un message à un client spécifique
    /// </summary>
    public async Task SendMessageToPlayerAsync(string playerId, GameMessage message)
    {
        PlayerConnection? connection;
        lock (_connectionsLock)
        {
            connection = _connections.FirstOrDefault(c => c.PlayerId == playerId);
        }
        
        if (connection != null)
        {
            await connection.SendMessageAsync(message);
        }
    }
    
    /// <summary>
    /// Obtient la liste des joueurs connectés
    /// </summary>
    public List<PlayerConnection> GetConnectedPlayers()
    {
        lock (_connectionsLock)
        {
            return new List<PlayerConnection>(_connections);
        }
    }
}

/// <summary>
/// Représente une connexion avec un joueur
/// </summary>
public class PlayerConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    
    public string? PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public bool IsReady { get; set; }
    public bool IsConnected => _client.Connected;
    public string RemoteEndPoint => _client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    
    public PlayerConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }
    
    /// <summary>
    /// Envoie un message au client
    /// </summary>
    public async Task SendMessageAsync(GameMessage message)
    {
        await _sendLock.WaitAsync();
        try
        {
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Sérialiser avec MessagePack
            var data = MessagePackSerializer.Serialize(message);
            
            // Envoyer la taille du message (4 bytes)
            var lengthBytes = BitConverter.GetBytes(data.Length);
            await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            
            // Envoyer les données
            await _stream.WriteAsync(data, 0, data.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }
    
    /// <summary>
    /// Reçoit un message du client
    /// </summary>
    public async Task<GameMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Lire la taille du message (4 bytes)
            var lengthBytes = new byte[4];
            var bytesRead = await _stream.ReadAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
            
            if (bytesRead == 0)
            {
                return null; // Connexion fermée
            }
            
            var length = BitConverter.ToInt32(lengthBytes, 0);
            
            // Lire les données
            var data = new byte[length];
            var totalRead = 0;
            
            while (totalRead < length)
            {
                bytesRead = await _stream.ReadAsync(data, totalRead, length - totalRead, cancellationToken);
                if (bytesRead == 0)
                {
                    return null; // Connexion fermée
                }
                totalRead += bytesRead;
            }
            
            // Désérialiser avec MessagePack
            return MessagePackSerializer.Deserialize<GameMessage>(data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[Connection] Erreur lors de la réception: {ex.Message}");
            return null;
        }
    }
    
    public void Dispose()
    {
        _sendLock.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }
}
