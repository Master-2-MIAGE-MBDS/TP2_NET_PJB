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
    private readonly Dictionary<string, string> _playerGameMap;
    private readonly Queue<PlayerConnection> _lobbyQueue;
    private readonly object _playersLock = new();
    private readonly object _gamesLock = new();
    private readonly object _lobbyLock = new();
    
    public GameCoordinator(TcpGameServer server)
    {
        _server = server;
        _players = new Dictionary<string, PlayerConnection>();
        _games = new Dictionary<string, TicTacToeGame>();
        _playerGameMap = new Dictionary<string, string>();
        _lobbyQueue = new Queue<PlayerConnection>();
        
        // S'abonner aux événements du serveur
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }
    
    private void OnClientConnected(object? sender, PlayerConnection connection)
    {
        Console.WriteLine($"[Coordinator] Client connecté: {connection.RemoteEndPoint}");
    }
    
    private async void OnClientDisconnected(object? sender, PlayerConnection connection)
    {
        await HandlePlayerDisconnectionAsync(connection);
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

            case MessageType.CreateGame:
                await HandleCreateGameAsync(connection, message);
                break;

            case MessageType.ListGames:
                await HandleListGamesAsync(connection);
                break;

            case MessageType.JoinGame:
                await HandleJoinGameAsync(connection, message);
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
    }

    private async Task HandleCreateGameAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null || connection.PlayerName == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité du joueur inconnue");
            return;
        }

        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données de création manquantes");
            return;
        }

        var createRequest = MessagePackSerializer.Deserialize<CreateGameRequest>(message.Data);
        var local_name = string.IsNullOrWhiteSpace(createRequest.GameName)
            ? $"Salle-{Guid.NewGuid().ToString()[..8]}"
            : createRequest.GameName.Trim();

        var gameId = Guid.NewGuid().ToString();
        var game = new TicTacToeGame(gameId, local_name);
        game.AssignPlayer(connection.PlayerId, connection.PlayerName);

        lock (_gamesLock)
        {
            _games[gameId] = game;
            _playerGameMap[connection.PlayerId] = gameId;
        }

        var response = new GameMessage
        {
            Type = MessageType.GameCreated,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameCreatedData { Game = game.ToSummary() })
        };

        await connection.SendMessageAsync(response);
    }

    private async Task HandleListGamesAsync(PlayerConnection connection)
    {
        List<GameSummary> local_games;
        lock (_gamesLock)
        {
            local_games = _games.Values
                .Where(g => g.Board.Status == GameStatus.WaitingForPlayers)
                .Select(g => g.ToSummary())
                .ToList();
        }

        var response = new GameMessage
        {
            Type = MessageType.GameList,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameListResponse { Games = local_games })
        };

        await connection.SendMessageAsync(response);
    }

    private async Task HandleJoinGameAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null || connection.PlayerName == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité du joueur inconnue");
            return;
        }

        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données de connexion manquantes");
            return;
        }

        var joinRequest = MessagePackSerializer.Deserialize<JoinGameRequest>(message.Data);
        TicTacToeGame? local_game;
        lock (_gamesLock)
        {
            _games.TryGetValue(joinRequest.GameId, out local_game);
        }

        if (local_game == null)
        {
            await SendErrorAsync(connection, "GAME_NOT_FOUND", "Partie introuvable");
            return;
        }

        if (!local_game.AssignPlayer(connection.PlayerId, connection.PlayerName))
        {
            await SendErrorAsync(connection, "GAME_FULL", "La partie est complète");
            return;
        }

        lock (_gamesLock)
        {
            _playerGameMap[connection.PlayerId] = local_game.GameId;
        }

        var joinResponse = new GameMessage
        {
            Type = MessageType.GameJoined,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameJoinedData
            {
                Game = local_game.ToSummary(),
                Role = local_game.PlayerOId == connection.PlayerId ? "O" : "X"
            })
        };

        await connection.SendMessageAsync(joinResponse);

        // Prévenir l'autre joueur si présent
        var otherPlayerId = local_game.PlayerXId == connection.PlayerId ? local_game.PlayerOId : local_game.PlayerXId;
        if (!string.IsNullOrEmpty(otherPlayerId))
        {
            PlayerConnection? other;
            lock (_playersLock)
            {
                _players.TryGetValue(otherPlayerId, out other);
            }

            if (other != null && other.IsConnected)
            {
                // Notifier l'autre joueur du join (mais pas de GameState)
                var playerJoinedMsg = new GameMessage
                {
                    Type = MessageType.PlayerJoined,
                    PlayerId = connection.PlayerId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Data = MessagePackSerializer.Serialize(new PlayerInfo
                    {
                        PlayerId = connection.PlayerId,
                        PlayerName = connection.PlayerName,
                        IsConnected = true,
                        IsReady = false
                    })
                };
                await other.SendMessageAsync(playerJoinedMsg);
            }
        }

        // Démarrer la partie SI tous les joueurs sont présents
        if (local_game.CanStart())
        {
            local_game.Board.Status = GameStatus.InProgress;

            var startMessage = new GameMessage
            {
                Type = MessageType.GameStarted,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await SendToGameAsync(local_game, startMessage);
            await BroadcastTicTacToeStateAsync(local_game);
        }
    }
    
    private void EnqueuePlayerToLobby(PlayerConnection connection)
    {
        if (connection.PlayerId == null)
        {
            return;
        }

        lock (_lobbyLock)
        {
            if (_playerGameMap.ContainsKey(connection.PlayerId))
            {
                return; // déjà dans une partie
            }

            if (_lobbyQueue.Any(p => p.PlayerId == connection.PlayerId))
            {
                return; // déjà en attente
            }

            _lobbyQueue.Enqueue(connection);
        }
    }

    private void RemoveFromLobby(string playerId)
    {
        lock (_lobbyLock)
        {
            if (_lobbyQueue.Count == 0) return;

            var remaining = _lobbyQueue.Where(p => p.PlayerId != playerId).ToList();
            _lobbyQueue.Clear();
            foreach (var player in remaining)
            {
                _lobbyQueue.Enqueue(player);
            }
        }
    }

    private async Task TryStartGamesAsync()
    {
        var pairs = new List<(PlayerConnection First, PlayerConnection Second)>();

        lock (_lobbyLock)
        {
            while (_lobbyQueue.Count >= 2)
            {
                var p1 = _lobbyQueue.Dequeue();
                var p2 = _lobbyQueue.Dequeue();
                pairs.Add((p1, p2));
            }
        }

        foreach (var (first, second) in pairs)
        {
            await StartNewGameAsync(first, second);
        }
    }

    private async Task StartNewGameAsync(PlayerConnection player1, PlayerConnection player2)
    {
        var gameId = Guid.NewGuid().ToString();
        var game = new TicTacToeGame(gameId, $"Match-{gameId[..8]}");
        
        game.AssignPlayer(player1.PlayerId!, player1.PlayerName!);
        game.AssignPlayer(player2.PlayerId!, player2.PlayerName!);
        game.Board.Status = GameStatus.InProgress;
        
        lock (_gamesLock)
        {
            _games[gameId] = game;
            _playerGameMap[player1.PlayerId!] = gameId;
            _playerGameMap[player2.PlayerId!] = gameId;
        }
        
        Console.WriteLine($"[Coordinator] Nouvelle partie de morpion: {player1.PlayerName} (X) vs {player2.PlayerName} (O)");
        Console.WriteLine($"[Coordinator] État du jeu: {game.Board.Status}, Joueur courant: {game.Board.CurrentPlayer}");
        
        var startMessage = new GameMessage
        {
            Type = MessageType.GameStarted,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        Console.WriteLine($"[Coordinator] Envoi de GameStarted...");
        await SendToGameAsync(game, startMessage);
        
        Console.WriteLine($"[Coordinator] Envoi du GameState...");
        await BroadcastTicTacToeStateAsync(game);
    }
    
    private async Task HandlePlayerDisconnectAsync(PlayerConnection connection, GameMessage message)
    {
        await HandlePlayerDisconnectionAsync(connection);
    }
    
    private async Task HandlePlayerReadyAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null)
        {
            return;
        }

        connection.IsReady = true;
        Console.WriteLine($"[Coordinator] Joueur prêt: {connection.PlayerName}");
        
        // La partie démarre automatiquement quand 2 joueurs se connectent
        // Cette méthode est conservée pour compatibilité mais n'est plus critique
    }
    
    private async Task HandleMakeMoveAsync(PlayerConnection connection, GameMessage message)
    {
        if (message.Data == null || connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie en cours");
            return;
        }
        
        var game = GetGameForPlayer(connection.PlayerId);
        if (game == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie en cours");
            return;
        }

        var move = MessagePackSerializer.Deserialize<TicTacToeMove>(message.Data);
        
        Console.WriteLine($"[Coordinator] {connection.PlayerName} joue en position {move.Position}");
        
        var result = game.MakeMove(connection.PlayerId, move.Position);
        
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
        await SendToGameAsync(game, moveMessage);
        
        if (game.IsGameOver())
        {
            await HandleGameOverAsync(game);
        }

        await BroadcastTicTacToeStateAsync(game);
    }
    
    private async Task HandleGameOverAsync(TicTacToeGame game)
    {
        var state = game.GetGameState();
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
        
        await SendToGameAsync(game, endMessage);
    }
    
    private async Task HandleRequestRematchAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie à rejouer");
            return;
        }

        var game = GetGameForPlayer(connection.PlayerId);
        if (game == null)
        {
            await SendErrorAsync(connection, "NO_GAME", "Aucune partie à rejouer");
            return;
        }
        
        Console.WriteLine($"[Coordinator] {connection.PlayerName} demande une revanche");
        
        // Réinitialiser la partie
        game.StartNewRound();
        
        var rematchMessage = new GameMessage
        {
            Type = MessageType.RematchOffered,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await SendToGameAsync(game, rematchMessage);
        
        // Redémarrer la partie
        var startMessage = new GameMessage
        {
            Type = MessageType.GameStarted,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        await SendToGameAsync(game, startMessage);
        await BroadcastTicTacToeStateAsync(game);
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
        // Ne broadcast que si le joueur est dans une game
        var game = GetGameForPlayer(playerId);
        if (game == null)
        {
            return; // Joueur pas encore dans une game
        }

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
        
        // Envoyer SEULEMENT aux joueurs de cette partie
        await SendToGameAsync(game, message);
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
    
    private async Task BroadcastGameCancelledAsync(TicTacToeGame game, string disconnectedPlayerName)
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
        
        await SendToGameAsync(game, message);
    }
    
    private async Task BroadcastTicTacToeStateAsync(TicTacToeGame game)
    {
        var state = game.GetGameState();
        
        var message = new GameMessage
        {
            Type = MessageType.GameState,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(state)
        };
        
        await SendToGameAsync(game, message);
    }

    private TicTacToeGame? GetGameForPlayer(string playerId)
    {
        string? gameId = GetPlayerGameId(playerId);
        if (gameId == null)
        {
            return null;
        }

        lock (_gamesLock)
        {
            _games.TryGetValue(gameId, out var game);
            return game;
        }
    }

    private string? GetPlayerGameId(string playerId)
    {
        lock (_gamesLock)
        {
            return _playerGameMap.TryGetValue(playerId, out var gameId) ? gameId : null;
        }
    }

    private List<string> GetPlayerIds(TicTacToeGame game)
    {
        var ids = new List<string>();
        if (!string.IsNullOrEmpty(game.PlayerXId)) ids.Add(game.PlayerXId);
        if (!string.IsNullOrEmpty(game.PlayerOId) && game.PlayerOId != game.PlayerXId) ids.Add(game.PlayerOId);
        return ids;
    }

    private async Task SendToGameAsync(TicTacToeGame game, GameMessage message)
    {
        var playerIds = GetPlayerIds(game);
        if (playerIds.Count == 0)
        {
            return;
        }

        await _server.SendMessageToPlayersAsync(playerIds, message);
    }

    private async Task HandlePlayerDisconnectionAsync(PlayerConnection connection)
    {
        if (connection.PlayerId == null)
        {
            return;
        }

        RemoveFromLobby(connection.PlayerId);
        await RemovePlayerFromGameAsync(connection);

        lock (_playersLock)
        {
            _players.Remove(connection.PlayerId);
        }

        await BroadcastPlayerLeftAsync(connection.PlayerId, connection.PlayerName ?? "Unknown");
        Console.WriteLine($"[Coordinator] Joueur déconnecté: {connection.PlayerName} ({connection.PlayerId})");
    }

    private async Task RemovePlayerFromGameAsync(PlayerConnection connection)
    {
        if (connection.PlayerId == null)
        {
            return;
        }

        var game = GetGameForPlayer(connection.PlayerId);
        if (game == null)
        {
            return;
        }

        var participants = GetPlayerIds(game);
        game.RemovePlayer(connection.PlayerId);

        await BroadcastGameCancelledAsync(game, connection.PlayerName ?? "Un joueur");

        lock (_gamesLock)
        {
            _games.Remove(game.GameId);
            foreach (var playerId in participants)
            {
                _playerGameMap.Remove(playerId);
            }
        }

        foreach (var playerId in participants)
        {
            if (playerId == connection.PlayerId)
            {
                continue;
            }

            PlayerConnection? opponent;
            lock (_playersLock)
            {
                _players.TryGetValue(playerId, out opponent);
            }

            if (opponent != null && opponent.IsConnected)
            {
                EnqueuePlayerToLobby(opponent);
            }
        }

        await TryStartGamesAsync();
    }
}
