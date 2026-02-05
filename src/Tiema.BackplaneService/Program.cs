using System;
using System.Threading;
using Grpc.Core;
using Tiema.Protocols.V1;
using Tiema.Runtime.Services;

namespace Tiema.BackplaneService
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("=== Tiema 数据总线（Tiema Backplane, TB） - MVP ===");

            var bindHost = Environment.GetEnvironmentVariable("TIEMA_BACKPLANE_HOST") ?? "0.0.0.0";
            var portStr = Environment.GetEnvironmentVariable("TIEMA_BACKPLANE_PORT") ?? "50051";
            if (!int.TryParse(portStr, out var port)) port = 50051;

            var listenHost = bindHost;
            var server = new Server
            {
                Services = { Backplane.BindService(new TiemaBackplaneServer()) },
                Ports = { new ServerPort(listenHost, port, ServerCredentials.Insecure) }
            };

            try
            {
                server.Start();
                Console.WriteLine($"[INFO] Tiema 数据总线（TB）已启动，地址: {listenHost}:{port}");
                Console.WriteLine("[INFO] 按 Ctrl+C 停止服务");

                var exit = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    exit.Set();
                };

                exit.WaitOne();

                Console.WriteLine("[INFO] 正在优雅关闭 TB 服务...");
                server.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                Console.WriteLine("[INFO] TB 服务已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] TB 启动/运行失败: {ex.Message}");
            }
        }
    }
}
