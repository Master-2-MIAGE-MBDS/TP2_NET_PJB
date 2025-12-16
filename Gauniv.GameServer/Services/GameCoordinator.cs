using MessagePack;
using Gauniv.GameServer.Models;
using Gauniv.GameServer.Network;

namespace Gauniv.GameServer.Services;

/// <summary>
/// Coordonnateur de jeu de morpion qui gère l'état du jeu et les joueurs
/// </summary>
public class GameCoordinator
{
    private readonly TcpGameServer _server;
    private readonly Dictionary<string, PlayerConnection> _players;
    private readonly Dictionary<string, TicTacToeGame> _games;
    private readonly object _playersLock = new();
    private readonly object _gamesLock = new();
    private TicTacToeGame? _currentGame;
    
    public GameCoordinator(TcpGameServer server)
    {
        _server = server;
        _players = new Dictionary<string, PlayerConnection>();
        _games = new Dictionary<string, TicTacToeGame>();
        
        // S'abonner aux événements du serveur
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }
    
    private void OnClientConnected(object? sender, PlayerConnection connection)
    {
        Console.WriteLine($"[Coordinator] Client connecté: {connection.RemoteEndPoint}");
    }
    
    private void OnClientDisconnected(object? sender, PlayerConnection connection)
    {
        if (connection.PlayerId != null)
        {
            lock (_playersLock)
            {
                _players.Remove(connection.PlayerId);
            }
            
            // Si le joueur était dans une partie, l'annuler
            if (_currentGame != null)
            {
                var playerSymbol = _currentGame.GetPlayerSymbol(connection.PlayerId);
                if (playerSymbol.HasValue)
                {
                    Console.WriteLine($"[Coordinator] Partie annulée: {connection.PlayerName} s'est déconnecté");
                    _currentGame.RemovePlayer(connection.PlayerId);
                    
                    // Notifier l'annulation
                    _ = BroadcastGameCancelledAsync(connection.PlayerName ?? "Un joueur");
                    _currentGame = null;
                }
            }
            
            // Notifier les autres joueurs
            _ = BroadcastPlayerLeftAsync(connection.PlayerId, connection.PlayerName ?? "Unknown");
            _ = BroadcastGameStateAsync();
            
            Console.WriteLine($"[Coordinator] Joueur déconnecté: {connection.PlayerName} ({connection.PlayerId})");
        }
    }
    
    private async void OnMessageReceived(object? sender, (PlayerConnection Connection, GameMessage Message) e)
    {
        var (connection, message) = e;
        
        try
        {
            await HandleMessageAsync(connection, message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coordinator] Erreur lors du traitement du message: {ex.Message}");
            await SendErrorAsync(connection, "PROCESSING_ERROR", ex.Message);
        }
    }
    
    private async Task HandleMessageAsync(PlayerConnection connection, GameMessage message)
    {
        switch (message.Type)
        {
            case MessageType.PlayerConnect:
                await HandlePlayerConnectAsync(connection, message);
                break;
                
            case MessageType.PlayerDisconnect:
                await HandlePlayerDisconnectAsync(connection, message);
                break;
                
            case MessageType.PlayerReady:
                await HandlePlayerReadyAsync(connection, message);
                break;
                
            case MessageType.MakeMove:
                await HandleMakeMoveAsync(connection, message);
                break;
                
            case MessageType.RequestRematch:
                await HandleRequestRematchAsync(connection, message);
                break;
                
            default:
                Console.WriteLine($"[Coordinator] Type de message inconnu: {message.Type}");
                break;
        }
    }
    
    private async Task HandlePlayerConnectAsync(PlayerConnection connection, GameMessage message)
    {
        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données de connexion manquantes");
            return;
        }
        
        // Vérifier qu'on ne dépasse pas 2 joueurs
        int currentPlayerCount;
        lock (_playersLock)
        {
            currentPlayerCount = _players.Count;
        }
        
        if (currentPlayerCount >= 2)
        {
            await SendErrorAsync(connection, "TOO_MANY_PLAYERS", "Le serveur est plein. Maximum 2 joueurs.");
            return;
        }
        
        var connectData = MessagePackSerializer.Deserialize<PlayerConnectData>(message.Data);
        
        // Générer un ID unique pour le joueur
        var playerId = Guid.NewGuid().ToString();
        connection.PlayerId = playerId;
        connection.PlayerName = connectData.PlayerName;
        
