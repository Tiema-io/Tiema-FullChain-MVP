using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;
using Tiema.Tags.Grpc.V1; // generated types from tagsystem.proto

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// Scan TiemaTagAttribute on plugin instances, register tags, wire subscriptions, and optional auto-publish.
    /// Called after Initialize(IPluginContext) in TiemaHost.LoadModule. Returns disposables to be disposed on unload.
    /// </summary>
    public static class TagAutoRegistrar
    {
        public static IList<IDisposable> RegisterAndWire(
            object pluginInstance,
            IPluginContext pluginContext,
            ITagRegistrationManager regManager,
            ITagService tagService)
        {
            var disposables = new List<IDisposable>();
            if (pluginInstance == null) return disposables;

            var type = pluginInstance.GetType();
            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var producers = new List<string>();
            var consumers = new List<string>();
            var annotated = new List<(MemberInfo member, TiemaTagAttribute attr)>();

            foreach (var m in members)
            {
                var attr = m.GetCustomAttribute<TiemaTagAttribute>();
                if (attr == null) continue;
                annotated.Add((m, attr));
                if (attr.Role == TagRole.Producer) producers.Add(attr.Path);
                else consumers.Add(attr.Path);
            }

            // 1) Registration (InMemory or gRPC implementation)
            // Use pluginContext.PluginInstanceId for plugin id
            var identities = regManager.RegisterModuleTags(pluginContext.PluginInstanceId, producers, consumers);

            // 2) Notify built-in tagService to subscribe consumers if needed
            if (tagService is BuiltInTagService builtIn)
            {
                try { builtIn.OnTagsRegistered(identities); } catch { }
            }

            // 3) For consumer tags, subscribe and write updates to members/methods
            foreach (var (member, attr) in annotated.Where(a => a.attr.Role == TagRole.Consumer))
            {
                var path = attr.Path;
                var sub = pluginContext.Tags.SubscribeTag(path, val =>
                {
                    try
                    {
                        if (member is PropertyInfo pi && pi.CanWrite)
                        {
                            var converted = ConvertValueIfNeeded(val, pi.PropertyType);
                            pi.SetValue(pluginInstance, converted);
                        }
                        else if (member is FieldInfo fi)
                        {
                            var converted = ConvertValueIfNeeded(val, fi.FieldType);
                            fi.SetValue(pluginInstance, converted);
                        }
                        else if (member is MethodInfo mi)
                        {
                            var parameters = mi.GetParameters();
                            if (parameters.Length == 1)
                            {
                                var pType = parameters[0].ParameterType;
                                var converted = ConvertValueIfNeeded(val, pType);
                                mi.Invoke(pluginInstance, new[] { converted });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Auto subscription callback failed for {path}: {ex.Message}");
                    }
                });

                disposables.Add(sub);
            }

            // 4) For producer tags, start optional periodic auto-publish
            foreach (var (member, attr) in annotated.Where(a => a.attr.Role == TagRole.Producer && a.attr.AutoPublishIntervalMs > 0))
            {
                var path = attr.Path;
                var interval = Math.Max(50, attr.AutoPublishIntervalMs);
                var cts = new CancellationTokenSource();
                disposables.Add(new CancellationDisposable(cts));

                _ = Task.Run(async () =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        try
                        {
                            object? value = null;
                            if (member is PropertyInfo pi && pi.CanRead) value = pi.GetValue(pluginInstance);
                            else if (member is FieldInfo fi) value = fi.GetValue(pluginInstance);
                            else if (member is MethodInfo mi && mi.GetParameters().Length == 0) value = mi.Invoke(pluginInstance, null);

                            if (value != null)
                            {
                                pluginContext.Tags.SetTag(path, value);
                            }
                        }
                        catch { /* best-effort */ }

                        try { await Task.Delay(interval, cts.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }, cts.Token);
            }

            return disposables;
        }

        private static object? ConvertValueIfNeeded(object? src, Type targetType)
        {
            if (src == null) return null;
            if (targetType.IsInstanceOfType(src)) return src;
            try { return Convert.ChangeType(src, targetType); }
            catch { return src; }
        }

        private sealed class CancellationDisposable : IDisposable
        {
            private readonly CancellationTokenSource _cts;
            public CancellationDisposable(CancellationTokenSource cts) { _cts = cts; }
            public void Dispose() { try { _cts.Cancel(); _cts.Dispose(); } catch { } }
        }
    }
}