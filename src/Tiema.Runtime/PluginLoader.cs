using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Runtime
{
    using global::Tiema.Abstractions;
    // Tiema.Runtime/PluginLoader.cs
    using System;
    using System.IO;
    using System.Reflection;


    namespace Tiema.Runtime
    {
        public static class PluginLoader
        {
            public static IPlugin Load(string pluginPath)
            {
                if (!File.Exists(pluginPath))
                    throw new FileNotFoundException($"插件文件不存在: {pluginPath}");

                try
                {
                    // 加载程序集
                    var assembly = Assembly.LoadFrom(pluginPath);

                    // 查找实现了IPlugin接口的类型
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                        {
                            // 创建插件实例
                            var plugin = Activator.CreateInstance(type) as IPlugin;
                            return plugin;
                        }
                    }

                    throw new InvalidOperationException($"在 {pluginPath} 中未找到实现IPlugin接口的类型");
                }
                catch (Exception ex)
                {
                    throw new PluginLoadException($"加载插件失败: {pluginPath}", ex);
                }
            }
        }

        public class PluginLoadException : Exception
        {
            public PluginLoadException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
    }
}
