using System;
using System.Threading;
using Grpc.Core;
using Tiema.DataConnect.Core;
using Tiema.Connect.Grpc.V1;
using static Tiema.Connect.Grpc.V1.DataConnect; // for BindService

namespace Tiema.BackplaneService
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("=== Tiema DataConnect - MVP ===");

            var bindHost = Environment.GetEnvironmentVariable("TIEMA_DATACONNECT_HOST") ?? "0.0.0.0";
            var portStr = Environment.GetEnvironmentVariable("TIEMA_DATACONNECT_PORT") ?? "50051";
            if (!int.TryParse(portStr, out var port)) port = 50051;

            var server = new Server
            {
                Services = { BindService(new TiemaDataConnectServer()) },
                Ports = { new ServerPort(bindHost, port, ServerCredentials.Insecure) }
            };

            try
            {
                server.Start();
                Console.WriteLine($"[INFO] DataConnect started at {bindHost}:{port}");
                Console.WriteLine("[INFO] Press Ctrl+C to stop");

                var exit = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; exit.Set(); };
                exit.WaitOne();

                Console.WriteLine("[INFO] Shutting down...");
                server.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                Console.WriteLine("[INFO] Stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to start/run: {ex.Message}");
            }
        }
    }
}
