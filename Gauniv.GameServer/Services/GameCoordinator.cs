using MessagePack;
using Gauniv.GameServer.Models;
using Gauniv.GameServer.Network;

namespace Gauniv.GameServer.Services;

public enum GameStatus
{
    WAITING,
    IN_PROGRESS,
    FINISHED
}

// Représente une room de jeu
public class GameRoom
{
    public string GameId { get; set; }
    public string Name { get; set; }

    // Joueurs et spectateurs
    public List<string> PlayerIds { get; set; } = new();
    public List<string> SpectatorIds { get; set; } = new();
    public Dictionary<string, string> CharactersName { get; set; } = new();

    // État du jeu
    public GameStatus GameStatus { get; set; } = GameStatus.WAITING;
    public string? WinnerId { get; set; }

    // Stockage des coups: playerId -> tableau de 3 positions (null si pas encore joué)
    public Dictionary<string, int?[]> PlayerMoves { get; set; } = new();

    public GameRoom(string gameId, string name)
    {
        GameId = gameId;
        Name = name;
    }

    public int TotalPlayers => PlayerIds.Count + SpectatorIds.Count;

    // Initialise les coups pour un joueur
    public void InitializePlayer(string playerId)
    {
        if (!PlayerMoves.ContainsKey(playerId))
            PlayerMoves[playerId] = new int?[3];
    }

    // Retourne toutes les positions occupées sur la grille
    public HashSet<int> GetOccupiedPositions()
    {
        return PlayerMoves.Values
            .SelectMany(moves => moves)
            .Where(pos => pos.HasValue)
            .Select(pos => pos!.Value)
            .ToHashSet();
    }
}

// Coordonnateur qui gère les rooms et diffuse les états du jeu
public class GameCoordinator
{
    private readonly TcpGameServer _server;
    private readonly Dictionary<string, PlayerConnection> _allConnections;
    private readonly Dictionary<string, GameRoom> _rooms;
    private readonly Dictionary<string, string> _playerRoomMap; // playerId -> roomId
    private readonly object _connectionsLock = new();
    private readonly object _roomsLock = new();
    private readonly object _mapLock = new();
    
    public GameCoordinator(TcpGameServer server)
    {
        _server = server;
        _allConnections = new Dictionary<string, PlayerConnection>();
        _rooms = new Dictionary<string, GameRoom>();
        _playerRoomMap = new Dictionary<string, string>();
        
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
        
        Console.WriteLine($"[Coordinator] Message reçu - Type: {message.Type}, PlayerId: {message.PlayerId ?? "null"}, Data: {(message.Data != null ? $"{message.Data.Length} bytes" : "null")}");
        
        try
        {
            await HandleMessageAsync(connection, message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coordinator] Erreur lors du traitement du message: {ex.Message}");
            Console.WriteLine($"[Coordinator] StackTrace: {ex.StackTrace}");
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

            case MessageType.CreateGame:
                await HandleCreateGameAsync(connection, message);
                break;

            case MessageType.ListGames:
                await HandleListGamesAsync(connection);
                break;

            case MessageType.JoinGame:
                await HandleJoinGameAsync(connection, message);
                break;
                
            // case MessageType.GameState:
            //     await HandleGameStateAsync(connection, message);
            //     break;
                
            case MessageType.MakeMove:
                await HandleMakeMoveAsync(connection, message);
                break;
                
            case MessageType.SyncGameState:
                await HandleSyncGameStateAsync(connection, message);
                break;
                
            default:
                Console.WriteLine($"[Coordinator] Type de message inconnu: {message.Type}");
                break;
        }
    }
    
    /// <summary>
    /// Gère la connexion d'un client
    /// </summary>
    private async Task HandlePlayerConnectAsync(PlayerConnection connection, GameMessage message)
    {
        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données de connexion manquantes");
            return;
        }
        
        Console.WriteLine($"[Coordinator] Data reçue - Taille: {message.Data.Length} bytes");
        Console.WriteLine($"[Coordinator] Data hex: {BitConverter.ToString(message.Data)}");
        
