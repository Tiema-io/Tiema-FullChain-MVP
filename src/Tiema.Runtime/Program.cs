//Tiema.Runtime/Program.cs 
using System;
using System.Text.Json;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

namespace Tiema.Runtime
{
    /// <summary>
    /// 程序入口：负责启动 Tiema 容器并运行。
    /// Program entry: responsible for creating and running the Tiema container.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 应用程序入口点（常见签名）。
        /// Application entry point (common signature).
        /// </summary>
        /// <param name="args">命令行参数 / command-line arguments</param>
        public static void Main(string[] args)
        {
            Console.WriteLine("=== Tiema Runtime v0.1 ====");

            try
            {
                var config = Utility.LoadConfiguration("tiema.config.json");
                var container = new TiemaContainer(config, new BuiltInTagService(), new BuiltInMessageService());


                // 根据配置批量加载模块（会自动尝试将模块插入到配置指定的 rack/slot）
                container.LoadModules();

                // 运行并阻塞直到 Stop() 被调用（或 Ctrl+C）
                container.Run();
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