using Gauniv.GameServer.Network;
using Gauniv.GameServer.Services;

Console.WriteLine("===========================================");
Console.WriteLine("    Gauniv Game Server - TCP Edition      ");
Console.WriteLine("===========================================");
Console.WriteLine();


const int DEFAULT_PORT = 7777;
const string PORT_ENV_VAR = "GAMESERVER_PORT";

string? envPort = Environment.GetEnvironmentVariable(PORT_ENV_VAR);
int port = (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
    ? parsedPort
    : (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int parsedEnvPort) ? parsedEnvPort : DEFAULT_PORT);

Console.WriteLine($"Configuration:\n  - Port: {port}\n  - Protocol: TCP avec MessagePack\n");

var server = new TcpGameServer(port);
var coordinator = new GameCoordinator(server);

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("[Server] Signal d'arrêt reçu (Ctrl+C)...");
    cancellationTokenSource.Cancel();
};

try
{
    var serverTask = server.StartAsync();
    Console.WriteLine("[Server] Serveur en attente de connexions...\n[Server] Appuyez sur Ctrl+C pour arrêter\n");
    await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
}
catch (OperationCanceledException)
{
    // Arrêt normal
}
catch (Exception ex)
{
    Console.WriteLine($"[Server] Erreur fatale: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    await server.StopAsync();
    Console.WriteLine("\n===========================================");
    Console.WriteLine("    Serveur arrêté - Au revoir !         ");
    Console.WriteLine("===========================================");
}
