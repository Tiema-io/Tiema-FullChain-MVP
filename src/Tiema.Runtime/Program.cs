//Tiema.Runtime/Program.cs 
using System;
using System.IO;
using System.Text.Json;
using Tiema.Runtime.Models;
using Grpc.Core;
using Tiema.Runtime.Services;
using Tiema.Protocols.V1;

namespace Tiema.Runtime
{
    /// <summary>
    /// 程序入口：负责启动 Tiema 容器并运行。
    /// Program entry: responsible for creating and running the Tiema container.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用程序入口点（常见签名）。
        /// Application entry point (common signature).
        /// </summary>
        /// <param name="args">命令行参数 / command-line arguments</param>
        private static void Main(string[] args)
        {
            Console.WriteLine("=== Tiema Runtime v0.1 ===");

            Server? grpcServer = null;

            try
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "tiema.config.json");
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<TiemaConfig>(json)
                             ?? throw new InvalidOperationException("Failed to load tiema.config.json");

                // 构建 HostBuilder（先创建，后根据配置选择 backplane）
                var hostBuilder = TiemaHostBuilder.Create(config);

                // 如果配置启用 messaging 且选择了 grpc，则启动本地 gRPC Backplane 服务并把 client URL 注入到 HostBuilder
                if (config.Messaging != null && config.Messaging.Enabled &&
                    string.Equals(config.Messaging.Transport, "grpc", StringComparison.OrdinalIgnoreCase))
                {
                    var bindHost = string.IsNullOrWhiteSpace(config.Messaging.Host) ? "0.0.0.0" : config.Messaging.Host;
                    var port = config.Messaging.Port > 0 ? config.Messaging.Port : 50051;

                    try
                    {
                        grpcServer = new Server
                        {
                            Services = { Backplane.BindService(new GrpcBackplaneServer()) },
                            Ports = { new ServerPort(bindHost, port, ServerCredentials.Insecure) }
                        };
                        grpcServer.Start();
                        Console.WriteLine($"[INFO] gRPC Backplane server started on {bindHost}:{port}");

                        // client 连接地址：若 bindHost 为 0.0.0.0 则使用 loopback 地址作为 client 目标
                        var clientHost = bindHost == "0.0.0.0" ? "127.0.0.1" : bindHost;

                        // Grpc.Net.Client 需要带 scheme 的地址（例如 http://host:port）
                        var grpcUrl = $"http://{clientHost}:{port}";

                        hostBuilder = hostBuilder.UseGrpcBackplane(grpcUrl);
                        Console.WriteLine($"[INFO] HostBuilder configured to use gRPC backplane at {grpcUrl}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Failed to start gRPC Backplane server: {ex.Message}");
                        // 如果 server 启动失败，不中断主流程；HostBuilder 不注入 gRPC backplane，继续使用默认（InMemory）
                    }
                }
                else
                {
                    // 如果显式配置为 inmemory，或未启用 messaging，使用 InMemoryBackplane（宿主级选择）
                    if (config.Messaging == null || !config.Messaging.Enabled || string.Equals(config.Messaging.Transport, "inmemory", StringComparison.OrdinalIgnoreCase))
                    {
                        hostBuilder = hostBuilder.UseInMemoryBackplane();
                        Console.WriteLine("[INFO] HostBuilder configured to use InMemory backplane");
                    }
                }

                // 使用 HostBuilder 构建并运行 Host
                var host = hostBuilder.Build();

                host.LoadModules();
                host.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex);
            }
            finally
            {
                // 优雅关闭 gRPC 服务（同步等待）
                if (grpcServer != null)
                {
                    try
                    {
                        Console.WriteLine("[INFO] Shutting down gRPC Backplane server...");
                        grpcServer.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Error shutting down gRPC server: {ex.Message}");
                    }
                }
            }
        }
    }
}