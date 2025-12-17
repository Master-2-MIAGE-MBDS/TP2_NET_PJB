using System.Net.Sockets;
using MessagePack;
using System.Text;

namespace Gauniv.TerminalClient;

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

public enum MessageType
{
    PlayerConnect = 1,
    PlayerDisconnect = 2,
    PlayerAction = 3,
    PlayerReady = 4,
    CreateGame = 12,
    ListGames = 13,
    JoinGame = 14,
    MakeMove = 10,
    RequestRematch = 11,
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
public class GameSummary
{
    [Key(0)] public string GameId { get; set; } = string.Empty;
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public int PlayerCount { get; set; }
    [Key(3)] public int MaxPlayers { get; set; }
    [Key(4)] public string Status { get; set; } = string.Empty;
}

[MessagePackObject]
public class GameListResponse
{
    [Key(0)] public List<GameSummary> Games { get; set; } = new();
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
    private static string? _currentGameId;
    private static TicTacToeGameState? _gameState;
    private static bool _gameInProgress = false;
    private static bool _waitingForStart = false;
    private static bool _hasOtherPlayer = false;
    private static List<GameSummary> _availableGames = new();

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
        Console.WriteLine($"Connexion à {host}:{port} en tant que {name}...");

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port);
            _stream = client.GetStream();
            PrintSuccess("Connecté au serveur");
        }
        catch (Exception ex)
        {
            PrintError($"Impossible de se connecter: {ex.Message}");
            return;
        }

        var cts = new CancellationTokenSource();
        var receiver = Task.Run(() => ReceiveLoopAsync(cts.Token));
        var inputHandler = Task.Run(() => HandleUserInputAsync(cts.Token));

        // Send PlayerConnect
        await SendAsync(new GameMessage
        {
            Type = MessageType.PlayerConnect,
            Data = MessagePackSerializer.Serialize(new PlayerConnectData { PlayerName = name! })
        });

        Console.WriteLine("[Client] Appuyez sur Ctrl+C pour quitter.\n");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try { await Task.WhenAll(receiver, inputHandler); } catch { }
        Console.WriteLine("[Client] Déconnecté.");
    }

    private static void PrintHeader()
    {
        Console.WriteLine("\n╔════════════════════════════════════════╗");
        Console.WriteLine("║  GAUNIV - MORPION MULTIPLAYER (TCP)    ║");
        Console.WriteLine("╚════════════════════════════════════════╝\n");
    }

    private static void PrintSuccess(string msg) => Console.WriteLine($"[✓] {msg}");
    private static void PrintError(string msg) => Console.WriteLine($"[✗] {msg}");
    private static void PrintInfo(string msg) => Console.WriteLine($"[ℹ] {msg}");
    private static void PrintWaiting(string msg) => Console.WriteLine($"[⏳] {msg}");

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
                break;

            case MessageType.GameCreated:
                if (msg.Data != null)
                {
                    var created = MessagePackSerializer.Deserialize<GameCreatedData>(msg.Data);
                    _currentGameId = created.Game.GameId;
                    _gameInProgress = false;
                    _waitingForStart = true;
                    _hasOtherPlayer = false;
                    PrintSuccess($"✓ Partie créée: '{created.Game.Name}' (ID: {created.Game.GameId[..8]}...)");
                    PrintWaiting("En attente d'un autre joueur pour démarrer...");
                }
                break;

            case MessageType.GameList:
                if (msg.Data != null)
                {
                    var list = MessagePackSerializer.Deserialize<GameListResponse>(msg.Data);
                    _availableGames = list.Games;
                    Console.WriteLine();
                    if (_availableGames.Count == 0)
                    {
                        PrintInfo("Aucune partie en attente. Créez-en une !");
                    }
                    else
                    {
                        Console.WriteLine("╔════════════════════════════════════════╗");
                        Console.WriteLine("║    Parties en attente de joueurs        ║");
                        Console.WriteLine("╚════════════════════════════════════════╝");
                        foreach (var g in _availableGames)
                        {
                            Console.WriteLine($"Nom: {g.Name}");
                            Console.WriteLine($"ID:  {g.GameId}");
                            Console.WriteLine($"Joueurs: {g.PlayerCount}/{g.MaxPlayers}");
                            Console.WriteLine();
                        }
                    }
                }
                break;

            case MessageType.GameJoined:
                if (msg.Data != null)
                {
                    var joined = MessagePackSerializer.Deserialize<GameJoinedData>(msg.Data);
                    _currentGameId = joined.Game.GameId;
                    _waitingForStart = true;
                    _hasOtherPlayer = false;
                    PrintSuccess($"✓ Rejoint: '{joined.Game.Name}' (role: {joined.Role})");
                    PrintWaiting("En attente du deuxième joueur...");
                }
                break;

            case MessageType.PlayerJoined:
                if (msg.Data != null)
                {
                    try
                    {
                        var info = MessagePackSerializer.Deserialize<PlayerInfo>(msg.Data);
                        if (_myPlayerId != info.PlayerId)
                        {
                            PrintSuccess($"✓ {info.PlayerName} a rejoint la partie !");
                            _hasOtherPlayer = true;
                            _waitingForStart = false;
                        }
                    }
                    catch { }
                }
                break;

            case MessageType.GameStarted:
                _gameInProgress = true;
                _waitingForStart = false;
                PrintSuccess("═══ LA PARTIE COMMENCE ═══");
                PrintInfo("C'est au joueur X de commencer...");
                break;

            case MessageType.GameState:
                if (msg.Data != null)
                {
                    try
                    {
                        var gs = MessagePackSerializer.Deserialize<TicTacToeGameState>(msg.Data);
                        _gameState = gs;
                        _currentGameId = gs.GameId;
                        _waitingForStart = gs.Status == GameStatus.WaitingForPlayers;
                        _gameInProgress = gs.Status == GameStatus.InProgress;
                        if (_gameInProgress)
                        {
                            PrintInfo($"État du jeu: {gs.PlayerXName} (X) vs {gs.PlayerOName} (O)");
                            DisplayBoard();
                        }
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
                        PrintSuccess($"{winner} a gagné");
                        _gameInProgress = false;
                        _waitingForStart = false;
                    }
                    catch { }
                }
                break;

            case MessageType.GameDraw:
                PrintInfo("Match nul");
                _gameInProgress = false;
                _waitingForStart = false;
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
        
        Console.WriteLine("\n╔═══════════════════╗");
        Console.WriteLine("║     PLATEAU       ║");
        Console.WriteLine("╚═══════════════════╝\n");
        
        Console.WriteLine("┌───┬───┬───┐");
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
        Console.WriteLine("└───┴───┴───┘\n");

        string currentTurn = _gameState.CurrentPlayer == CellState.X ? _gameState.PlayerXName : _gameState.PlayerOName;
        Console.WriteLine($"[X] {_gameState.PlayerXName}  vs  [O] {_gameState.PlayerOName}");
        Console.WriteLine($"\n>>> Tour: {currentTurn} ({(_gameState.CurrentPlayer == CellState.X ? "X" : "O")})\n");
    }

    private static async Task HandleUserInputAsync(CancellationToken ct)
    {
        bool waitingForRematchResponse = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Phase gameplay - joueur fait un coup
                if (_gameInProgress && _gameState != null && _myPlayerId != null && !waitingForRematchResponse)
                {
                    bool isMyTurn = (_gameState.CurrentPlayer == CellState.X && _gameState.PlayerXId == _myPlayerId) ||
                                   (_gameState.CurrentPlayer == CellState.O && _gameState.PlayerOId == _myPlayerId);

                    if (isMyTurn)
                    {
                        Console.Write("\nEntrez votre coup (0-8): ");
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
                            PrintError("Position invalide. Entrez un nombre entre 0 et 8.");
                        }
                    }
                    else
                    {
                        await Task.Delay(300, ct);
                    }
                }
                // Phase fin de partie - proposition revanche
                else if (!_gameInProgress && _myPlayerId != null && _hasOtherPlayer && !waitingForRematchResponse && _currentGameId != null)
                {
                    waitingForRematchResponse = true;
                    Console.Write("\nVoulez-vous rejouer? (oui/non): ");
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
                        PrintInfo("Retour au menu principal...");
                        _currentGameId = null;
                        _gameState = null;
                        _hasOtherPlayer = false;
                        _waitingForStart = false;
                        waitingForRematchResponse = false;
                    }
                }
                // Phase attente - joueur attend que l'autre rejoint ou la partie commence
                else if (_waitingForStart && _currentGameId != null && _myPlayerId != null)
                {
                    // Vider le buffer clavier pour ignorer les inputs pendant l'attente
                    while (Console.KeyAvailable)
                    {
                        Console.ReadKey(true);
                    }
                    // Attente silencieuse - le message a déjà été affiché lors de la création/join
                    await Task.Delay(500, ct);
                }
                // Menu principal - lobby
                else
                {
                    Console.WriteLine("\n╔════════════════════════════════════════╗");
                    Console.WriteLine("║     LOBBY - Choisir une action         ║");
                    Console.WriteLine("╚════════════════════════════════════════╝");
                    Console.WriteLine("  [c] Créer une nouvelle partie");
                    Console.WriteLine("  [l] Lister les parties en attente");
                    Console.WriteLine("  [j] Rejoindre une partie par ID");
                    Console.WriteLine("  [q] Quitter");
                    Console.Write("\nVotre choix: ");
                    string? input = await Task.Run(() => Console.ReadLine(), ct);

                    // Si, pendant la saisie, l'état est passé en attente de démarrage,
                    // ignorer l'entrée et ne pas permettre d'autres actions de lobby.
                    if (_waitingForStart && _currentGameId != null)
                    {
                        // Vider aussi le buffer ici au cas où
                        while (Console.KeyAvailable)
                        {
                            Console.ReadKey(true);
                        }
                        await Task.Delay(100, ct);
                        continue;
                    }

                    // Si l'état a changé pendant la saisie (la partie a démarré),
                    // interpréter une entrée numérique comme un coup au lieu d'un choix de menu
                    if (_gameInProgress && _gameState != null && _myPlayerId != null)
                    {
                        if (int.TryParse(input, out int position) && position >= 0 && position <= 8)
                        {
                            await SendAsync(new GameMessage
                            {
                                Type = MessageType.MakeMove,
                                PlayerId = _myPlayerId,
                                Data = MessagePackSerializer.Serialize(new TicTacToeMove { Position = position })
                            });
                            continue;
                        }
                        // Sinon, laisser tomber cette entrée et repasser en boucle
                        // pour afficher le prompt de coup correct
                        await Task.Delay(100, ct);
                        continue;
                    }

                    switch (input?.Trim().ToLower())
                    {
                        case "c":
                        case "create":
                            Console.Write("Nom de la partie (facultatif, appuyez sur Entrée pour auto): ");
                            var gameName = await Task.Run(() => Console.ReadLine(), ct) ?? string.Empty;
                            await SendAsync(new GameMessage
                            {
                                Type = MessageType.CreateGame,
                                PlayerId = _myPlayerId,
                                Data = MessagePackSerializer.Serialize(new CreateGameRequest { GameName = gameName })
                            });
                            _waitingForStart = true;
                            PrintWaiting("Partie créée. En attente d'un autre joueur...");
                            break;

                        case "l":
                        case "list":
                            PrintInfo("Récupération de la liste des parties...");
                            await SendAsync(new GameMessage
                            {
                                Type = MessageType.ListGames,
                                PlayerId = _myPlayerId
                            });
                            await Task.Delay(400, ct);
                            break;

                        case "j":
                        case "join":
                            if (_availableGames.Count > 0)
                            {
                                Console.WriteLine("\n╔════════════════════════════════════════╗");
                                Console.WriteLine("║      Parties disponibles à rejoindre   ║");
                                Console.WriteLine("╚════════════════════════════════════════╝");
                                foreach (var g in _availableGames)
                                {
                                    Console.WriteLine($"{g.Name} ({g.PlayerCount}/{g.MaxPlayers})");
                                    Console.WriteLine($"ID: {g.GameId}");
                                    Console.WriteLine();
                                }
                            }
                            else
                            {
                                PrintInfo("Aucune partie disponible. Créez-en une ou attendez.");
                                break;
                            }
                            Console.Write("Entrez l'ID de la partie à rejoindre: ");
                            var gameId = await Task.Run(() => Console.ReadLine(), ct);
                            if (!string.IsNullOrWhiteSpace(gameId))
                            {
                                await SendAsync(new GameMessage
                                {
                                    Type = MessageType.JoinGame,
                                    PlayerId = _myPlayerId,
                                    Data = MessagePackSerializer.Serialize(new JoinGameRequest { GameId = gameId.Trim() })
                                });
                                _waitingForStart = true;
                                PrintWaiting("Connexion à la partie...");
                            }
                            break;

                        case "q":
                        case "quit":
                            PrintInfo("Déconnexion...");
                            return;

                        default:
                            PrintError("Choix invalide. Réessayez.");
                            await Task.Delay(400, ct);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                PrintError($"Erreur: {ex.Message}");
                await Task.Delay(500, ct);
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