        lock (_playersLock)
        {
            _players[playerId] = connection;
        }
        
        Console.WriteLine($"[Coordinator] Joueur connecté: {connectData.PlayerName} (ID: {playerId})");
        
        // Envoyer un message de bienvenue
        await SendWelcomeMessageAsync(connection, playerId);
        
        // Notifier les autres joueurs
        await BroadcastPlayerJoinedAsync(playerId, connectData.PlayerName);
        
        // Envoyer l'état du jeu à tous
        await BroadcastGameStateAsync();
    }
    
    private async Task HandlePlayerDisconnectAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId != null)
        {
            lock (_playersLock)
            {
                _players.Remove(connection.PlayerId);
            }
            
            await BroadcastPlayerLeftAsync(connection.PlayerId, connection.PlayerName ?? "Unknown");
            await BroadcastGameStateAsync();
        }
    }
    
    private async Task HandlePlayerReadyAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null)
        {
            return;
        }
        
        // Vérifier si on peut démarrer une partie (2 joueurs prêts)
        List<PlayerConnection> readyPlayers;
        lock (_playersLock)
        {
            readyPlayers = _players.Values.Where(p => p.IsReady).ToList();
        }
        
        if (readyPlayers.Count >= 2)
        {
            await SendErrorAsync(connection, "TOO_MANY_PLAYERS", "Une partie est déjà en cours. Maximum 2 joueurs.");
            return;
        }
        
        connection.IsReady = true;
        Console.WriteLine($"[Coordinator] Joueur prêt: {connection.PlayerName}");
        
        lock (_playersLock)
        {
            readyPlayers = _players.Values.Where(p => p.IsReady).ToList();
        }
        
        if (readyPlayers.Count == 2 && _currentGame == null)
        {
            await StartNewGameAsync(readyPlayers[0], readyPlayers[1]);
        }
    }
    
    private async Task StartNewGameAsync(PlayerConnection player1, PlayerConnection player2)
    {
        var gameId = Guid.NewGuid().ToString();
        var game = new TicTacToeGame(gameId);
        
        game.AssignPlayer(player1.PlayerId!, player1.PlayerName!);
        game.AssignPlayer(player2.PlayerId!, player2.PlayerName!);
        
        lock (_gamesLock)
        {
            _games[gameId] = game;
            _currentGame = game;
        }
        
        Console.WriteLine($"[Coordinator] Nouvelle partie de morpion: {player1.PlayerName} (X) vs {player2.PlayerName} (O)");
        
        var startMessage = new GameMessage
        {
            Type = MessageType.GameStarted,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await _server.BroadcastMessageAsync(startMessage);
        await BroadcastTicTacToeStateAsync();
    }
    
    private async Task HandleMakeMoveAsync(PlayerConnection connection, GameMessage message)
    {
        if (message.Data == null || connection.PlayerId == null || _currentGame == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie en cours");
            return;
        }
        
        var move = MessagePackSerializer.Deserialize<TicTacToeMove>(message.Data);
        
        Console.WriteLine($"[Coordinator] {connection.PlayerName} joue en position {move.Position}");
        
        var result = _currentGame.MakeMove(connection.PlayerId, move.Position);
        
        if (!result.Success)
        {
            await SendErrorAsync(connection, "INVALID_MOVE", result.ErrorMessage ?? "Coup invalide");
            
            var invalidMessage = new GameMessage
            {
                Type = MessageType.InvalidMove,
                PlayerId = connection.PlayerId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = MessagePackSerializer.Serialize(result)
            };
            await connection.SendMessageAsync(invalidMessage);
            return;
        }
        
        // Diffuser le coup à tous les joueurs
        var moveMessage = new GameMessage
        {
            Type = MessageType.MoveMade,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(result)
        };
        await _server.BroadcastMessageAsync(moveMessage);
        
        // Vérifier si la partie est terminée
        if (_currentGame.IsGameOver())
        {
            await HandleGameOverAsync();
        }
        
        // Envoyer l'état du jeu mis à jour
        await BroadcastTicTacToeStateAsync();
    }
    
    private async Task HandleGameOverAsync()
    {
        if (_currentGame == null) return;
        
        var state = _currentGame.GetGameState();
        MessageType messageType;
        string statusMessage;
        
        if (state.Status == GameStatus.XWins)
        {
            messageType = MessageType.GameWon;
            statusMessage = $"{state.PlayerXName} (X) a gagné!";
        }
        else if (state.Status == GameStatus.OWins)
        {
            messageType = MessageType.GameWon;
            statusMessage = $"{state.PlayerOName} (O) a gagné!";
        }
        else
        {
            messageType = MessageType.GameDraw;
            statusMessage = "Match nul!";
        }
        
        Console.WriteLine($"[Coordinator] Fin de partie: {statusMessage}");
        
        var endMessage = new GameMessage
        {
            Type = messageType,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(state)
        };
        
        await _server.BroadcastMessageAsync(endMessage);
    }
    
    private async Task HandleRequestRematchAsync(PlayerConnection connection, GameMessage message)
    {
        if (_currentGame == null || connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie à rejouer");
            return;
        }
        
        Console.WriteLine($"[Coordinator] {connection.PlayerName} demande une revanche");
        
        // Réinitialiser la partie
        _currentGame.StartNewRound();
        
        var rematchMessage = new GameMessage
        {
            Type = MessageType.RematchOffered,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await _server.BroadcastMessageAsync(rematchMessage);
        
        // Redémarrer la partie
        var startMessage = new GameMessage
        {
            Type = MessageType.GameStarted,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await _server.BroadcastMessageAsync(startMessage);
        await BroadcastTicTacToeStateAsync();
    }
    
    private async Task SendWelcomeMessageAsync(PlayerConnection connection, string playerId)
    {
        var message = new GameMessage
        {
            Type = MessageType.ServerWelcome,
            PlayerId = playerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await connection.SendMessageAsync(message);
    }
    
    private async Task SendErrorAsync(PlayerConnection connection, string errorCode, string errorMessage)
    {
        var errorData = new ErrorData
        {
            ErrorCode = errorCode,
            Message = errorMessage
        };
        
        var message = new GameMessage
        {
            Type = MessageType.ServerError,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(errorData)
        };
        
        await connection.SendMessageAsync(message);
    }
    
    private async Task BroadcastPlayerJoinedAsync(string playerId, string playerName)
    {
        var playerInfo = new PlayerInfo
        {
            PlayerId = playerId,
            PlayerName = playerName,
            IsReady = false,
            IsConnected = true
        };
        
        var message = new GameMessage
        {
            Type = MessageType.PlayerJoined,
            PlayerId = playerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(playerInfo)
        };
        
        await _server.BroadcastMessageAsync(message);
    }
    
    private async Task BroadcastPlayerLeftAsync(string playerId, string playerName)
    {
        var playerInfo = new PlayerInfo
        {
            PlayerId = playerId,
            PlayerName = playerName,
            IsReady = false,
            IsConnected = false
        };
        
        var message = new GameMessage
        {
            Type = MessageType.PlayerLeft,
            PlayerId = playerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(playerInfo)
        };
        
        await _server.BroadcastMessageAsync(message);
    }
    
    private async Task BroadcastGameCancelledAsync(string disconnectedPlayerName)
    {
        var errorData = new ErrorData
        {
            ErrorCode = "GAME_CANCELLED",
            Message = $"{disconnectedPlayerName} s'est déconnecté. La partie est annulée."
        };
        
        var message = new GameMessage
        {
            Type = MessageType.GameEnded,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(errorData)
        };
        
        await _server.BroadcastMessageAsync(message);
    }
    
    private async Task BroadcastGameStateAsync()
    {
        List<PlayerInfo> playersList;
        
        lock (_playersLock)
        {
            playersList = _players.Values.Select(p => new PlayerInfo
            {
                PlayerId = p.PlayerId ?? "",
                PlayerName = p.PlayerName ?? "Unknown",
                IsReady = p.IsReady,
                IsConnected = p.IsConnected
            }).ToList();
        }
        
        var gameState = new GameStateData
        {
            GameId = _currentGame?.GameId ?? "waiting",
            Players = playersList,
            Status = _currentGame == null ? "Waiting" : _currentGame.Board.Status.ToString()
        };
        
        var message = new GameMessage
        {
            Type = MessageType.GameState,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(gameState)
        };
        
        await _server.BroadcastMessageAsync(message);
    }
    
    private async Task BroadcastTicTacToeStateAsync()
    {
        if (_currentGame == null) return;
        
        var state = _currentGame.GetGameState();
        
        var message = new GameMessage
        {
            Type = MessageType.GameState,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(state)
        };
        
        await _server.BroadcastMessageAsync(message);
    }
}
