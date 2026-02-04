using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 容器内置的 Tag 服务实现：委托给 Backplane 存储和推送。
    /// 增强：当 path 尚未注册时缓冲 SetTag 值（覆盖最新），在宿主注册完成（OnTagsRegistered）时下发；
    /// 增加异步 publish 错误处理、可选回退、后台重试与简单监控指标。
    /// </summary>
    public class BuiltInTagService : ITagService, IDisposable
    {
        private readonly HashSet<string> _producers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _consumers = new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, List<Action<object>>> _subscribers =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ITagRegistrationManager _registrationManager;
        private readonly IBackplane _backplane;

        private readonly ConcurrentDictionary<uint, SubscriptionInfo> _backplaneSubscriptions = new();
        private readonly ConcurrentDictionary<uint, HashSet<string>> _handlePaths = new();
        private readonly ConcurrentDictionary<uint, object> _handleLocks = new();

        private readonly ConcurrentDictionary<string, object> _pendingValues = new(StringComparer.OrdinalIgnoreCase);

        // configuration via env
        private readonly int _publishMaxRetries = int.TryParse(Environment.GetEnvironmentVariable("TIEMA_TAG_PUBLISH_RETRIES"), out var r) ? Math.Max(0, r) : 3;
        private readonly int _initialBackoffMs = int.TryParse(Environment.GetEnvironmentVariable("TIEMA_TAG_PUBLISH_BACKOFF_MS"), out var b) ? Math.Max(50, b) : 200;
        private readonly bool _notifyLocalOnPublish = string.Equals(Environment.GetEnvironmentVariable("TIEMA_TAG_NOTIFY_LOCAL_ON_PUBLISH"), "1", StringComparison.OrdinalIgnoreCase)
                                                       || string.Equals(Environment.GetEnvironmentVariable("TIEMA_TAG_NOTIFY_LOCAL_ON_PUBLISH"), "true", StringComparison.OrdinalIgnoreCase);

        // requeue on immediate publish failure (fire-and-forget helper can requeue)
        private readonly bool _requeueOnPublishFail = string.Equals(Environment.GetEnvironmentVariable("TIEMA_TAG_REQUEUE_ON_PUBLISH_FAIL"), "1", StringComparison.OrdinalIgnoreCase)
                                                       || string.Equals(Environment.GetEnvironmentVariable("TIEMA_TAG_REQUEUE_ON_PUBLISH_FAIL"), "true", StringComparison.OrdinalIgnoreCase);

        // pending limits and retry loop interval
        private readonly int _pendingMax = int.TryParse(Environment.GetEnvironmentVariable("TIEMA_PENDING_MAX"), out var pm) ? Math.Max(1, pm) : 1000;
        private readonly int _pendingRetryMs = int.TryParse(Environment.GetEnvironmentVariable("TIEMA_PENDING_RETRY_MS"), out var pr) ? Math.Max(1000, pr) : 5000;

        // metrics
        private long _publishFailures;

        // background retry
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pendingRetryTask;

        public BuiltInTagService(
            ITagRegistrationManager registrationManager,
            IBackplane backplane)
        {
            _registrationManager = registrationManager ?? throw new ArgumentNullException(nameof(registrationManager));
            _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));

            // start background retry loop
            _pendingRetryTask = Task.Run(() => PendingRetryLoopAsync(_cts.Token));
        }

        // Public metrics
        public int PendingCount => _pendingValues.Count;
        public long PublishFailures => Interlocked.Read(ref _publishFailures);

        public void SetTag(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
            if (value == null) throw new ArgumentNullException(nameof(value));

            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
            {
                // pending store with simple max limit
                if (_pendingValues.Count >= _pendingMax)
                {
                    Console.WriteLine($"[WARN] Pending values exceeded limit {_pendingMax}, dropping pending for '{path}'.");
                }
                else
                {
                    _pendingValues[path] = value;
                }

                // local visibility
                NotifySubscribers(path, value);
                return;
            }

            // have handle -> publish async and observe errors via helper
            FireAndHandlePublish(identity.Handle, value, path);
        }

        public T GetTag<T>(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));

            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
            {
                if (_pendingValues.TryGetValue(path, out var pending) && pending is T tPending) return tPending;
                return default!;
            }

            var v = _backplane.GetLastValueAsync(identity.Handle).Result;
            if (v == null) return default!;
            if (v is T tv) return tv;
            try { return (T)Convert.ChangeType(v, typeof(T)); }
            catch { return default!; }
        }

        public bool TryGetTag<T>(string path, out T value)
        {
            value = default!;
            if (string.IsNullOrWhiteSpace(path)) return false;

            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
            {
                if (_pendingValues.TryGetValue(path, out var pending))
                {
                    try
                    {
                        value = (T)Convert.ChangeType(pending, typeof(T));
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            }

            var v = _backplane.GetLastValueAsync(identity.Handle).Result;
            if (v == null) return false;
            try
            {
                value = (T)Convert.ChangeType(v, typeof(T));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public object GetTag(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));

            var identity = _registrationManager.GetByPath(path);
            if (identity == null)
            {
                _pendingValues.TryGetValue(path, out var pending);
                return pending!;
            }

            return _backplane.GetLastValueAsync(identity.Handle).Result!;
        }

        public void DeclareProducer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
            _producers.Add(path);
        }

        public void DeclareConsumer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
            _consumers.Add(path);

            var identity = _registrationManager.GetByPath(path);
            if (identity != null)
            {
                EnsureBackplaneSubscription(identity.Handle, path);
            }
        }

        public IDisposable SubscribeTag(string path, Action<object> onUpdate)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
            if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

            var list = _subscribers.GetOrAdd(path, _ => new List<Action<object>>());
            lock (list) list.Add(onUpdate);

            var identity = _registrationManager.GetByPath(path);
            if (identity != null) EnsureBackplaneSubscription(identity.Handle, path);

            if (_pendingValues.TryGetValue(path, out var pending))
            {
                try { onUpdate(pending); } catch { /* swallow */ }
            }

            return new Subscription(path, onUpdate, () =>
            {
                if (_subscribers.TryGetValue(path, out var cbs))
                {
                    lock (cbs) cbs.Remove(onUpdate);
                }

                var id = _registrationManager.GetByPath(path);
                if (id != null) ReleaseBackplaneSubscription(id.Handle, path);
            });
        }

        // Called by Host after RegisterModuleTags(...) completes.
        public void OnTagsRegistered(IEnumerable<TagIdentity> identities)
        {
            if (identities == null) return;

            foreach (var identity in identities)
            {
                if (identity == null) continue;

                if (_consumers.Contains(identity.Path))
                {
                    EnsureBackplaneSubscription(identity.Handle, identity.Path);
                }

                if (_pendingValues.TryRemove(identity.Path, out var pending))
                {
                    // Fire-and-forget, helper will handle errors and requeue if configured
                    FireAndHandlePublish(identity.Handle, pending, identity.Path);
                }
            }
        }

        // Fire-and-forget helper that observes publish outcome and optionally requeues
        private void FireAndHandlePublish(uint handle, object value, string path)
        {
            // start an async task to observe completion and failures
            _ = HandlePublishTaskAsync(_backplane.PublishAsync(handle, value), handle, value, path);
        }

        private async Task HandlePublishTaskAsync(Task publishTask, uint handle, object value, string path)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await publishTask.ConfigureAwait(false);
                sw.Stop();
                Console.WriteLine($"[INFO] Published tag '{path}' to handle {handle} (latency {sw.ElapsedMilliseconds}ms).");
                if (_notifyLocalOnPublish)
                {
                    try { NotifySubscribers(path, value); } catch (Exception ex) { Console.WriteLine($"[WARN] NotifySubscribers after publish failed: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _publishFailures);
                Console.WriteLine($"[WARN] Publish canceled for '{path}' handle={handle}.");
                if (_requeueOnPublishFail)
                {
                    _pendingValues[path] = value;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _publishFailures);
                Console.WriteLine($"[WARN] Publish failed for '{path}' handle={handle}: {ex.GetBaseException().Message}");
                if (_requeueOnPublishFail)
                {
                    _pendingValues[path] = value;
                    Console.WriteLine($"[INFO] Pending value for '{path}' requeued after publish failure.");
                }
            }
        }

        // Background loop: periodically attempt to publish pending values if registration exists
        private async Task PendingRetryLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var keys = _pendingValues.Keys.ToArray();
                    foreach (var path in keys)
                    {
                        if (ct.IsCancellationRequested) break;

                        if (!_pendingValues.TryGetValue(path, out var pending)) continue;

                        var identity = _registrationManager.GetByPath(path);
                        if (identity == null) continue;

                        // attempt fire-and-handle publish and remove pending (the handler will requeue on failure if configured)
                        if (_pendingValues.TryRemove(path, out var removedValue))
                        {
                            FireAndHandlePublish(identity.Handle, removedValue, path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] PendingRetryLoop error: {ex.Message}");
                }

                try { await Task.Delay(_pendingRetryMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void NotifySubscribers(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!_subscribers.TryGetValue(path, out var list)) return;
            Action<object>[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (var cb in snapshot)
            {
                try { cb(value); } catch { /* ignore subscriber exceptions */ }
            }
        }

        private void NotifySubscribersForHandle(uint handle, object value)
        {
            if (TagValueHelper.TryUnpack(value, out var unpacked))
            {
                value = unpacked;
            }

            if (_handlePaths.TryGetValue(handle, out var paths))
            {
                string[] snapshot;
                lock (paths) snapshot = paths.ToArray();
                foreach (var path in snapshot)
                {
                    try { NotifySubscribers(path, value); } catch { /* ignore */ }
                }
            }
        }

        private void EnsureBackplaneSubscription(uint handle, string path)
        {
            var lockObj = _handleLocks.GetOrAdd(handle, _ => new object());
            lock (lockObj)
            {
                var paths = _handlePaths.GetOrAdd(handle, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                if (_backplaneSubscriptions.TryGetValue(handle, out var existing))
                {
                    if (paths.Add(path)) existing.AddRef();
                }
                else
                {
                    paths.Add(path);
                    var si = new SubscriptionInfo(handle, _backplane, v => NotifySubscribersForHandle(handle, v));
                    _backplaneSubscriptions[handle] = si;
                }
            }
        }

        private void ReleaseBackplaneSubscription(uint handle, string path)
        {
            var lockObj = _handleLocks.GetOrAdd(handle, _ => new object());
            lock (lockObj)
            {
                if (!_handlePaths.TryGetValue(handle, out var paths)) return;
                bool removed;
                lock (paths) removed = paths.Remove(path);
                if (removed && _backplaneSubscriptions.TryGetValue(handle, out var info))
                {
                    var remaining = info.Release();
                    if (remaining <= 0)
                    {
                        if (_backplaneSubscriptions.TryRemove(handle, out var removedInfo) && removedInfo == info)
                        {
                            removedInfo.Dispose();
                        }
                        _handlePaths.TryRemove(handle, out _);
                        _handleLocks.TryRemove(handle, out _);
                    }
                }
            }
        }

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

            public void Dispose() => _unsubscribe();
        }

        private sealed class SubscriptionInfo : IDisposable
        {
            private readonly object _lock = new();
            private int _refCount;
            private IDisposable? _subscription;
            private readonly uint _handle;
            private readonly IBackplane _backplane;
            private readonly Action<object> _onUpdate;

            public SubscriptionInfo(uint handle, IBackplane backplane, Action<object> onUpdate)
            {
                _handle = handle;
                _backplane = backplane;
                _onUpdate = onUpdate ?? throw new ArgumentNullException(nameof(onUpdate));
                _refCount = 1;
                try
                {
                    _subscription = _backplane.Subscribe(_handle, _onUpdate);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to create backplane subscription for handle={_handle}: {ex.Message}");
                    _subscription = null;
                }
            }

            public void AddRef()
            {
                lock (_lock) { _refCount++; }
            }

            public int Release()
            {
                lock (_lock) { _refCount--; return _refCount; }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    try { _subscription?.Dispose(); } catch { }
                    _subscription = null;
                    _refCount = 0;
                }
            }
        }

        internal IReadOnlyCollection<string> GetDeclaredProducers() => _producers;
        internal IReadOnlyCollection<string> GetDeclaredConsumers() => _consumers;

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
                _pendingRetryTask?.Wait(500);
            }
            catch { }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