        PlayerConnectData connectData;
        try
        {
            connectData = MessagePackSerializer.Deserialize<PlayerConnectData>(message.Data);
            Console.WriteLine($"[Coordinator] PlayerConnectData désérialisé: Name={connectData.PlayerName}, UserId={connectData.UserId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coordinator] Erreur de désérialisation PlayerConnectData: {ex.Message}");
            Console.WriteLine($"[Coordinator] StackTrace: {ex.StackTrace}");
            await SendErrorAsync(connection, "DESERIALIZATION_ERROR", $"Impossible de désérialiser PlayerConnectData: {ex.Message}");
            return;
        }
        
        // Générer un ID unique
        var clientId = Guid.NewGuid().ToString();
        connection.PlayerId = clientId;
        connection.PlayerName = connectData.PlayerName;
        
        lock (_connectionsLock)
        {
            _allConnections[clientId] = connection;
        }
        
        Console.WriteLine($"[Coordinator] Joueur connecté: {connectData.PlayerName} (ID: {clientId})");
        
        // Envoyer un message de bienvenue
        await SendWelcomeMessageAsync(connection, clientId);
    }

    /// <summary>
    /// Crée une nouvelle room
    /// </summary>
    private async Task HandleCreateGameAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null || connection.PlayerName == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité inconnue");
            return;
        }

        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données manquantes");
            return;
        }

        var createRequest = MessagePackSerializer.Deserialize<CreateGameRequest>(message.Data);
        var roomName = string.IsNullOrWhiteSpace(createRequest.GameName)
            ? $"Room-{Guid.NewGuid().ToString()[..8]}"
            : createRequest.GameName.Trim();

        var gameId = Guid.NewGuid().ToString();
        var room = new GameRoom(gameId, roomName);
        room.PlayerIds.Add(connection.PlayerId);
        room.InitializePlayer(connection.PlayerId); // Initialiser les coups du créateur
        room.CharactersName[connection.PlayerId] = createRequest.Character;

        lock (_roomsLock)
        {
            _rooms[gameId] = room;
        }

        lock (_mapLock)
        {
            _playerRoomMap[connection.PlayerId] = gameId;
        }

        Console.WriteLine($"[Coordinator] Room créée: '{roomName}' (ID: {gameId[..8]}...)");

        var response = new GameMessage
        {
            Type = MessageType.GameCreated,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameCreatedData
            {
                Game = new GameSummary
                {
                    GameId = gameId,
                    Name = roomName,
                    PlayerCount = room.PlayerIds.Count,
                    MaxPlayers = 2,
                    SpectatorCount = 0,
                    Status = room.GameStatus.ToString()
                }
            })
        };

        await connection.SendMessageAsync(response);
    }

    /// <summary>
    /// Liste les rooms disponibles
    /// </summary>
    private async Task HandleListGamesAsync(PlayerConnection connection)
    {
        List<GameSummary> games;
        int totalRooms;

        lock (_roomsLock)
        {
            totalRooms = _rooms.Count;
            games = _rooms.Values
                .Select(r => new GameSummary
                {
                    GameId = r.GameId,
                    Name = r.Name,
                    PlayerCount = r.PlayerIds.Count,
                    MaxPlayers = 2,
                    Status = r.GameStatus.ToString(),
                    SpectatorCount = r.SpectatorIds.Count
                })
                .ToList();
        }

        Console.WriteLine($"[Coordinator] ListGames: {games.Count} rooms affichées (total: {totalRooms})");

        var response = new GameMessage
        {
            Type = MessageType.GameList,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameListResponse { Games = games })
        };

        await connection.SendMessageAsync(response);
    }

    /// <summary>
    /// Rejoint une room
    /// </summary>
    private async Task HandleJoinGameAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null || connection.PlayerName == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité inconnue");
            return;
        }

        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données manquantes");
            return;
        }

        var joinRequest = MessagePackSerializer.Deserialize<JoinGameRequest>(message.Data);
        
        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(joinRequest.GameId, out room);
        }

        if (room == null)
        {
            await SendErrorAsync(connection, "GAME_NOT_FOUND", "Room introuvable");
            return;
        }

        bool isSpectator;
        if (room.PlayerIds.Count >= 2)
        {
            room.SpectatorIds.Add(connection.PlayerId);
            isSpectator = true;
            Console.WriteLine($"[Coordinator] Spectateur rejoint: '{room.Name}'");
        }
        else
        {
            room.PlayerIds.Add(connection.PlayerId);
            isSpectator = false;
            Console.WriteLine($"[Coordinator] Joueur rejoint: '{room.Name}' ({room.PlayerIds.Count}/2)");
        }

        lock (_mapLock)
        {
            _playerRoomMap[connection.PlayerId] = room.GameId;
        }

        // Initialiser les coups du joueur s'il n'est pas spectateur
        if (room.PlayerIds.Count <= 2)
        {
            room.InitializePlayer(connection.PlayerId);
            room.CharactersName[connection.PlayerId] = joinRequest.Character;
        }
        
        // Démarrer la partie si 2 joueurs
        if (room.PlayerIds.Count == 2 && room.GameStatus == GameStatus.WAITING)
        {
            room.GameStatus = GameStatus.IN_PROGRESS;
            Console.WriteLine($"[Coordinator] Partie démarrée: '{room.Name}'");
        }

        var response = new GameMessage
        {
            Type = MessageType.GameJoined,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(new GameJoinedData
            {
                Game = new GameSummary
                {
                    GameId = room.GameId,
                    Name = room.Name,
                    PlayerCount = room.PlayerIds.Count,
                    MaxPlayers = 2,
                    Status = room.GameStatus.ToString(),
                    SpectatorCount = room.SpectatorIds.Count
                },
                Role = isSpectator ? "SPECTATEUR" : "JOUEUR"
            })
        };

        await connection.SendMessageAsync(response);

        // Après le join, envoyer l'état complet du jeu à toute la room (joueur hôte compris)
        var syncData = BuildSyncData(room);
        var syncMessage = new GameMessage
        {
            Type = MessageType.GameStateSynced,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(syncData)
        };

        await BroadcastToRoomAsync(room.GameId, syncMessage);
    }
    private async Task HandleGameStateAsync(PlayerConnection connection, GameMessage message)
    {
        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "État du jeu manquant");
            return;
        }

        if (connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité inconnue");
            return;
        }

        // Trouver la room du joueur
        string? roomId;
        lock (_mapLock)
        {
            _playerRoomMap.TryGetValue(connection.PlayerId, out roomId);
        }

        if (roomId == null)
        {
            await SendErrorAsync(connection, "NO_ROOM", "Pas en room");
            return;
        }

        try
        {
            // Deserialiser l'état du jeu
            var lastMoves = MessagePackSerializer.Deserialize<int>(message.Data);

            Console.WriteLine($"[Coordinator] État du jeu reçu de {connection.PlayerName}: {lastMoves}");


            await BroadcastToRoomExceptAsync(roomId, connection.PlayerId, new GameMessage
            {
                Type = MessageType.GameState,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = message.Data,
                PlayerId = connection.PlayerId
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coordinator] Erreur: {ex.Message}");
            await SendErrorAsync(connection, "DESERIALIZATION_ERROR", ex.Message);
        }
    }


    // Méthode redondante supprimée: utiliser BroadcastToRoomExceptAsync

    /// <summary>
    /// Envoie un message de bienvenue
    /// </summary>
    private async Task SendWelcomeMessageAsync(PlayerConnection connection, string clientId)
    {
        var message = new GameMessage
        {
            Type = MessageType.ServerWelcome,
            PlayerId = clientId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await connection.SendMessageAsync(message);
    }

    /// <summary>
    /// Envoie un message d'erreur
    /// </summary>
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

    /// <summary>
    /// Envoie un message de Victoire
    /// </summary>
    
    private async Task SendGameWonAsync(PlayerConnection connection, GameWonData winData)
    {
        var message = new GameMessage
        {
            Type = MessageType.GameWon,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(winData)
        };

        await connection.SendMessageAsync(message);
    }

    private async Task SendGameLooseAsyncToPlayers(string roomId, GameWonData looseData)
    {
        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null) return;


        var looserIds = room.PlayerIds.Where(id => id != looseData.WinnerId).ToList();

        var tasks = new List<Task>();
        foreach (var playerId in looserIds)
        {
            PlayerConnection? conn;
            lock (_connectionsLock)
            {
                _allConnections.TryGetValue(playerId, out conn);
            }

            if (conn != null && conn.IsConnected)
            {
                tasks.Add(SendGameLooseAsync(conn, looseData));
            }
        }
        await Task.WhenAll(tasks);
    }

    private async Task SendGameLooseAsync(PlayerConnection connection, GameWonData looseData)
    {
        var message = new GameMessage
        {
            Type = MessageType.GameLoose,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(looseData)
        };

        await connection.SendMessageAsync(message);
    }


    /// <summary>
    /// Gère la déconnexion d'un client
    /// </summary>
    private async Task HandlePlayerDisconnectAsync(PlayerConnection connection, GameMessage message)
    {
        await HandlePlayerDisconnectionAsync(connection);
    }

    /// <summary>
    /// Gère la déconnexion d'un client (interne)
    /// </summary>
    private async Task HandlePlayerDisconnectionAsync(PlayerConnection connection)
    {
        if (connection.PlayerId == null) return;

        // Retirer des connections
        lock (_connectionsLock)
        {
            _allConnections.Remove(connection.PlayerId);
        }

        // Retirer de la room si présent
        string? roomId;
        lock (_mapLock)
        {
            _playerRoomMap.TryGetValue(connection.PlayerId, out roomId);
            _playerRoomMap.Remove(connection.PlayerId);
        }

        if (roomId != null)
        {
            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(roomId, out var room))
                {
                    room.PlayerIds.Remove(connection.PlayerId);
                    room.SpectatorIds.Remove(connection.PlayerId);

                    // Supprimer la room si vide
                    if (room.TotalPlayers == 0)
                    {
                        _rooms.Remove(roomId);
                        Console.WriteLine($"[Coordinator] Room vide supprimée: {roomId[..8]}...");
                    }
                }
            }
        }

        Console.WriteLine($"[Coordinator] Joueur déconnecté: {connection.PlayerName}");
    }


    private void ShowCurrentGameState(GameRoom room)
    {

        string[][] tableInit = new string[3][]
        {
            new string[3] {"0","1","2"},
            new string[3] {"3","4","5"},
            new string[3] {"6","7","8"}
        };

        Console.WriteLine($"[Coordinator] État actuel de la partie '{room.Name}':");
        for (int i = 0; i < room.PlayerIds.Count; i++)
        {
            var playerId = room.PlayerIds[i];
            var moves = room.PlayerMoves[playerId];
            var playerName = GetPlayerName(playerId);
            for (int j = 0; j < moves.Length; j++)
            {
                if (moves[j] is int pos)
                {
                    int row = pos / 3;
                    int col = pos % 3;
                    tableInit[row][col] = playerName;
                }
            }
        }

        var maxSizeName = room.PlayerIds
            .Select(id => GetPlayerName(id).Length)
            .Max();

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Console.Write(tableInit[i][j].PadRight(maxSizeName + 2));
            }
            Console.WriteLine();
        }
            
    }


    /// <summary>
    /// Save un coup joué par un joueur
    /// </summary>
    
    private void SaveMove(string playerId, int position, GameRoom room)
    {
        
        if (room == null) return;

        // Enregistrer le coup
        var moves = room.PlayerMoves[playerId];
        // Décaler et ajouter en fin
        moves[0] = moves[1];
        moves[1] = moves[2];
        moves[2] = position;

    }

    
    /// <summary>
    /// Gère un coup joué par un joueur
    /// </summary>
    private async Task HandleMakeMoveAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité inconnue");
            return;
        }

        if (message.Data == null)
        {
            await SendErrorAsync(connection, "INVALID_DATA", "Données manquantes");
            return;
        }

        // Trouver la room du joueur
        string? roomId;
        lock (_mapLock)
        {
            _playerRoomMap.TryGetValue(connection.PlayerId, out roomId);
        }

        if (roomId == null)
        {
            await SendErrorAsync(connection, "NO_ROOM", "Pas en room");
            return;
        }

        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null)
        {
            await SendErrorAsync(connection, "ROOM_NOT_FOUND", "Room introuvable");
            return;
        }

        // Vérifier que le joueur est bien un joueur actif (pas spectateur)
        if (!room.PlayerIds.Contains(connection.PlayerId))
        {
            await SendMoveRejectedAsync(connection, -1, "Vous êtes spectateur");
            return;
        }

        // Vérifier que la partie est en cours
        if (room.GameStatus != GameStatus.IN_PROGRESS)
        {
            await SendMoveRejectedAsync(connection, -1, "La partie n'est pas en cours");
            return;
        }

        var moveData = MessagePackSerializer.Deserialize<MakeMoveData>(message.Data);
        int position = moveData.Position;


        // Valider le coup
        if (position < 0 || position > 8)
        {
            await SendMoveRejectedAsync(connection, position, "Position invalide (doit être entre 0 et 8)");
            return;
        }

        var occupied = room.GetOccupiedPositions();
        if (occupied.Contains(position))
        {
            await SendMoveRejectedAsync(connection, position, "Case déjà occupée");
            return;
        }

        

        // Enregistrer le coup
        SaveMove(connection.PlayerId, position, room);
        Console.WriteLine($"[Coordinator] Coup accepté: {connection.PlayerName} -> position {position} dans '{room.Name}'");

        // Envoyer l'acceptation au joueur
        var acceptedData = new MoveAcceptedData
        {
            PlayerId = connection.PlayerId,
            Position = position,
        };

        await connection.SendMessageAsync(new GameMessage
        {
            Type = MessageType.MoveAccepted,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(acceptedData)
        });

        // Broadcaster le coup à tous les autres joueurs et spectateurs
        await BroadcastToRoomExceptAsync(roomId, connection.PlayerId, new GameMessage
        {
            Type = MessageType.MoveMade,
            PlayerId = connection.PlayerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(acceptedData)
        });

        // Envoyer l'état complet du jeu aux spectateurs
        await BroadcastSyncToSpectatorsAsync(roomId);

        ShowCurrentGameState(room);

        // Vérifier la victoire
        var winner = CheckWinner(room);
        if (winner != null)
        {
            room.GameStatus = GameStatus.FINISHED;
            room.WinnerId = winner.PlayerId;
            
            var winData = new GameWonData
            {
                WinnerId = winner.PlayerId,
                WinnerName = GetPlayerName(winner.PlayerId),
                WinningPositions = winner.WinningPositions
            };

            
            Console.WriteLine($"[Coordinator] Victoire de {winData.WinnerName}!");
            
            await SendGameWonAsync(connection, winData);
            // Envoyer la défaite aux autres joueurs
            await SendGameLooseAsyncToPlayers(roomId, winData);

            // Broadcast à toute la room la fin de la partie
            await BroadcastToRoomAsync(roomId, new GameMessage
            {
                Type = MessageType.GameEnded,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = MessagePackSerializer.Serialize(winData)
            });
        }
        
    }
    
    /// <summary>
    /// Synchronise l'état du jeu avec un client
    /// </summary>
    private async Task HandleSyncGameStateAsync(PlayerConnection connection, GameMessage message)
    {
        if (connection.PlayerId == null)
        {
            await SendErrorAsync(connection, "NOT_CONNECTED", "Identité inconnue");
            return;
        }

        // Trouver la room du joueur
        string? roomId;
        lock (_mapLock)
        {
            _playerRoomMap.TryGetValue(connection.PlayerId, out roomId);
        }

        if (roomId == null)
        {
            await SendErrorAsync(connection, "NO_ROOM", "Pas en room");
            return;
        }

        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null)
        {
            await SendErrorAsync(connection, "ROOM_NOT_FOUND", "Room introuvable");
            return;
        }

        var syncData = BuildSyncData(room);

        Console.WriteLine($"[Coordinator] Synchronisation envoyée à {connection.PlayerName}");

        await connection.SendMessageAsync(new GameMessage
        {
            Type = MessageType.GameStateSynced,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(syncData)
        });
    }

    private GameStateSyncedData BuildSyncData(GameRoom room)
    {
        var playerNames = new Dictionary<string, string>();
        foreach (var playerId in room.PlayerIds)
        {
            playerNames[playerId] = GetPlayerName(playerId);
        }

        return new GameStateSyncedData
        {
            PlayerIds = room.PlayerIds,
            PlayerMoves = room.PlayerMoves,
            GameStatus = room.GameStatus.ToString(),
            WinnerId = room.WinnerId,
            CharactersName = room.CharactersName,
            PlayerNames = playerNames
        };
    }
    
    /// <summary>
    /// Envoie un rejet de coup
    /// </summary>
    private async Task SendMoveRejectedAsync(PlayerConnection connection, int position, string reason)
    {
        var rejectedData = new MoveRejectedData
        {
            Position = position,
            Reason = reason
        };

        await connection.SendMessageAsync(new GameMessage
        {
            Type = MessageType.MoveRejected,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(rejectedData)
        });
    }
    
    /// <summary>
    /// Broadcast à toute la room sauf un joueur
    /// </summary>
    private async Task BroadcastToRoomExceptAsync(string roomId, string exceptPlayerId, GameMessage message)
    {
        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null) return;

        var allIds = new List<string>();
        allIds.AddRange(room.PlayerIds);
        allIds.AddRange(room.SpectatorIds);

        var tasks = new List<Task>();
        foreach (var playerId in allIds)
        {
            if (playerId == exceptPlayerId) continue;
            
            PlayerConnection? conn;
            lock (_connectionsLock)
            {
                _allConnections.TryGetValue(playerId, out conn);
            }

            if (conn != null && conn.IsConnected)
            {
                tasks.Add(conn.SendMessageAsync(message));
            }
        }

        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Broadcast à toute la room
    /// </summary>
    private async Task BroadcastToRoomAsync(string roomId, GameMessage message)
    {
        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null) return;

        var allIds = new List<string>();
        allIds.AddRange(room.PlayerIds);
        allIds.AddRange(room.SpectatorIds);

        var tasks = new List<Task>();
        foreach (var playerId in allIds)
        {
            PlayerConnection? conn;
            lock (_connectionsLock)
            {
                _allConnections.TryGetValue(playerId, out conn);
            }

            if (conn != null && conn.IsConnected)
            {
                tasks.Add(conn.SendMessageAsync(message));
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task BroadcastSyncToSpectatorsAsync(string roomId)
    {
        GameRoom? room;
        lock (_roomsLock)
        {
            _rooms.TryGetValue(roomId, out room);
        }

        if (room == null) return;

        var syncData = BuildSyncData(room);
        var message = new GameMessage
        {
            Type = MessageType.GameStateSynced,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = MessagePackSerializer.Serialize(syncData)
        };

        var tasks = new List<Task>();
        foreach (var playerId in room.SpectatorIds)
        {
            PlayerConnection? conn;
            lock (_connectionsLock)
            {
                _allConnections.TryGetValue(playerId, out conn);
            }

            if (conn != null && conn.IsConnected)
            {
                tasks.Add(conn.SendMessageAsync(message));
            }
        }

        await Task.WhenAll(tasks);
    }
    
    /// <summary>
    /// Récupère le nom d'un joueur
    /// </summary>
    private string GetPlayerName(string playerId)
    {
        PlayerConnection? conn;
        lock (_connectionsLock)
        {
            _allConnections.TryGetValue(playerId, out conn);
        }
        return conn?.PlayerName ?? "Inconnu";
    }
    
    /// <summary>
    /// Vérifie s'il y a un gagnant
    /// </summary>
    private WinnerInfo? CheckWinner(GameRoom room)
    {
        // Combinaisons gagnantes (lignes, colonnes, diagonales)
        int[][] winPatterns = new int[][]
        {
            new int[] { 0, 1, 2 }, // ligne 1
            new int[] { 3, 4, 5 }, // ligne 2
            new int[] { 6, 7, 8 }, // ligne 3
            new int[] { 0, 3, 6 }, // colonne 1
            new int[] { 1, 4, 7 }, // colonne 2
            new int[] { 2, 5, 8 }, // colonne 3
            new int[] { 0, 4, 8 }, // diagonale 1
            new int[] { 2, 4, 6 }  // diagonale 2
        };

        foreach (var playerId in room.PlayerIds)
        {
            if (!room.PlayerMoves.ContainsKey(playerId))
                continue;

            var playerPositions = room.PlayerMoves[playerId]
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToHashSet();

            foreach (var pattern in winPatterns)
            {
                if (pattern.All(pos => playerPositions.Contains(pos)))
                {
                    return new WinnerInfo
                    {
                        PlayerId = playerId,
                        WinningPositions = pattern
                    };
                }
            }
        }

        return null;
    }
    
}

/// <summary>
/// Informations sur le gagnant
/// </summary>
public class WinnerInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public int[] WinningPositions { get; set; } = Array.Empty<int>();
}
