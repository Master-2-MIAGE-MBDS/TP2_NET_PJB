using System.Net.Sockets;
using MessagePack;
using System.Text;

namespace Gauniv.TerminalClient;

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

public static class Program
{
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

        Console.WriteLine($"[Client] Connexion à {host}:{port} en tant que {name}...");

        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        using var stream = client.GetStream();

        var state = new ClientState { Name = name! };

        // Start receiver
        var cts = new CancellationTokenSource();
        var receiver = Task.Run(() => ReceiveLoopAsync(stream, state, cts.Token));

        // Send PlayerConnect
        await SendAsync(stream, new GameMessage
        {
            Type = MessageType.PlayerConnect,
            Data = MessagePackSerializer.Serialize(new PlayerConnectData { PlayerName = name! })
        });

        // Keep process alive until Ctrl+C
        Console.WriteLine("[Client] Connecté. Appuyez sur Ctrl+C pour quitter.");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try { await receiver; } catch { /* ignore */ }
        Console.WriteLine("[Client] Terminé.");
    }

    private static async Task SendAsync(NetworkStream stream, GameMessage message)
    {
        message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = MessagePackSerializer.Serialize(message);
        var len = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(len, 0, len.Length);
        await stream.WriteAsync(payload, 0, payload.Length);
        await stream.FlushAsync();
    }

    private static async Task ReceiveLoopAsync(NetworkStream stream, ClientState state, CancellationToken ct)
    {
        var bufLen = new byte[4];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // read length
                int read = await ReadExactAsync(stream, bufLen, 4, ct);
                if (read == 0) break;
                int len = BitConverter.ToInt32(bufLen, 0);
                var buf = new byte[len];
                read = await ReadExactAsync(stream, buf, len, ct);
                if (read == 0) break;

                var msg = MessagePackSerializer.Deserialize<GameMessage>(buf);
                await HandleMessageAsync(msg, state);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client] Erreur réception: {ex.Message}");
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

    private static Task HandleMessageAsync(GameMessage msg, ClientState state)
    {
        switch (msg.Type)
        {
            case MessageType.ServerWelcome:
                state.MyPlayerId = msg.PlayerId ?? state.MyPlayerId;
                Console.WriteLine($"[Client] Bienvenue! Mon ID: {state.MyPlayerId}");
                break;

            case MessageType.PlayerJoined:
                if (msg.Data != null)
                {
                    var info = MessagePackSerializer.Deserialize<PlayerInfo>(msg.Data);
                    if (!string.IsNullOrEmpty(state.MyPlayerId) && info.PlayerId == state.MyPlayerId)
                    {
                        // Ignore self join broadcast
                        break;
                    }
                    Console.WriteLine($"[Info] {info.PlayerName} a rejoint la salle.");
                }
                break;

            case MessageType.GameState:
                if (msg.Data != null)
                {
                    try
                    {
                        var gs = MessagePackSerializer.Deserialize<GameStateData>(msg.Data);
                        // When second player connects, announce who is already there
                        if (!state.AnnouncedArrival && !string.IsNullOrEmpty(state.MyPlayerId))
                        {
                            var others = gs.Players.Where(p => p.PlayerId != state.MyPlayerId).ToList();
                            if (others.Count > 0)
                            {
                                var first = others[0];
                                Console.WriteLine($"[Info] J'arrive dans une game avec {first.PlayerName}.");
                                state.AnnouncedArrival = true;
                            }
                        }
                    }
                    catch
                    {
                        // Might be TicTacToeGameState; ignore for this lightweight client
                    }
                }
                break;

            case MessageType.ServerError:
                if (msg.Data != null)
                {
                    var err = MessagePackSerializer.Deserialize<ErrorData>(msg.Data);
                    Console.WriteLine($"[Erreur] {err.ErrorCode}: {err.Message}");
                }
                break;

            case MessageType.PlayerLeft:
                if (msg.Data != null)
                {
                    var info = MessagePackSerializer.Deserialize<PlayerInfo>(msg.Data);
                    Console.WriteLine($"[Info] {info.PlayerName} a quitté.");
                }
                break;
        }
        return Task.CompletedTask;
    }

    private class ClientState
    {
        public string Name { get; set; } = string.Empty;
        public string? MyPlayerId { get; set; }
        public bool AnnouncedArrival { get; set; }
    }
}
