// Tiema.Runtime/Program.cs
using System;
using System.IO;
using System.Text.Json;
using Tiema.Runtime.Models;
using Grpc.Core;
using Tiema.Connect.Grpc.V1; // DataConnect service
using Tiema.DataConnect.Core; // generated from connect.proto (DataConnect)
using static Tiema.Connect.Grpc.V1.DataConnect; // for BindService

namespace Tiema.Runtime
{
    // Program entry: create and run the Tiema container.
    internal static class Program
    {
        // Application entry point
        private static void Main(string[] args)
        {
            Console.WriteLine("=== Tiema DataConnect (MVP) ===");

            Server? grpcServer = null;

            try
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "tiema.config.json");
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<TiemaConfig>(json)
                             ?? throw new InvalidOperationException("Failed to load tiema.config.json");

                // Build HostBuilder then choose backplane per config
                var hostBuilder = TiemaHostBuilder.Create(config);

                // If messaging enabled and transport=grpc, start local DataConnect and inject client URL
                if (config.Messaging != null && config.Messaging.Enabled &&
                    string.Equals(config.Messaging.Transport, "grpc", StringComparison.OrdinalIgnoreCase))
                {
                    var bindHost = string.IsNullOrWhiteSpace(config.Messaging.Host) ? "0.0.0.0" : config.Messaging.Host;
                    var port = config.Messaging.Port > 0 ? config.Messaging.Port : 50051;

                    try
                    {
                        grpcServer = new Server
                        {
                            Services = { BindService(new TiemaDataConnectServer()) },
                            Ports = { new ServerPort(bindHost, port, ServerCredentials.Insecure) }
                        };
                        grpcServer.Start();
                        Console.WriteLine($"[INFO] DataConnect started: {bindHost}:{port}");

                        // client target: use loopback when binding to 0.0.0.0
                        var clientHost = bindHost == "0.0.0.0" ? "127.0.0.1" : bindHost;

                        // Grpc.Net.Client requires scheme
                        var grpcUrl = $"http://{clientHost}:{port}";

                        hostBuilder = hostBuilder.UseGrpcBackplane(grpcUrl);
                        Console.WriteLine($"[INFO] HostBuilder configured to use DataConnect at: {grpcUrl}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to start DataConnect: {ex.Message}");
                        // Fallback: do not inject gRPC backplane, continue with default (InMemory)
                    }
                }
                else
                {
                    // If messaging disabled or explicitly inmemory, use InMemory backplane
                    if (config.Messaging == null || !config.Messaging.Enabled ||
                        string.Equals(config.Messaging.Transport, "inmemory", StringComparison.OrdinalIgnoreCase))
                    {
                        hostBuilder = hostBuilder.UseInMemoryBackplane();
                        Console.WriteLine("[INFO] HostBuilder configured to use InMemory backplane (local debug only)");
                    }
                }

                var host = hostBuilder.Build();

                host.LoadPlugins();
                host.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex);
            }
            finally
            {
                // Gracefully shutdown gRPC service
                if (grpcServer != null)
                {
                    try
                    {
                        Console.WriteLine("[INFO] Shutting down DataConnect...");
                        grpcServer.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Shutdown DataConnect failed: {ex.Message}");
                    }
                }
            }
        }
    }
}