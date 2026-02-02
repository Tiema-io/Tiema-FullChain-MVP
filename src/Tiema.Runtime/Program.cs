//Tiema.Runtime/Program.cs 
using System;
using System.IO;
using System.Text.Json;
using Tiema.Runtime.Models;

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

            try
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "tiema.config.json");
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<TiemaConfig>(json)
                             ?? throw new InvalidOperationException("Failed to load tiema.config.json");

                // 使用最小版 HostBuilder 创建 TiemaHost
                // Use minimal HostBuilder to create TiemaHost.
                var host = TiemaHostBuilder
                    .Create(config)
                    // 当前使用默认实现，不额外调用 UseXXX
                    .Build();

                host.LoadModules();
                host.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Console.Error.WriteLine(ex);
                // 若需要退出码可在宿主脚本中根据输出判断
            }
        }
    }
}