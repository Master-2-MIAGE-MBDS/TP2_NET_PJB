using MessagePack;

namespace Gauniv.GameServer.Models;

/// <summary>
/// Type de message échangé entre client et serveur
/// </summary>
public enum MessageType
{
    // Messages client -> serveur
    PlayerConnect = 1,
    PlayerDisconnect = 2,
    PlayerAction = 3,
    PlayerReady = 4,

    // Lobby / salon
    CreateGame = 12,
    ListGames = 13,
    JoinGame = 14,
    
    // Messages spécifiques Morpion
    MakeMove = 10,
    RequestRematch = 11,
    
    // Messages serveur -> client
    ServerWelcome = 100,
    ServerError = 101,
    GameState = 102,
    PlayerJoined = 103,
    PlayerLeft = 104,
    GameStarted = 105,
    GameEnded = 106,
    GameCreated = 107,
    GameList = 108,
    GameJoined = 109,
    
    // Messages spécifiques Morpion serveur
    MoveMade = 110,
    InvalidMove = 111,
    GameWon = 112,
    GameDraw = 113,
    RematchOffered = 114,
}

/// <summary>
/// Message de base pour la communication
/// </summary>
[MessagePackObject]
public class GameMessage
{
    [Key(0)]
    public MessageType Type { get; set; }
    
    [Key(1)]
    public string? PlayerId { get; set; }
    
    [Key(2)]
    public long Timestamp { get; set; }
    
    [Key(3)]
    public byte[]? Data { get; set; }
}

/// <summary>
/// Données de connexion d'un joueur
/// </summary>
[MessagePackObject]
public class PlayerConnectData
{
    [Key(0)]
    public string PlayerName { get; set; } = string.Empty;
    
    [Key(1)]
    public string? UserId { get; set; }
}

/// <summary>
/// Demande de création de partie
/// </summary>
[MessagePackObject]
public class CreateGameRequest
{
    [Key(0)]
    public string GameName { get; set; } = string.Empty;
}

/// <summary>
/// Demande de rejoindre une partie
/// </summary>
[MessagePackObject]
public class JoinGameRequest
{
    [Key(0)]
    public string GameId { get; set; } = string.Empty;
}

/// <summary>
/// Données d'action d'un joueur
/// </summary>
[MessagePackObject]
public class PlayerActionData
{
    [Key(0)]
    public string ActionType { get; set; } = string.Empty;
    
    [Key(1)]
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// État du jeu envoyé aux clients
/// </summary>
[MessagePackObject]
public class GameStateData
{
    [Key(0)]
    public string GameId { get; set; } = string.Empty;
    
    [Key(1)]
    public List<PlayerInfo> Players { get; set; } = new();
    
    [Key(2)]
    public string Status { get; set; } = string.Empty;
    
    [Key(3)]
    public Dictionary<string, object>? CustomData { get; set; }
}

/// <summary>
/// Résumé d'une partie pour la liste/lobby
/// </summary>
[MessagePackObject]
public class GameSummary
{
    [Key(0)]
    public string GameId { get; set; } = string.Empty;

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public int PlayerCount { get; set; }

    [Key(3)]
    public int MaxPlayers { get; set; }

    [Key(4)]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Réponse listant les parties disponibles
/// </summary>
[MessagePackObject]
public class GameListResponse
{
    [Key(0)]
    public List<GameSummary> Games { get; set; } = new();
}

/// <summary>
/// Réponse de création de partie
/// </summary>
[MessagePackObject]
public class GameCreatedData
{
    [Key(0)]
    public GameSummary Game { get; set; } = new();
}

/// <summary>
/// Réponse de join de partie
/// </summary>
[MessagePackObject]
public class GameJoinedData
{
    [Key(0)]
    public GameSummary Game { get; set; } = new();

    [Key(1)]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Informations sur un joueur
/// </summary>
[MessagePackObject]
public class PlayerInfo
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;
    
    [Key(1)]
    public string PlayerName { get; set; } = string.Empty;
    
    [Key(2)]
    public bool IsReady { get; set; }
    
    [Key(3)]
    public bool IsConnected { get; set; }
}

/// <summary>
/// Message d'erreur
/// </summary>
[MessagePackObject]
public class ErrorData
{
    [Key(0)]
    public string ErrorCode { get; set; } = string.Empty;
    
    [Key(1)]
    public string Message { get; set; } = string.Empty;
}
