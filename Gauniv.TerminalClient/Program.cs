using System.Net.Sockets;
using MessagePack;

namespace Gauniv.TerminalClient;

#region Messages & DTOs



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
    SyncGameState = 15,
    
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
    MoveAccepted = 115,
    MoveRejected = 116,
    GameStateSync = 117,
}


[MessagePackObject]
public class GameMessage
{
    [Key(0)] public MessageType Type { get; set; }
    [Key(1)] public string? PlayerId { get; set; }
    [Key(2)] public long Timestamp { get; set; }
    [Key(3)] public byte[]? Data { get; set; }
}

[MessagePackObject]
public class MakeMoveData
{
    [Key(0)]
    public int Position { get; set; } // Position 0-8 sur la grille
}


[MessagePackObject]
public class PlayerConnectData
{
    [Key(0)] public string PlayerName { get; set; } = string.Empty;
}

[MessagePackObject]
public class ErrorData
{
    [Key(0)] public string ErrorCode { get; set; } = string.Empty;
    [Key(1)] public string Message { get; set; } = string.Empty;
}

[MessagePackObject]
public class CreateGameRequest
{
    [Key(0)] public string GameName { get; set; } = string.Empty;
}

[MessagePackObject]
public class JoinGameRequest
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
}

[MessagePackObject]
public class GameSummary
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public int PlayerCount { get; set; }
    [Key(3)] public int MaxPlayers { get; set; }
}

[MessagePackObject]
public class GameCreatedData
{
    [Key(0)] public GameSummary Game { get; set; } = new();
}

[MessagePackObject]
public class GameJoinedData
{
    [Key(0)] public GameSummary Game { get; set; } = new();
    [Key(1)] public string Role { get; set; } = string.Empty;
}

[MessagePackObject]
public class GameListResponse
{
    [Key(0)] public List<GameSummary> Games { get; set; } = new();
}

#endregion

#region Client State

enum ClientState
{
    Connecting,
    Menu,
    WaitingServer,
    WaitingInRoom,
    InGame,
    Disconnected
}


#endregion

class Program
{
    private static NetworkStream? _stream;
    private static string? _myPlayerId;
    private static string? _myPlayerName;
    private static string? _currentGameId;

    private static ClientState _state = ClientState.Connecting;
    private static List<GameSummary> _availableGames = new();
    private static TaskCompletionSource<bool> _listGamesReceived = new();
    private static Random _random = new();

    static async Task Main(string[] args)
    {
        string host = "localhost";
        int port = 7777;
        string name = args.Length > 0 ? args[0] : $"Player-{DateTime.Now.Ticks % 1000}";

        _myPlayerName = name;
        PrintHeader();

        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        _stream = client.GetStream();

        var cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(cts.Token);

        await SendAsync(new GameMessage
        {
            Type = MessageType.PlayerConnect,
            Data = MessagePackSerializer.Serialize(new PlayerConnectData { PlayerName = name })
        });

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _state = ClientState.Disconnected;
            cts.Cancel();
        };

