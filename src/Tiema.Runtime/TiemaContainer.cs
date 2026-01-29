using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Tiema.Abstractions;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;
using Tiema.Runtime.Tiema.Runtime;

namespace Tiema.Runtime
{

    // core container for Tiema applications
    public class TiemaContainer:IPluginContainer
    {
        // 修改 _plugins 的类型为 Dictionary<string, IPlugin>
        private Dictionary<string, IPlugin> _plugins = new();
        private CancellationTokenSource _cts = new();
        private readonly TiemaConfig _config;

        private readonly ITagService _tagService;
        private readonly IMessageService _messageService;
        private readonly IServiceProvider _serviceProvider;



        public TiemaContainer(
          TiemaConfig config,
          ITagService tagService,
          IMessageService messageService
      
      

            
            )
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        
           

        }
        public ITagService TagService => _tagService;
        public IMessageService MessageService => _messageService;


        public void LoadPlugins()
        {
            if (!_config.Plugins.Any())
            {
                Console.WriteLine("配置中没有找到插件");
                return;
            }
            // 修改 LoadPlugins 方法中的 foreach 循环
            foreach (var pluginConfig in _config.Plugins)
            {
                if (!pluginConfig.Enabled)
                {
                    Console.WriteLine($"跳过禁用插件: {pluginConfig.Name}");
                    continue;
                }

                try
                {
                    var plugin = PluginLoader.Load(pluginConfig.Path);
                    if (plugin != null)
                    {
                        var pluginId = $"{plugin.Name}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                        _plugins[pluginId] = plugin; // 这里 pluginId 是 string，Dictionary 支持

                        // 创建插件上下文
                        var pluginContext = new DefaultPluginContext(
                            container: this
                           );

                        // 初始化插件
                        plugin.Initialize(pluginContext);

                        Console.WriteLine($"已加载插件: {plugin.Name} ({pluginId})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex+ $",加载插件失败: {pluginConfig.Name}, 路径: {pluginConfig.Path}");
                }
            }

            Console.WriteLine($"共加载 {_plugins.Count} 个插件");


        }

        public void Run()
        {
            Console.WriteLine($"开始运行，周期: {_config.Container.ScanIntervalMs}ms");

            // 初始化所有插件
            // 修改 Run 方法中的插件遍历方式
            foreach (var plugin in _plugins.Values)
            {
                plugin.Initialize(new DefaultPluginContext(this));
            }

            // 主循环
            int cycleCount = 0;
            while (!_cts.IsCancellationRequested)
            {
                var cycleStart = DateTime.Now;

                // 执行所有插件
                // 主循环中执行插件
                foreach (var plugin in _plugins.Values)
                {
                    try
                    {
                        plugin.Execute(new DefaultCycleContext(_tagService,_messageService));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 插件 {plugin.Name} 执行失败: {ex.Message}");
                    }
                }
                // 等待下一个周期
                var elapsed = (DateTime.Now - cycleStart).TotalMilliseconds;
                var waitTime = _config.Container.ScanIntervalMs - (int)elapsed;

                if (waitTime > 0)
                {
                    Thread.Sleep(waitTime);
                }

                cycleCount++;

                // 每100周期输出状态
                if (cycleCount % 100 == 0)
                {
                    Console.WriteLine($"已运行 {cycleCount} 个周期");
                }
            }
        }

        public void Stop() => _cts.Cancel();
    }

}
