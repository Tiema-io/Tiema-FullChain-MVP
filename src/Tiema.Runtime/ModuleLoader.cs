using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Tiema.Contracts;


namespace Tiema.Runtime
{
    /// <summary>
    /// Module loader: loads an assembly and creates an instance of the first exported type
    /// that implements IModule. Uses a parameterless constructor and does NOT call Initialize;
    /// initialization is handled by the host (IModuleHost / TiemaContainer).
    /// 模块加载器：加载程序集并实例化第一个导出的实现 IModule 的类型。使用无参构造且不在此处调用 Initialize；
    /// 初始化由宿主（IModuleHost / TiemaContainer）统一负责。
    /// </summary>
    public static class ModuleLoader
    {
        /// <summary>
        /// Load a module assembly and create an instance of the first IModule implementation found.
        /// The instance is returned without calling Initialize; host must call Initialize(context) and Start().
        /// 从程序集加载模块并创建第一个 IModule 实现的实例。返回的实例不会被初始化；宿主需调用 Initialize(context) 与 Start().
        /// </summary>
        /// <param name="modulePath">Path to the module assembly (absolute or relative)</param>
        /// <returns>Created instance implementing IModule</returns>
        public static IModule Load(string modulePath)
        {
            if (string.IsNullOrWhiteSpace(modulePath))
                throw new ArgumentException("modulePath must not be null or empty / modulePath 不能为空", nameof(modulePath));

            if (!File.Exists(modulePath))
                throw new FileNotFoundException($"Module file not found: {modulePath} / 模块文件不存在: {modulePath}", modulePath);

            try
            {
                var assemblyPath = Path.GetFullPath(modulePath);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

                var moduleTypes = assembly.GetExportedTypes()
                    .Where(t => typeof(IModule).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    .ToArray();

                if (moduleTypes.Length == 0)
                    throw new InvalidOperationException($"No IModule implementation found in assembly: {modulePath} / 在程序集未找到 IModule 实现: {modulePath}");

                if (moduleTypes.Length > 1)
                {
                    Console.WriteLine($"Warning: multiple IModule implementations found in {Path.GetFileName(modulePath)}; selecting the first. / 警告: 在程序集找到多个 IModule 实现，将选择第一个。");
                }

                var type = moduleTypes[0];

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                    throw new MissingMethodException($"Type {type.FullName} must provide a parameterless constructor. / 类型 {type.FullName} 需要提供无参构造函数。");

                var instance = Activator.CreateInstance(type);
                if (instance is not IModule module)
                    throw new InvalidCastException($"Type {type.FullName} could not be cast to IModule. / 类型 {type.FullName} 无法转换为 IModule。");

                // NOTE: do not call Initialize here; host will call Initialize(context) and Start()
                return module;
            }
            catch (Exception ex)
            {
                throw new ModuleLoadException($"Failed to load module: {modulePath} / 加载模块失败: {modulePath}", ex);
            }
        }
    }

    /// <summary>
    /// Module load exception (bilingual message support).
    /// 模块加载异常类型（支持中英信息）。
    /// </summary>
    public class ModuleLoadException : Exception
    {
        public ModuleLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}