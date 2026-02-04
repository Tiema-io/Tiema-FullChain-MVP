
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;
using Tiema.Protocols.V1;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 扫描模块上的 TiemaTagAttribute，完成注册并建立订阅/可选自动发布。
    /// 在 TiemaHost.LoadModule 的 module.Initialize() 后调用。
    /// 返回的 IDisposable 列表由宿主保存并在卸载时 Dispose。
    /// </summary>
    public static class TagAutoRegistrar
    {
        public static IList<IDisposable> RegisterAndWire(
            object moduleInstance,
            IModuleContext moduleContext,
            ITagRegistrationManager regManager,
            ITagService tagService)
        {
            var disposables = new List<IDisposable>();
            if (moduleInstance == null) return disposables;

            var type = moduleInstance.GetType();
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

            // 1) 注册（会走 InMemory 或 Grpc 实现）
            // 使用 moduleContext.ModuleInstanceId 作为注册的 module/plugin id（LoadModule 时已生成）
            var identities = regManager.RegisterModuleTags(moduleContext.ModuleInstanceId,
                producers, consumers);

            // 2) 通知内置 tagService 完成注册以便建立 consumer 订阅（若实现需）
            if (tagService is BuiltInTagService builtIn)
            {
                try { builtIn.OnTagsRegistered(identities); } catch { }
            }

            // 3) 为 annotated consumers 建立订阅 -> 把更新写入成员或调用方法
            foreach (var (member, attr) in annotated.Where(a => a.attr.Role == TagRole.Consumer))
            {
                var path = attr.Path;
                var sub = moduleContext.Tags.SubscribeTag(path, val =>
                {
                    try
                    {
                        if (member is PropertyInfo pi && pi.CanWrite)
                        {
                            var converted = ConvertValueIfNeeded(val, pi.PropertyType);
                            pi.SetValue(moduleInstance, converted);
                        }
                        else if (member is FieldInfo fi)
                        {
                            var converted = ConvertValueIfNeeded(val, fi.FieldType);
                            fi.SetValue(moduleInstance, converted);
                        }
                        else if (member is MethodInfo mi)
                        {
                            var parameters = mi.GetParameters();
                            if (parameters.Length == 1)
                            {
                                var pType = parameters[0].ParameterType;
                                var converted = ConvertValueIfNeeded(val, pType);
                                mi.Invoke(moduleInstance, new[] { converted });
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

            // 4) 为 annotated producers 启动可选的周期自动 publish（读取成员并 SetTag）
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
                            if (member is PropertyInfo pi && pi.CanRead) value = pi.GetValue(moduleInstance);
                            else if (member is FieldInfo fi) value = fi.GetValue(moduleInstance);
                            else if (member is MethodInfo mi && mi.GetParameters().Length == 0) value = mi.Invoke(moduleInstance, null);

                            if (value != null)
                            {
                                moduleContext.Tags.SetTag(path, value);
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