using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// Grpc Backplane Adapter（实现）：
    /// - 构建在传输层（GrpcBackplaneTransport / IBackplane）之上；
    /// - 管理 RegisterTags、assembly（批量）映射、RPI 周期调度以及 TagBatch 的打包/回填逻辑。
    /// 
    /// 说明：Adapter 生成轻量 DTO（TagBatchDto），通过事件暴露给上层的 gRPC sender，
    /// 上层负责把 DTO 序列化为 protobuf 并发送到远端；远端收到 TagBatch 后同样可由上层反序列化
    /// 为 TagBatchDto 并调用 Adapter.OnTagBatchReceived 做原样回填。
    /// </summary>
    public sealed class TiemaDataConnectAdapter : IDisposable
    {
        private readonly IBackplane _transport;
        private readonly ITagRegistrationManager _registrationManager;
        private readonly ConcurrentDictionary<string, IoAssembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public TiemaDataConnectAdapter(IBackplane transport, ITagRegistrationManager registrationManager)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _registrationManager = registrationManager ?? throw new ArgumentNullException(nameof(registrationManager));
        }

        /// <summary>
        /// 当 adapter 产生一个待发送的 TagBatch 时触发（外部应订阅此事件并负责把 DTO 序列化/发送到远端）。
        /// </summary>
        public event Action<TagBatchDto>? BatchReady;

        /// <summary>
        /// 注册插件提供的 Tags（会调用 RegistrationManager 并返回分配结果）。
        /// 这里作为示例方法，实际可能通过 RegistrationManager 或 RPC 与远端协调。
        /// </summary>
        public IEnumerable<TagIdentity> RegisterTags(string pluginInstanceId, IEnumerable<string> producerPaths, IEnumerable<string> consumerPaths)
        {
            var identities = _registrationManager.RegisterModuleTags(pluginInstanceId, producerPaths, consumerPaths);
            // TODO: 根据 identities 中的 assemblyId（若有）把 handle 绑定到 assembly 并启动
            return identities;
        }

        /// <summary>
        /// 启动（或创建）一个 assembly（简化示例）。
        /// assemblyId 唯一标识，handles 为成员句柄集合，rpiMs 为请求周期（ms）。
        /// </summary>
        public void StartAssembly(string assemblyId, IEnumerable<uint> handles, int rpiMs)
        {
            if (string.IsNullOrWhiteSpace(assemblyId)) throw new ArgumentException(nameof(assemblyId));
            var asm = _assemblies.GetOrAdd(assemblyId, id => new IoAssembly(id, _transport, rpiMs, OnBatchProduced));
            asm.UpdateMembers(handles);
            asm.Start();
        }

        /// <summary>
        /// 停止并移除 assembly。
        /// </summary>
        public void StopAssembly(string assemblyId)
        {
            if (_assemblies.TryRemove(assemblyId, out var asm))
            {
                try { asm.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 处理从远端接收到的 TagBatch（Adapter 接收后按 handle 原样回填到 Backplane）。
        /// 上层负责把网络/transport 的 protobuf 反序列化为 TagBatchDto 并调用此方法。
        /// </summary>
        public void OnTagBatchReceived(TagBatchDto batch)
        {
            if (batch == null) return;

            foreach (var tv in batch.Tags)
            {
                try
                {
                    // 原样回填到本地背板；value 是 object（可能为 CLR 值或字节数组/字符串），transport 决定如何传输
                    _ = _transport.PublishAsync(tv.Handle, tv.Value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to publish tag handle={tv.Handle} from batch: {ex.Message}");
                }
            }
        }

        private void OnBatchProduced(TagBatchDto batch)
        {
            try
            {
                BatchReady?.Invoke(batch);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] BatchReady handler threw: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _assemblies)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _assemblies.Clear();
        }

        // -----------------------
        // DTO 定义（轻量）：
        // -----------------------
        public sealed class TagValueDto
        {
            public uint Handle { get; set; }
            public string? Name { get; set; }
            public long TimestampUnixMs { get; set; }
            public object? Value { get; set; } // 保留原始值（CLR 值、byte[]、string 等）
            public int Quality { get; set; }
            public string? SourcePluginId { get; set; }
            public string? ReferencePluginInstanceId { get; set; }
        }

        public sealed class TagBatchDto
        {
            public string? PluginInstanceId { get; set; }
            public List<TagValueDto> Tags { get; } = new();
        }

        // -----------------------
        // Assembly 实现
        // -----------------------
        private sealed class IoAssembly : IDisposable
        {
            private readonly string _assemblyId;
            private readonly IBackplane _transport;
            private readonly int _rpiMs;
            private readonly HashSet<uint> _members = new();
            private Timer? _timer;
            private bool _running;
            private int _inFlight; // 0/1 用于避免重入
            private readonly Action<TagBatchDto> _onBatchProduced;

            public IoAssembly(string assemblyId, IBackplane transport, int rpiMs, Action<TagBatchDto> onBatchProduced)
            {
                _assemblyId = assemblyId;
                _transport = transport;
                _rpiMs = rpiMs > 0 ? rpiMs : 100;
                _onBatchProduced = onBatchProduced;
            }

            public void UpdateMembers(IEnumerable<uint> handles)
            {
                lock (_members)
                {
                    _members.Clear();
                    foreach (var h in handles) _members.Add(h);
                }
            }

            public void Start()
            {
                if (_running) return;
                // timer callback 要尽量短：启动后台任务执行周期逻辑
                _timer = new Timer(_ => Task.Run(() => CycleAsync()), null, 0, _rpiMs);
                _running = true;
            }

            private async Task CycleAsync()
            {
                // 避免上一周期尚未完成时再次进入
                if (Interlocked.Exchange(ref _inFlight, 1) == 1) return;
                try
                {
                    List<uint> snapshot;
                    lock (_members)
                    {
                        snapshot = new List<uint>(_members);
                    }

                    if (snapshot.Count == 0) return;

                    var batch = new TagBatchDto
                    {
                        PluginInstanceId = null
                    };

                    var tasks = new List<Task>();
                    foreach (var handle in snapshot)
                    {
                        // 并发获取每个 handle 的最新值
                        var t = _transport.GetLastValueAsync(handle)
                            .ContinueWith(task =>
                            {
                                object? val = null;
                                try
                                {
                                    if (task.Status == TaskStatus.RanToCompletion)
                                        val = task.Result;
                                }
                                catch { }

                                var tv = new TagValueDto
                                {
                                    Handle = handle,
                                    Name = null,
                                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    Value = val,
                                    Quality = 0,
                                    SourcePluginId = null,
                                    ReferencePluginInstanceId = null
                                };

                                lock (batch.Tags)
                                {
                                    batch.Tags.Add(tv);
                                }
                            }, TaskScheduler.Default);

                        tasks.Add(t);
                    }

                    // 等待收集完成（短超时策略可在此处扩展）
                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    // 触发上层发送事件（上层负责把 DTO 序列化为 protobuf 并发送到远端）
                    try
                    {
                        _onBatchProduced?.Invoke(batch);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] IoAssembly.OnBatchProduced error: {ex.Message}");
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _inFlight, 0);
                }
            }

            public void Dispose()
            {
                try { _timer?.Dispose(); } catch { }
                _timer = null;
                _members.Clear();
                _running = false;
            }
        }
    }
}