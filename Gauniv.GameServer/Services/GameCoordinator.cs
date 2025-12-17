using MessagePack;
using Gauniv.GameServer.Models;
using Gauniv.GameServer.Network;

namespace Gauniv.GameServer.Services;

/// <summary>
/// Représente une room de jeu
/// </summary>
public class GameRoom
{
    public string GameId { get; set; }
    public string Name { get; set; }
    public List<string> PlayerIds { get; set; } = new();
    public List<string> SpectatorIds { get; set; } = new();

    public GameRoom(string gameId, string name)
    {
        GameId = gameId;
        Name = name;
    }

    public int TotalPlayers => PlayerIds.Count + SpectatorIds.Count;
}

/// <summary>
/// Coordonnateur qui gère les rooms et diffuse les états du jeu
/// </summary>
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

            case MessageType.CreateGame:
                await HandleCreateGameAsync(connection, message);
                break;

            case MessageType.ListGames:
                await HandleListGamesAsync(connection);
                break;

            case MessageType.JoinGame:
                await HandleJoinGameAsync(connection, message);
                break;
                
            case MessageType.GameState:
                await HandleGameStateAsync(connection, message);
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
        
        var connectData = MessagePackSerializer.Deserialize<PlayerConnectData>(message.Data);
        
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
                    MaxPlayers = 2
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
                .Where(r => r.PlayerIds.Count < 2)
                .Select(r => new GameSummary
                {
                    GameId = r.GameId,
                    Name = r.Name,
                    PlayerCount = r.PlayerIds.Count,
                    MaxPlayers = 2
                })
                .ToList();
        }

        Console.WriteLine($"[Coordinator] ListGames demandé: {games.Count} rooms libres sur {totalRooms} total");

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

        if (room.PlayerIds.Count >= 2)
        {
            // C'est un spectateur
            room.SpectatorIds.Add(connection.PlayerId);
            Console.WriteLine($"[Coordinator] Spectateur rejoint: '{room.Name}'");
        }
        else
        {
            // C'est un joueur
            room.PlayerIds.Add(connection.PlayerId);
            Console.WriteLine($"[Coordinator] Joueur rejoint: '{room.Name}' ({room.PlayerIds.Count}/2)");
        }

        lock (_mapLock)
        {
            _playerRoomMap[connection.PlayerId] = room.GameId;
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
                    MaxPlayers = 2
                },
                Role = room.PlayerIds.Count >= 2 ? "SPECTATEUR" : "JOUEUR"
            })
        };

        await connection.SendMessageAsync(response);
    }

    /// <summary>
    /// Reçoit l'état du jeu et le diffuse uniquement à la room
    /// </summary>
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
            // Deserialiser la liste 3x3
            var lastMoves = MessagePackSerializer.Deserialize<int[][]>(message.Data);

            if (lastMoves == null || lastMoves.Length != 2)
            {
                await SendErrorAsync(connection, "INVALID_FORMAT", "Format attendu: 2 listes de coups");
                return;
            }

            foreach (var playerMoves in lastMoves)
            {
                if (playerMoves.Length != 3)
                {
                    await SendErrorAsync(connection, "INVALID_FORMAT", "Chaque joueur doit avoir 3 coups");
                    return;
                }

                foreach (var move in playerMoves)
                {
                    if (move < 0 || move > 8)
                    {
                        await SendErrorAsync(connection, "INVALID_VALUE", "Coups entre 0 et 8");
                        return;
                    }
                }
            }

            Console.WriteLine($"[Coordinator] Coups reçus de {connection.PlayerName}");
            Console.WriteLine($"  - Joueur 1: [{string.Join(", ", lastMoves[0])}]");
            Console.WriteLine($"  - Joueur 2: [{string.Join(", ", lastMoves[1])}]");

            // Diffuser à TOUS dans la room (joueurs + spectateurs)
            await BroadcastToRoomAsync(roomId, new GameMessage
            {
                Type = MessageType.GameState,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = message.Data
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Coordinator] Erreur: {ex.Message}");
            await SendErrorAsync(connection, "DESERIALIZATION_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Diffuse un message à tous les clients d'une room
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
        Console.WriteLine($"[Coordinator] Diffusé à {allIds.Count} clients dans la room");
    }

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
}

/// <summary>
/// Données de création de room
/// </summary>
[MessagePackObject]
public class CreateGameRequest
{
    [Key(0)] public string GameName { get; set; } = string.Empty;
}

/// <summary>
/// Données de join de room
/// </summary>
[MessagePackObject]
public class JoinGameRequest
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
}

/// <summary>
/// Résumé d'une room
/// </summary>
[MessagePackObject]
public class GameSummary
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public int PlayerCount { get; set; }
    [Key(3)] public int MaxPlayers { get; set; }
}

/// <summary>
/// Réponse de création de room
/// </summary>
[MessagePackObject]
public class GameCreatedData
{
    [Key(0)] public GameSummary Game { get; set; } = new();
}

/// <summary>
/// Réponse de join de room
/// </summary>
[MessagePackObject]
public class GameJoinedData
{
    [Key(0)] public GameSummary Game { get; set; } = new();
    [Key(1)] public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Réponse listant les rooms
/// </summary>
[MessagePackObject]
public class GameListResponse
{
    [Key(0)] public List<GameSummary> Games { get; set; } = new();
}
