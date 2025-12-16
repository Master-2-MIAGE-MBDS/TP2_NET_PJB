using Gauniv.GameServer.Network;
using Gauniv.GameServer.Services;

Console.WriteLine("===========================================");
Console.WriteLine("    Gauniv Game Server - TCP Edition      ");
Console.WriteLine("===========================================");
Console.WriteLine();

// Configuration
const int DEFAULT_PORT = 7777;
int port = DEFAULT_PORT;

// Vérifier si un port est spécifié en argument
if (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
{
    port = parsedPort;
}

Console.WriteLine($"Configuration:");
Console.WriteLine($"  - Port: {port}");
Console.WriteLine($"  - Protocol: TCP avec MessagePack");
Console.WriteLine();

// Créer le serveur TCP
var server = new TcpGameServer(port);

// Créer le coordinateur de jeu
var coordinator = new GameCoordinator(server);

// Gérer l'arrêt propre du serveur
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
    // Démarrer le serveur
    var serverTask = server.StartAsync();
    
    Console.WriteLine("[Server] Serveur en attente de connexions...");
    Console.WriteLine("[Server] Appuyez sur Ctrl+C pour arrêter");
    Console.WriteLine();
    
    // Attendre le signal d'arrêt
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
    // Arrêter le serveur proprement
    await server.StopAsync();
    Console.WriteLine();
    Console.WriteLine("===========================================");
    Console.WriteLine("    Serveur arrêté - Au revoir !         ");
    Console.WriteLine("===========================================");
}
