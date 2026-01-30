using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Tiema.Abstractions;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

namespace Tiema.Runtime
{
    // core container for Tiema applications
    public class TiemaContainer : IPluginContainer
    {
        private readonly Dictionary<string, IPlugin> _plugins = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly TiemaConfig _config;

        private readonly ITagService _tag_service;
        private readonly IMessageService _message_service;

        public TiemaContainer(
            TiemaConfig config,
            ITagService tagService,
            IMessageService messageService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tag_service = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _message_service = messageService ?? throw new ArgumentNullException(nameof(messageService));
        }

        public ITagService TagService => _tag_service;
        public IMessageService MessageService => _message_service;

        /// <summary>
        /// 加载插件：解析路径、通过 PluginLoader 创建实例（不执行初始化），
        /// 然后由容器统一创建 DefaultPluginContext 并调用 Initialize/Start。
        /// Load plugins: resolve path, create instance via PluginLoader (no init),
        /// then container creates DefaultPluginContext and calls Initialize/Start.
        /// </summary>
        public void LoadPlugins()
        {
            if (_config.Plugins == null || _config.Plugins.Count == 0)
            {
                Console.WriteLine("配置中没有找到插件 / No plugins configured");
                return;
            }

            foreach (var pluginConfig in _config.Plugins)
            {
                if (!pluginConfig.Enabled)
                {
                    Console.WriteLine($"跳过禁用插件: {pluginConfig.Name} / Skipping disabled plugin: {pluginConfig.Name}");
                    continue;
                }

                try
                {
                    var rawPath = pluginConfig.Path ?? string.Empty;
                    var pluginPath = rawPath;
                    if (!Path.IsPathRooted(pluginPath))
                    {
                        pluginPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, pluginPath));
                    }

                    if (!File.Exists(pluginPath))
                    {
                        Console.WriteLine($"插件文件不存在，跳过: {pluginConfig.Name}, path: {pluginPath} / Plugin file not found, skipping");
                        continue;
                    }

                    // 仅创建插件实例（不做初始化），初始化由容器统一负责
                    var plugin = PluginLoader.Load(pluginPath);
                    if (plugin == null)
                    {
                        Console.WriteLine($"未能实例化插件: {pluginConfig.Name} / Failed to instantiate plugin");
                        continue;
                    }

                    // 为插件创建上下文并统一调用 Initialize/Start
                    var pluginContext = new DefaultPluginContext(this);

                    // 唯一 id（避免冲突）
                    var pluginId = $"{plugin.Name}_{Guid.NewGuid():N}".Substring(0, 20);
                    _plugins[pluginId] = plugin;

                    // 由容器统一初始化并启动插件（将 Initialize 作为唯一初始化契约）
                    plugin.Initialize(pluginContext);
                    plugin.Start();

                    Console.WriteLine($"已加载插件: {plugin.Name} ({pluginId}) / Loaded plugin");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 加载插件失败: {pluginConfig.Name}, 路径: {pluginConfig.Path} / Failed to load plugin: {ex}");
                }
            }

            Console.WriteLine($"total {_plugins.Count} plugins");
        }

        /// <summary>
        /// 运行容器并阻塞直到 Stop 被调用。
        /// Run the container and block until Stop is called.
        /// </summary>
        public void Run()
        {
            Console.WriteLine($"Container running. Press Ctrl+C to stop. / Container running.");

            // 捕获 Ctrl+C，转换为 Stop 信号
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true; // 阻止进程立即退出，交由 Stop 处理
                Console.WriteLine("Cancel requested, stopping container...");
                Stop();
            };

            try
            {
                // 阻塞直到取消令牌被触发（避免占用 CPU）
                _cts.Token.WaitHandle.WaitOne();
            }
            finally
            {
                // 在退出前确保所有插件被停止并清理
                foreach (var plugin in _plugins.Values)
                {
                    try
                    {
                        plugin.Stop();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 停止插件 {plugin.Name} 失败: {ex.Message}");
                    }
                }

                Console.WriteLine("Container stopped.");
            }
        }

        /// <summary>
        /// 请求停止容器（会使 Run() 返回）。
        /// Request container stop (causes Run() to return).
        /// </summary>
        public void Stop() => _cts.Cancel();
    }
}
