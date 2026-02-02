using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 容器内置的 Tag 服务实现：委托给 Backplane 存储和推送。
    /// Built-in tag service implementation: delegates to Backplane for storage and pushing.
    /// </summary>
    public class BuiltInTagService : ITagService
    {
        private readonly HashSet<string> _producers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _consumers = new(StringComparer.OrdinalIgnoreCase);

        // 订阅缓存（单进程用；跨进程时移除）。
        private readonly ConcurrentDictionary<uint, IDisposable> _subscriptions = new();
        private readonly ConcurrentDictionary<string, List<Action<object>>> _subscribers =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ITagRegistrationManager _registrationManager;
        private readonly IBackplane _backplane;
        private readonly ConcurrentDictionary<uint, IDisposable> _backplaneSubscriptions = new();

        public BuiltInTagService(
            ITagRegistrationManager registrationManager,
            IBackplane backplane)
        {
            _registrationManager = registrationManager ?? throw new ArgumentNullException(nameof(registrationManager));
            _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
        }

        public void SetTag(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tag path cannot be null or empty.", nameof(path));

            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
            {
                Console.WriteLine($"[DEBUG] Tag '{path}' not found in registration manager. Producers: {string.Join(",", _producers)}, Consumers: {string.Join(",", _consumers)}");
                throw new InvalidOperationException($"Tag '{path}' not registered.");
            }

            // 委托给 Backplane 发布（Backplane 负责存储和推送）。
            // Delegate to Backplane for publishing (Backplane handles storage and pushing).
            _ = _backplane.PublishAsync(identity.Handle, value, CancellationToken.None);
        }

        public T GetTag<T>(string path)
        {
            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
                return default;

            var value = _backplane.GetLastValueAsync(identity.Handle, CancellationToken.None).Result;
            if (value == null)
                return default;

            if (value is T typed)
                return typed;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                // 类型不匹配或转换失败时返回 default，避免抛 Null->ValueType 转换异常
                return default;
            }
        }

        public bool TryGetTag<T>(string path, out T value)
        {
            value = default;
            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
                return false;

            var val = _backplane.GetLastValueAsync(identity.Handle, CancellationToken.None).Result;
            if (val == null)
                return false;

            if (val is T typed)
            {
                value = typed;
                return true;
            }

            try
            {
                value = (T)Convert.ChangeType(val, typeof(T));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public object GetTag(string path)
        {
            var identity = _registrationManager.GetByPath(path);
            return identity != null ? _backplane.GetLastValueAsync(identity.Handle, CancellationToken.None).Result : null;
        }

        public void DeclareProducer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tag path cannot be null or empty.", nameof(path));

            _producers.Add(path);
               Console.WriteLine($"[DEBUG] Declared producer: {path}");
           }

        public void DeclareConsumer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tag path cannot be null or empty.", nameof(path));

            _consumers.Add(path);

            // 自动订阅（单进程用回调；跨进程时改为消息监听）。
            // Auto-subscribe (callback for single-process; message listening for cross-process).
            var identity = _registrationManager.GetByPath(path);
            if (identity != null)
            {
                _subscriptions[identity.Handle] = _backplane.Subscribe(identity.Handle, value =>
                {
                    // 触发订阅者回调。
                    // Trigger subscriber callbacks.
                    if (_subscribers.TryGetValue(path, out var callbacks))
                    {
                        foreach (var callback in callbacks)
                        {
                            try
                            {
                                callback(value);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WARN] Tag subscriber callback failed for {path}: {ex.Message}");
                            }
                        }
                    }
                });
            }
        }

        public IDisposable SubscribeTag(string path, Action<object> onUpdate)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tag path cannot be null or empty.", nameof(path));
            if (onUpdate == null)
                throw new ArgumentNullException(nameof(onUpdate));

            var callbacks = _subscribers.GetOrAdd(path, _ => new List<Action<object>>());
            lock (callbacks) { callbacks.Add(onUpdate); }

            var identity = _registrationManager.GetByPath(path);
            if (identity != null)
            {
                // ensure one backend subscription per handle
                _backplaneSubscriptions.GetOrAdd(identity.Handle, h =>
                    _backplane.Subscribe(h, value => NotifySubscribers(path, value))
                );
            }

            return new Subscription(path, onUpdate, () =>
            {
                lock (callbacks) { callbacks.Remove(onUpdate); }
                // if no local callbacks remain, cancel backend subscription
                if (callbacks.Count == 0 && identity != null)
                {
                    if (_backplaneSubscriptions.TryRemove(identity.Handle, out var sub))
                        sub.Dispose();
                }
            });
        }

        // 新增：当 Host 完成 RegisterModuleTags 后调用，便于在注册完成后建立订阅（解决注册时序问题）
        // Called by host after RegisterModuleTags so service can bind declared consumers/producers to handles.
        public void OnTagsRegistered(IEnumerable<TagIdentity> identities)
        {
            if (identities == null) return;

            foreach (var identity in identities)
            {
                if (identity == null) continue;

                // 如果此 path 是本进程声明的 consumer，则在 backplane 上订阅该 handle（如果尚未订阅）
                if (_consumers.Contains(identity.Path))
                {
                    if (!_subscriptions.ContainsKey(identity.Handle))
                    {
                        try
                        {
                            var sub = _backplane.Subscribe(identity.Handle, value =>
                            {
                                // 回调来自 backplane，转发到按路径注册的回调集合
                                NotifySubscribers(identity.Path, value);
                            });

                            _subscriptions[identity.Handle] = sub;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARN] Failed to subscribe backplane for {identity.Path} (handle={identity.Handle}): {ex.Message}");
                        }
                    }
                }

                // 如需对 Producer 做初始化（例如写入默认值），可在这里处理（目前不强制）
            }
        }

        // 内部：把 backplane 的回调转成按 path 的回调集合调用
        private void NotifySubscribers(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            if (_subscribers.TryGetValue(path, out var callbacks))
            {
                // 复制一份防止回调期间集合被修改
                var snapshot = callbacks.ToArray();
                foreach (var cb in snapshot)
                {
                    try
                    {
                        cb(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Tag subscriber callback failed for {path}: {ex.Message}");
                    }
                }
            }
        }

        // 内部订阅句柄类。
        // Internal subscription handle.
        private sealed class Subscription : IDisposable
        {
            private readonly string _path;
            private readonly Action<object> _callback;
            private readonly Action _unsubscribe;

            public Subscription(string path, Action<object> callback, Action unsubscribe)
            {
                _path = path;
                _callback = callback;
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                _unsubscribe();
            }
        }

        internal IReadOnlyCollection<string> GetDeclaredProducers() => _producers;
        internal IReadOnlyCollection<string> GetDeclaredConsumers() => _consumers;
    }
}
