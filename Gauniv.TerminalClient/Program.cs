using System.Net.Sockets;
using MessagePack;
using System.Text;

namespace Gauniv.TerminalClient;

// Enums from server
public enum CellState
{
    Empty = 0,
    X = 1,
    O = 2
}

public enum GameStatus
{
    WaitingForPlayers = 0,
    InProgress = 1,
    XWins = 2,
    OWins = 3,
    Draw = 4,
    Cancelled = 5
}

// Protocol models (keys must match server)
public enum MessageType
{
    PlayerConnect = 1,
    PlayerDisconnect = 2,
    PlayerAction = 3,
    PlayerReady = 4,
    MakeMove = 10,
    RequestRematch = 11,
    ServerWelcome = 100,
    ServerError = 101,
    GameState = 102,
    PlayerJoined = 103,
    PlayerLeft = 104,
    GameStarted = 105,
    GameEnded = 106,
    MoveMade = 110,
    InvalidMove = 111,
    GameWon = 112,
    GameDraw = 113,
    RematchOffered = 114,
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
public class PlayerConnectData
{
    [Key(0)] public string PlayerName { get; set; } = string.Empty;
    [Key(1)] public string? UserId { get; set; }
}

[MessagePackObject]
public class PlayerInfo
{
    [Key(0)] public string PlayerId { get; set; } = string.Empty;
    [Key(1)] public string PlayerName { get; set; } = string.Empty;
    [Key(2)] public bool IsReady { get; set; }
    [Key(3)] public bool IsConnected { get; set; }
}

[MessagePackObject]
public class GameStateData
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
    [Key(1)] public List<PlayerInfo> Players { get; set; } = new();
    [Key(2)] public string Status { get; set; } = string.Empty;
    [Key(3)] public Dictionary<string, object>? CustomData { get; set; }
}

[MessagePackObject]
public class ErrorData
{
    [Key(0)] public string ErrorCode { get; set; } = string.Empty;
    [Key(1)] public string Message { get; set; } = string.Empty;
}

[MessagePackObject]
public class TicTacToeMove
{
    [Key(0)] public int Position { get; set; }
}

[MessagePackObject]
public class TicTacToeGameState
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
    [Key(1)] public CellState[] Board { get; set; } = new CellState[9];
    [Key(2)] public GameStatus Status { get; set; }
    [Key(3)] public CellState CurrentPlayer { get; set; }
    [Key(4)] public string? PlayerXId { get; set; }
    [Key(5)] public string? PlayerXName { get; set; }
    [Key(6)] public string? PlayerOId { get; set; }
    [Key(7)] public string? PlayerOName { get; set; }
    [Key(8)] public string? WinnerId { get; set; }
    [Key(9)] public int[] WinningLine { get; set; } = Array.Empty<int>();
    [Key(10)] public int PlayerXScore { get; set; }
    [Key(11)] public int PlayerOScore { get; set; }
}

public static class Program
{
    private static NetworkStream? _stream;
    private static string? _myPlayerId;
    private static string? _myPlayerName;
    private static TicTacToeGameState? _gameState;
    private static bool _gameInProgress = false;
    private static bool _hasOtherPlayer = false;

    public static async Task Main(string[] args)
    {
        var host = "127.0.0.1";
        var port = 7777;
        string? name = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host":
                case "-h":
                    if (i + 1 < args.Length) host = args[++i];
                    break;
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p;
                    break;
                case "--name":
                case "-n":
                    if (i + 1 < args.Length) name = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Write("Nom du joueur: ");
            name = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = $"Player-{Guid.NewGuid().ToString()[..8]}";
        }