        while (_state != ClientState.Disconnected)
            await Task.Delay(500);
    }

    #region Network

    private static async Task SendAsync(GameMessage msg)
    {
        if (_stream == null) return;

        msg.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var data = MessagePackSerializer.Serialize(msg);
        var len = BitConverter.GetBytes(data.Length);

        await _stream.WriteAsync(len);
        await _stream.WriteAsync(data);
        await _stream.FlushAsync();
    }

    private static async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var lenBuf = new byte[4];

        while (!ct.IsCancellationRequested)
        {
            await ReadExactAsync(_stream!, lenBuf, 4, ct);
            int len = BitConverter.ToInt32(lenBuf);

            var buf = new byte[len];
            await ReadExactAsync(_stream!, buf, len, ct);

            var msg = MessagePackSerializer.Deserialize<GameMessage>(buf);
            await HandleMessageAsync(msg);
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken ct)
    {
        int read = 0;
        while (read < length)
            read += await stream.ReadAsync(buffer.AsMemory(read, length - read), ct);
    }

    #endregion

    #region Message Handling

    private static async Task HandleMessageAsync(GameMessage msg)
    {
        PrintInfo($"Reçu: {msg.Type}");

        switch (msg.Type)
        {
            case MessageType.ServerWelcome:
                _myPlayerId = msg.PlayerId;
                _state = ClientState.Menu;
                _ = RunStateLoopAsync();
                break;

            case MessageType.GameList:
                var list = MessagePackSerializer
                    .Deserialize<GameListResponse>(msg.Data!);

                _availableGames = list.Games;

                DisplayGames();

                _state = ClientState.Menu;
                break;

            case MessageType.MoveMade:
                
                MakeMoveData moveData = MessagePackSerializer
                    .Deserialize<MakeMoveData>(msg.Data!);
                PrintInfo("Mouvement effectué");
                PrintInfo($"Le serveur a enregistré le mouvement à la position: {moveData.Position}");

                await Task.Delay(5000);
                await SendGameMovesAsync();
                break;
            
            case MessageType.MoveAccepted:
                int acceptedState = MessagePackSerializer
                    .Deserialize<int>(msg.Data!);
                
                PrintInfo("État du jeu reçu");
                PrintInfo($"Le serveur a envoyé le mouvement: {acceptedState}");
                await Task.Delay(5000);
                await SendGameMovesAsync();
                break;

            case MessageType.GameCreated:
                _currentGameId =
                    MessagePackSerializer.Deserialize<GameCreatedData>(msg.Data!).Game.GameId;
                PrintSuccess("Room créée");
                _state = ClientState.WaitingInRoom;
                break;

            case MessageType.GameJoined:
                _currentGameId =
                    MessagePackSerializer.Deserialize<GameJoinedData>(msg.Data!).Game.GameId;
                PrintSuccess("Room rejointe");
                _state = ClientState.InGame;
                await SendGameMovesAsync();
                break;

            case MessageType.ServerError:
                var err = MessagePackSerializer.Deserialize<ErrorData>(msg.Data!);
                PrintError(err.Message);
                _state = ClientState.Menu;
                break;

            default:
                PrintError($"Message inconnu: {msg.Type}");
                break;
        }
    }

    #endregion

    #region State Machine

    private static async Task RunStateLoopAsync()
    {
        while (_state != ClientState.Disconnected)
        {
            switch (_state)
            {
                case ClientState.Menu:
                    await ShowMenuOnceAsync();
                    break;
                
                case ClientState.WaitingServer:
                case ClientState.WaitingInRoom:
                case ClientState.InGame:
                    await Task.Delay(2000);
                    break;
            }
        }
    }

    private static async Task ShowMenuOnceAsync()
    {
        Console.WriteLine("\n[c] Créer  [l] Lister  [j] Rejoindre  [q] Quitter");
        Console.Write("> ");
        var choice = Console.ReadLine()?.ToLower();

        switch (choice)
        {
            case "c":
                Console.Write("Nom room: ");
                string name = Console.ReadLine() ?? "Room";
                _state = ClientState.WaitingInRoom;
                await SendAsync(new GameMessage
                {
                    Type = MessageType.CreateGame,
                    PlayerId = _myPlayerId,
                    Data = MessagePackSerializer.Serialize(new CreateGameRequest { GameName = name })
                });
                break;

            case "l":
                _state = ClientState.WaitingServer;

                _listGamesReceived = new TaskCompletionSource<bool>();

                await SendAsync(new GameMessage
                {
                    Type = MessageType.ListGames,
                    PlayerId = _myPlayerId
                });
                break;

            case "j":
                if (_availableGames.Count == 0) break;
                Console.Write("Index: ");
                if (int.TryParse(Console.ReadLine(), out int i))
                {
                    _state = ClientState.WaitingInRoom;
                    await SendAsync(new GameMessage
                    {
                        Type = MessageType.JoinGame,
                        PlayerId = _myPlayerId,
                        Data = MessagePackSerializer.Serialize(
                            new JoinGameRequest { GameId = _availableGames[i].GameId })
                    });
                }
                break;

            case "q":
                _state = ClientState.Disconnected;
                break;
        }
    }

    #endregion

    #region Game Logic
    private static async Task SendGameMovesAsync()
    {

        
        await SendAsync(new GameMessage
        {
            Type = MessageType.MakeMove,
            Data = MessagePackSerializer.Serialize(
                new MakeMoveData { Position = _random.Next(0, 8) }
            )
        }
        );
    }


    private static void DisplayGames()
    {
        Console.WriteLine("Rooms:");
        for (int i = 0; i < _availableGames.Count; i++)
        {
            var g = _availableGames[i];
            Console.WriteLine($"[{i}] {g.Name} ({g.PlayerCount}/{g.MaxPlayers})");
        }
    }

    #endregion

    #region UI Helpers

    private static void PrintHeader()
    {
        Console.WriteLine("╔══════════════════════════════╗");
        Console.WriteLine("║   MORPION CLIENT (STATE)    ║");
        Console.WriteLine("╚══════════════════════════════╝");
    }

    private static void PrintInfo(string m) => Console.WriteLine($"[ℹ] {m}");
    private static void PrintSuccess(string m) => Console.WriteLine($"[✓] {m}");
    private static void PrintError(string m) => Console.WriteLine($"[✗] {m}");

    #endregion
}
