using System.Diagnostics;
using System.Net.Sockets;
using MessagePack;
using Gauniv.GameServer.Models;

namespace Gauniv.GameServer.Tests;

/// <summary>
/// Script de test automatique pour 2 joueurs
/// </summary>
public class TestGameScript
{
    private readonly int _port;
    private Process? _serverProcess;
    
    public TestGameScript(int port = 7777)
    {
        _port = port;
    }
    
    public async Task RunAsync()
    {
        Console.WriteLine("==========================================");
        Console.WriteLine("  Test du Serveur Morpion - 2 Joueurs");
        Console.WriteLine("==========================================");
        Console.WriteLine();
        
        try
        {
            // Démarrer le serveur
            await StartServerAsync();
            
            // Attendre que le serveur soit prêt
            await Task.Delay(3000);
            
            // Lancer les deux joueurs
            var player1Task = PlayGameAsync("Player1", new[] { 0 });
            var player2Task = PlayGameAsync("Player2", new[] { 4, 1, 2 });
            
            await Task.WhenAll(player1Task, player2Task);
            
            // Attendre un peu avant d'arrêter le serveur
            await Task.Delay(2000);
        }
        finally
        {
            StopServer();
        }
        
        Console.WriteLine();
        Console.WriteLine("==========================================");
        Console.WriteLine("  Test Terminé");
        Console.WriteLine("==========================================");
    }
    
    private async Task StartServerAsync()
    {
        Console.WriteLine("[Test] Démarrage du serveur...");
        
        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run",
                WorkingDirectory = "/workspaces/TP2_NET_PJB/Gauniv.GameServer",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };
        
        _serverProcess.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"  [Server] {e.Data}");
        };
        
        _serverProcess.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"  [Server ERROR] {e.Data}");
        };
        
        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
        
        Console.WriteLine("[Test] ✅ Serveur démarré");
    }
    
    private void StopServer()
    {
        if (_serverProcess == null) return;
        
        Console.WriteLine("[Test] Arrêt du serveur...");
        
        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(5000);
            }
        }
        catch { }
        finally
        {
            _serverProcess.Dispose();
        }
        
        Console.WriteLine("[Test] ✅ Serveur arrêté");
    }
    
    private async Task PlayGameAsync(string playerName, int[] moves)
    {
        Console.WriteLine($"[Test] {playerName} se connecte...");
        
        using var client = new TcpClient();
        
        try
        {
            // Connexion
            await client.ConnectAsync("localhost", _port);
            var stream = client.GetStream();
            
            Console.WriteLine($"[Test] ✅ {playerName} connecté");
            
            // Connexion au serveur
            var connectData = new PlayerConnectData { PlayerName = playerName };
            await SendMessageAsync(stream, new GameMessage
            {
                Type = MessageType.PlayerConnect,
                Data = MessagePackSerializer.Serialize(connectData),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            
            // Attendre un peu
            await Task.Delay(500);
            
            // Prêt
            await SendMessageAsync(stream, new GameMessage
            {
                Type = MessageType.PlayerReady,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            
            Console.WriteLine($"[Test] ✅ {playerName} est prêt");
            
            // Attendre le démarrage de la partie
            await Task.Delay(2000);
            
            // Lire les messages du serveur et jouer les coups
            var moveIndex = 0;
            var moveTask = Task.Run(async () =>
            {
                while (client.Connected && moveIndex < moves.Length)
                {
                    await Task.Delay(1500);
                    
                    if (moveIndex < moves.Length)
                    {
                        var position = moves[moveIndex];
                        Console.WriteLine($"[Test] {playerName} joue en position {position}");
                        
                        var moveData = new TicTacToeMove
                        {
                            Position = position,
                            PlayerId = playerName
                        };
                        
                        await SendMessageAsync(stream, new GameMessage
                        {
                            Type = MessageType.MakeMove,
                            Data = MessagePackSerializer.Serialize(moveData),
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        });
                        
                        moveIndex++;
                    }
                }
            });
            
            var receiveTask = Task.Run(async () =>
            {
                try
                {
                    while (client.Connected)
                    {
                        var lengthBytes = new byte[4];
                        var bytesRead = await stream.ReadAsync(lengthBytes, 0, 4);
                        
                        if (bytesRead == 0) break;
                        
                        var length = BitConverter.ToInt32(lengthBytes, 0);
                        var data = new byte[length];
                        var totalRead = 0;
                        
                        while (totalRead < length)
                        {
                            bytesRead = await stream.ReadAsync(data, totalRead, length - totalRead);
                            if (bytesRead == 0) break;
                            totalRead += bytesRead;
                        }
                        
                        var msg = MessagePackSerializer.Deserialize<GameMessage>(data);
                        Console.WriteLine($"[Test] {playerName} reçoit: {msg.Type}");
                    }
                }
                catch { }
            });
            
            // Attendre la fin ou timeout
            await Task.WhenAny(
                moveTask,
                Task.Delay(15000)
            );
            
            // Déconnexion
            await SendMessageAsync(stream, new GameMessage
            {
                Type = MessageType.PlayerDisconnect,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test] ❌ Erreur {playerName}: {ex.Message}");
        }
        
        Console.WriteLine($"[Test] {playerName} déconnecté");
    }
    
    private async Task SendMessageAsync(NetworkStream stream, GameMessage message)
    {
        try
        {
            var data = MessagePackSerializer.Serialize(message);
            var lengthBytes = BitConverter.GetBytes(data.Length);
            
            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Test] ❌ Erreur d'envoi: {ex.Message}");
        }
    }
    
    public static async Task Main(string[] args)
    {
        var test = new TestGameScript();
        await test.RunAsync();
    }
}
