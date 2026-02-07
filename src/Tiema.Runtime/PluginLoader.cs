using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Tiema.Contracts;

namespace Tiema.Runtime
{
    // Plugin loader: loads an assembly and creates an instance of the first exported type
    // that implements IPlugin. Uses a parameterless constructor and does NOT call Initialize;
    // initialization is handled by the host (IPluginHost/TiemaContainer).
    public static class PluginLoader
    {
        // Load a plugin assembly and create an instance of the first IPlugin implementation found.
        // The instance is returned without calling Initialize; host must call Initialize(context) and Start().
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
                {
                    Console.WriteLine($"[WARN] multiple IPlugin implementations found in {Path.GetFileName(pluginPath)}; selecting the first.");
                }

                var type = pluginTypes[0];

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                    throw new MissingMethodException($"Type {type.FullName} must provide a parameterless constructor.");

                var instance = Activator.CreateInstance(type);
                if (instance is not IPlugin plugin)
                    throw new InvalidCastException($"Type {type.FullName} could not be cast to IPlugin.");

                // NOTE: do not call Initialize here; host will call Initialize(context) and Start()
                return plugin;
            }
            catch (Exception ex)
            {
                throw new PluginLoadException($"Failed to load plugin: {pluginPath}", ex);
            }
        }
    }

    // Plugin load exception
    public class PluginLoadException : Exception
    {
        public PluginLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}