using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Tiema.Abstractions;

namespace Tiema.Runtime
{
    /// <summary>
    /// 插件加载器：负责从程序集加载实现了 IPlugin 的类型并实例化。
    /// Plugin loader: loads assemblies, finds types implementing IPlugin and instantiates them.
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// 加载插件文件并返回插件实例。
        /// 如果提供了 IPluginContext，会优先尝试使用带 IPluginContext 的构造器注入；否则使用无参构造并在必要时调用 Initialize。
        /// Load plugin assembly and return an IPlugin instance.
        /// If an IPluginContext is provided, constructor injection (ctor(IPluginContext)) is attempted first;
        /// otherwise a parameterless ctor is used and Initialize(context) will be called if applicable.
        /// </summary>
        /// <param name="pluginPath">插件程序集路径 / plugin assembly path</param>
        /// <param name="context">可选的插件上下文 / optional plugin context</param>
        /// <returns>实例化的插件 / instantiated plugin</returns>
        public static IPlugin Load(string pluginPath, IPluginContext? context = null)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                throw new ArgumentException("pluginPath 不能为空 / pluginPath must not be null or empty", nameof(pluginPath));

            if (!File.Exists(pluginPath))
                throw new FileNotFoundException($"插件文件不存在: {pluginPath} / Plugin file not found: {pluginPath}");

            try
            {
                // 使用 AssemblyLoadContext 从绝对路径加载程序集，便于插件依赖解析
                // Use AssemblyLoadContext to load assembly from path to improve dependency resolution.
                var assemblyPath = Path.GetFullPath(pluginPath);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

                // 查找实现 IPlugin 的导出类型，优先 public 类型
                // Find exported types implementing IPlugin (prefer public types).
                var pluginTypes = assembly.GetExportedTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    .ToArray();

                if (pluginTypes.Length == 0)
                    throw new InvalidOperationException($"在程序集 {pluginPath} 中未找到实现 IPlugin 的类型 / No IPlugin implementation found in assembly: {pluginPath}");

                if (pluginTypes.Length > 1)
                {
                    // 如果找到多个实现，选择第一个并记录（宿主可改用更严格策略）
                    // If multiple implementations found, pick the first one. Host may adopt stricter selection logic.
                    Console.WriteLine($"警告: 在程序集 {Path.GetFileName(pluginPath)} 中找到多个 IPlugin 实现，已选择第一个。/ Warning: multiple IPlugin implementations found; selecting the first.");
                }

                var type = pluginTypes[0];

                // 实例化优先策略：
                // 1) 如果提供了 context，尝试 ctor(IPluginContext)
                // 2) 否则尝试无参 ctor
                // 3) 如果只有带参数 ctor 且 context 为 null，则报错提示宿主应提供 context
                object? instance = null;
                Exception? lastEx = null;

                if (context != null)
                {
                    var ctorWithContext = type.GetConstructor(new[] { typeof(IPluginContext) });
                    if (ctorWithContext != null)
                    {
                        instance = ctorWithContext.Invoke(new object[] { context });
                    }
                }

                if (instance == null)
                {
                    // 试试无参构造
                    var parameterlessCtor = type.GetConstructor(Type.EmptyTypes);
                    if (parameterlessCtor != null)
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    else
                    {
                        // 无无参 ctor，试着找到第一个可调用的构造并抛出更明确的错误
                        try
                        {
                            // 若只有带 context 的构造且 context 为 null，给出提示
                            var ctors = type.GetConstructors();
                            throw new MissingMethodException($"类型 {type.FullName} 没有无参构造。若要使用构造器注入，请在调用 PluginLoader.Load 时传入 IPluginContext。/ Type {type.FullName} has no parameterless ctor. To use ctor injection, call PluginLoader.Load with an IPluginContext.");
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                        }
                    }
                }

                if (instance == null)
                {
                    // 尝试性地抛出上一次捕获的异常，否则返回通用错误
                    throw lastEx ?? new InvalidOperationException($"无法实例化插件类型 {type.FullName} / Failed to instantiate plugin type {type.FullName}");
                }

                var plugin = instance as IPlugin;
                if (plugin == null)
                    throw new InvalidCastException($"类型 {type.FullName} 未能转换为 IPlugin / Type {type.FullName} could not be cast to IPlugin");

                // 如果通过无参构造创建并且提供了 context，则调用 Initialize 以兼容旧实现
                // If created with parameterless ctor and a context was provided, call Initialize for backward-compatibility.
                if (context != null)
                {
                    // PluginBase 内部会保护重复初始化（如果构造器已执行初始化则不会重复）
                    plugin.Initialize(context);
                }

                return plugin;
            }
            catch (Exception ex)
            {
                throw new PluginLoadException($"加载插件失败: {pluginPath} / Failed to load plugin: {pluginPath}", ex);
            }
        }
    }

    /// <summary>
    /// 插件加载异常类型 / Plugin load exception type
    /// </summary>
    public class PluginLoadException : Exception
    {
        public PluginLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}