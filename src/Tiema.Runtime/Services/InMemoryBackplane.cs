using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 内存 Backplane 实现：维护全局 Tag 镜像，支持发布/订阅。
    /// In-memory backplane implementation: maintains global tag mirror, supports publish/subscribe.
    /// </summary>
    public class InMemoryBackplane : IBackplane
    {
        private readonly ConcurrentDictionary<uint, object?> _tagMirror = new();
        private readonly ConcurrentDictionary<uint, List<Action<object>>> _subscribers = new();

        public Task PublishAsync(uint handle, object value, CancellationToken ct = default)
        {
            // 更新镜像。
            // Update mirror.
            _tagMirror[handle] = value;

            // 推送给订阅者。
            // Push to subscribers.
            if (_subscribers.TryGetValue(handle, out var callbacks))
            {
                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Backplane subscriber callback failed for handle {handle}: {ex.Message}");
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<object?> GetLastValueAsync(uint handle, CancellationToken ct = default)
        {
            _tagMirror.TryGetValue(handle, out var value);
            return Task.FromResult(value);
        }

        public IDisposable Subscribe(uint handle, Action<object> onUpdate)
        {
            if (onUpdate == null)
                throw new ArgumentNullException(nameof(onUpdate));

            var callbacks = _subscribers.GetOrAdd(handle, _ => new List<Action<object>>());
            callbacks.Add(onUpdate);
            return new Subscription(handle, onUpdate, () => callbacks.Remove(onUpdate));
        }

        private sealed class Subscription : IDisposable
        {
            private readonly uint _handle;
            private readonly Action<object> _callback;
            private readonly Action _unsubscribe;

            public Subscription(uint handle, Action<object> callback, Action unsubscribe)
            {
                _handle = handle;
                _callback = callback;
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                _unsubscribe();
            }
        }
    }
}