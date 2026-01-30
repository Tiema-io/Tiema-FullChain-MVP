using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Tiema.Abstractions;

namespace Tiema.Runtime
{
    /// <summary>
    /// 插件加载器：严格使用无参构造创建插件实例，由宿主统一调用 Initialize/Start。
    /// Plugin loader: strictly uses parameterless ctor to create plugin instances; host calls Initialize/Start.
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// 加载插件程序集并实例化第一个 IPlugin 实现（仅使用无参构造）。
        /// Load plugin assembly and instantiate the first IPlugin implementation (parameterless ctor only).
        /// </summary>
        /// <param name="pluginPath">插件程序集路径 / plugin assembly path</param>
        /// <returns>已创建但未初始化的 IPlugin 实例 / created but not-initialized IPlugin instance</returns>
        public static IPlugin Load(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                throw new ArgumentException("pluginPath must not be null or empty", nameof(pluginPath));

            if (!File.Exists(pluginPath))
                throw new FileNotFoundException($"Plugin file not found: {pluginPath}", pluginPath);

            try
            {
                var assemblyPath = Path.GetFullPath(pluginPath);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

                var pluginTypes = assembly.GetExportedTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    .ToArray();

                if (pluginTypes.Length == 0)
                    throw new InvalidOperationException($"No IPlugin implementation found in assembly: {pluginPath}");

                if (pluginTypes.Length > 1)
                    Console.WriteLine($"Warning: multiple IPlugin implementations found in {Path.GetFileName(pluginPath)}; selecting the first.");

                var type = pluginTypes[0];

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                    throw new MissingMethodException($"Type {type.FullName} must provide a parameterless constructor.");

                var instance = Activator.CreateInstance(type);
                if (instance is not IPlugin plugin)
                    throw new InvalidCastException($"Type {type.FullName} could not be cast to IPlugin.");

                // 不在这里调用 Initialize，交由宿主统一负责
                return plugin;
            }
            catch (Exception ex)
            {
                throw new PluginLoadException($"Failed to load plugin: {pluginPath}", ex);
            }
        }
    }

    /// <summary>
    /// 插件加载异常 / Plugin load exception
    /// </summary>
    public class PluginLoadException : Exception
    {
        public PluginLoadException(string message, Exception inner) : base(message, inner) { }
    }
}