        _myPlayerName = name;
        PrintHeader();
        Console.WriteLine($"🌐 Connexion à {host}:{port} en tant que {name}...");

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port);
            _stream = client.GetStream();
            PrintSuccess("✅ Connecté au serveur!");
        }
        catch (Exception ex)
        {
            PrintError($"❌ Impossible de se connecter: {ex.Message}");
            return;
        }

        var cts = new CancellationTokenSource();
        var receiver = Task.Run(() => ReceiveLoopAsync(cts.Token));
        var moveHandler = Task.Run(() => HandleMoveInputAsync(cts.Token));

        // Send PlayerConnect
        await SendAsync(new GameMessage
        {
            Type = MessageType.PlayerConnect,
            Data = MessagePackSerializer.Serialize(new PlayerConnectData { PlayerName = name! })
        });

        Console.WriteLine("[Client] Appuyez sur Ctrl+C pour quitter.\n");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try { await Task.WhenAll(receiver, moveHandler); } catch { }
        Console.WriteLine("[Client] Déconnecté.");
    }

    private static void PrintHeader()
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║  🎮 GAUNIV - MORPION MULTIPLAYER (TCP) ║");
        Console.WriteLine("╚════════════════════════════════════════╝\n");
    }

    private static void PrintSuccess(string msg) => Console.WriteLine($"{msg}");
    private static void PrintError(string msg) => Console.WriteLine($"⚠️  {msg}");
    private static void PrintInfo(string msg) => Console.WriteLine($"ℹ️  {msg}");
    private static void PrintWaiting(string msg) => Console.WriteLine($"⏳ {msg}");

    private static async Task SendAsync(GameMessage message)
    {
        if (_stream == null) return;
        try
        {
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var payload = MessagePackSerializer.Serialize(message);
            var len = BitConverter.GetBytes(payload.Length);
            await _stream.WriteAsync(len, 0, len.Length);
            await _stream.WriteAsync(payload, 0, payload.Length);
            await _stream.FlushAsync();
        }
        catch (Exception ex)
        {
            PrintError($"Erreur d'envoi: {ex.Message}");
        }
    }

    private static async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_stream == null) return;
        var bufLen = new byte[4];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int read = await ReadExactAsync(_stream, bufLen, 4, ct);
                if (read == 0) break;
                int len = BitConverter.ToInt32(bufLen, 0);
                var buf = new byte[len];
                read = await ReadExactAsync(_stream, buf, len, ct);
                if (read == 0) break;

                var msg = MessagePackSerializer.Deserialize<GameMessage>(buf);
                HandleMessage(msg);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PrintError($"Erreur réception: {ex.Message}");
                break;
            }
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken ct)
    {
        int total = 0;
        while (total < length)
        {
            int read = await stream.ReadAsync(buffer, total, length - total, ct);
            if (read == 0) return 0;
            total += read;
        }
        return total;
    }

    private static void HandleMessage(GameMessage msg)
    {
        switch (msg.Type)
        {
            case MessageType.ServerWelcome:
                _myPlayerId = msg.PlayerId;
                PrintSuccess($"Bienvenue! Mon ID: {_myPlayerId}");
                PrintWaiting("En attente d'un adversaire...");
                break;

            case MessageType.PlayerJoined:
                if (msg.Data != null)
                {
                    try
                    {
                        var info = MessagePackSerializer.Deserialize<PlayerInfo>(msg.Data);
                        if (_myPlayerId != info.PlayerId)
                        {
                            PrintInfo($"{info.PlayerName} a rejoint la salle");
                            _hasOtherPlayer = true;
                        }
                    }
                    catch { }
                }
                break;

            case MessageType.GameStarted:
                _gameInProgress = true;
                PrintSuccess("🎮 LA PARTIE COMMENCE!");
                PrintInfo("Attente du premier coup...");
                break;

            case MessageType.GameState:
                if (msg.Data != null)
                {
                    try
                    {
                        var gs = MessagePackSerializer.Deserialize<TicTacToeGameState>(msg.Data);
                        _gameState = gs;
                        PrintInfo($"État du jeu reçu: {gs.PlayerXName} (X) vs {gs.PlayerOName} (O)");
                        DisplayBoard();
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Erreur désérialisation GameState: {ex.Message}");
                    }
                }
                break;

            case MessageType.MoveMade:
                if (msg.Data != null)
                {
                    try
                    {
                        var moveResult = MessagePackSerializer.Deserialize<MoveResult>(msg.Data);
                        PrintInfo($"Coup joué en position {moveResult.Position}");
                    }
                    catch { }
                }
                break;

            case MessageType.InvalidMove:
                if (msg.Data != null)
                {
                    try
                    {
                        var moveResult = MessagePackSerializer.Deserialize<MoveResult>(msg.Data);
                        PrintError($"Coup invalide: {moveResult.ErrorMessage}");
                    }
                    catch { }
                }
                break;

            case MessageType.GameWon:
                if (msg.Data != null)
                {
                    try
                    {
                        var gs = MessagePackSerializer.Deserialize<TicTacToeGameState>(msg.Data);
                        _gameState = gs;
                        DisplayBoard();
                        string winner = _gameState.CurrentPlayer == CellState.X ? _gameState.PlayerXName : _gameState.PlayerOName;
                        PrintSuccess($"🏆 {winner} a gagné!");
                        _gameInProgress = false;
                    }
                    catch { }
                }
                break;

            case MessageType.GameDraw:
                PrintInfo("🤝 Match nul!");
                _gameInProgress = false;
                break;

            case MessageType.ServerError:
                if (msg.Data != null)
                {
                    try
                    {
                        var err = MessagePackSerializer.Deserialize<ErrorData>(msg.Data);
                        PrintError($"{err.ErrorCode}: {err.Message}");
                    }
                    catch { }
                }
                break;

            case MessageType.PlayerLeft:
                if (msg.Data != null)
                {
                    try
                    {
                        var info = MessagePackSerializer.Deserialize<PlayerInfo>(msg.Data);
                        PrintInfo($"{info.PlayerName} a quitté");
                    }
                    catch { }
                }
                break;
        }
    }

    private static void DisplayBoard()
    {
        if (_gameState == null) return;
        
        Console.WriteLine("\n┌───┬───┬───┐");
        for (int row = 0; row < 3; row++)
        {
            Console.Write("│");
            for (int col = 0; col < 3; col++)
            {
                int idx = row * 3 + col;
                char cell = _gameState.Board[idx] switch
                {
                    CellState.X => 'X',
                    CellState.O => 'O',
                    _ => (char)('0' + idx)
                };
                Console.Write($" {cell} │");
            }
            Console.WriteLine();
            if (row < 2) Console.WriteLine("├───┼───┼───┤");
        }
        Console.WriteLine("└───┴───┴───┘");

        string currentTurn = _gameState.CurrentPlayer == CellState.X ? _gameState.PlayerXName : _gameState.PlayerOName;
        PrintInfo($"Tour de {currentTurn} ({(_gameState.CurrentPlayer == CellState.X ? "X" : "O")})");
    }

    private static async Task HandleMoveInputAsync(CancellationToken ct)
    {
        bool waitingForRematchResponse = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Pendant le jeu et on a reçu l'état du jeu
                if (_gameInProgress && _gameState != null && _myPlayerId != null && !waitingForRematchResponse)
                {
                    bool isMyTurn = (_gameState.CurrentPlayer == CellState.X && _gameState.PlayerXId == _myPlayerId) ||
                                   (_gameState.CurrentPlayer == CellState.O && _gameState.PlayerOId == _myPlayerId);

                    if (isMyTurn)
                    {
                        Console.Write("\n🎯 Entrez votre coup (0-8): ");
                        string? input = await Task.Run(() => Console.ReadLine(), ct);

                        if (int.TryParse(input, out int position) && position >= 0 && position <= 8)
                        {
                            await SendAsync(new GameMessage
                            {
                                Type = MessageType.MakeMove,
                                PlayerId = _myPlayerId,
                                Data = MessagePackSerializer.Serialize(new TicTacToeMove { Position = position })
                            });
                        }
                        else
                        {
                            PrintError($"Position invalide. Entrez un nombre entre 0 et 8.");
                        }
                    }
                    else
                    {
                        // On attend que l'autre joueur joue
                        await Task.Delay(500, ct);
                    }
                }
                // Si la partie est terminée
                else if (!_gameInProgress && _myPlayerId != null && _hasOtherPlayer)
                {
                    waitingForRematchResponse = true;
                    Console.Write("\n🔄 Voulez-vous rejoner? (oui/non): ");
                    string? input = await Task.Run(() => Console.ReadLine(), ct);
                    
                    if (input?.ToLower() == "oui" || input?.ToLower() == "o" || input?.ToLower() == "y" || input?.ToLower() == "yes")
                    {
                        await SendAsync(new GameMessage
                        {
                            Type = MessageType.RequestRematch,
                            PlayerId = _myPlayerId
                        });
                        _gameInProgress = false;
                        waitingForRematchResponse = false;
                    }
                    else
                    {
                        PrintInfo("Déconnexion...");
                        break;
                    }
                }
                // En attente - ne rien faire, juste attendre les messages du serveur
                else
                {
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PrintError($"Erreur: {ex.Message}");
            }
        }
    }
}

[MessagePackObject]
public class MoveResult
{
    [Key(0)] public int Position { get; set; }
    [Key(1)] public bool Success { get; set; }
    [Key(2)] public int Player { get; set; }
    [Key(3)] public string? ErrorMessage { get; set; }
}
