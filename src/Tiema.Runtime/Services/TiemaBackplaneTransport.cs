using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;
using Tiema.Hosting.Abstractions;
using Tiema.Protocols.V1;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// gRPC Backplane 传输实现（Transport）：
    /// - 同时实现 IBackplane（本地 CLR 语义）与 ITagTransport（protobuf/网络语义）;
    /// - 传输层负责 RPC/序列化/流式订阅；高阶 Adapter 在此之上实现 assembly/周期逻辑。
    /// </summary>
    public class TiemaBackplaneTransport : IBackplane, ITagTransport, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly Backplane.BackplaneClient _client;

        // 本地值回调复用（原实现）：handle -> SubscriptionGroup（返回 CLR object）
        private readonly ConcurrentDictionary<uint, SubscriptionGroup> _subscriptions = new();

        // 原样 protobuf Update 回调（用于 ITagTransport.SubscribeTag/Batch）：handle -> RawSubscriptionGroup
        private readonly ConcurrentDictionary<uint, RawSubscriptionGroup> _rawSubscriptions = new();

        private bool _disposed;

        public TiemaBackplaneTransport(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            _channel = GrpcChannel.ForAddress(url);
            _client = new Backplane.BackplaneClient(_channel);
        }

        // -----------------------
        // IBackplane 实现（保留原有语义）
        // -----------------------
        // 替换 IBackplane.PublishAsync 的实现，构造 TagValue
        public async Task PublishAsync(uint handle, object value, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            // 构造 TagValue：使用 proto 新字段名（Timestamp, SourceModuleInstanceId）
            var tag = new TagValue
            {
                Handle = handle,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Quality = QualityCode.QualityUnknown,
                SourceModuleInstanceId = string.Empty
            };

            switch (value)
            {
                case bool b:
                    tag.BoolValue = b;
                    break;
                case int i:
                    tag.IntValue = i;
                    break;
                case long l:
                    tag.IntValue = l;
                    break;
                case double d:
                    tag.DoubleValue = d;
                    break;
                case string s:
                    tag.StringValue = s;
                    break;
                case byte[] bytes:
                    tag.BytesValue = Google.Protobuf.ByteString.CopyFrom(bytes);
                    break;
                case TagValue tv:
                    tag = tv;
                    break;
                default:
                    // fallback to string
                    tag.StringValue = value?.ToString() ?? string.Empty;
                    break;
            }

            var req = new PublishRequest { Tag = tag };

            try
            {
                var resp = await _client.PublishAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                if (resp == null || !resp.Success)
                {
                    Console.WriteLine($"[WARN] Publish failed for handle={handle}: {resp?.Message}");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled || ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneTransport.PublishAsync exception: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// IBackplane.GetLastValueAsync：返回原样的 protobuf TagValue（object），不在传输层做强制解包。
        /// 这样上层或 adapter 可以按需用 TagValueHelper/TryUnpack 做解包/转换。
        /// </summary>
        public async Task<object?> GetLastValueAsync(uint handle, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var req = new GetRequest { Handle = handle };
            try
            {
                var resp = await _client.GetLastValueAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                if (resp == null || !resp.Found) return null;

                // 返回 protobuf TagValue 原始对象（上层决定是否解包）
                return resp.Value;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneTransport.GetLastValueAsync exception: {ex.Message}");
                throw;
            }
        }

        public IDisposable Subscribe(uint handle, Action<object> onUpdate)
        {
            EnsureNotDisposed();

            if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

            var group = _subscriptions.GetOrAdd(handle, h =>
            {
                var g = new SubscriptionGroup(this, h);
                g.Start();
                return g;
            });

            return group.AddCallback(onUpdate);
        }

        // -----------------------
        // ITagTransport 实现（protobuf / 网络语义）
        // -----------------------
        public async Task<RegisterTagsResponse> RegisterTagsAsync(RegisterTagsRequest request, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (request == null)
            {
                return new RegisterTagsResponse
                {
                    Success = false,
                    Message = "null request"
                };
            }

            try
            {
                // 直接透传 protobuf 的 RegisterTagsRequest 到远端 Backplane RPC（保持原样，避免不必要的映射）
                var rpcCall = _client.RegisterTagsAsync(request, cancellationToken: ct);
                var resp = await rpcCall.ResponseAsync.ConfigureAwait(false);

                if (resp == null)
                {
                    return new RegisterTagsResponse
                    {
                        Success = false,
                        Message = "null response from server"
                    };
                }

                return resp;
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled || rex.StatusCode == StatusCode.DeadlineExceeded)
            {
                // 上层可能需要感知取消/超时，继续抛出以便 caller 处理
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneTransport.RegisterTagsAsync exception: {ex.Message}");
                return new RegisterTagsResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        // ITagTransport.PublishTagAsync：直接发送 TagValue
        public async Task<PublishResponse> PublishTagAsync(TagValue tag, CancellationToken ct = default)
        {
            EnsureNotDisposed();
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            try
            {
                var req = new PublishRequest { Tag = tag };
                var resp = await _client.PublishAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                return resp ?? new PublishResponse { Success = false, Message = "null response" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PublishTagAsync exception: {ex.Message}");
                return new PublishResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<PublishResponse> PublishBatchAsync(TagBatch batch, CancellationToken ct = default)
        {
            EnsureNotDisposed();
            if (batch == null) throw new ArgumentNullException(nameof(batch));

            // Simple implementation: publish each TagValue individually in parallel
            var tasks = new List<Task<PublishResponse>>();
            foreach (var tv in batch.Tags)
            {
                tasks.Add(PublishTagAsync(tv, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Aggregate result: success if all Success
            var failed = tasks.Select(t => t.Result).FirstOrDefault(r => r == null || !r.Success);
            return failed ?? new PublishResponse { Success = true, Message = string.Empty };
        }

        // 显式接口实现，避免与 IBackplane 的同名冲突：直接返回 protobuf GetResponse
        async Task<GetResponse> ITagTransport.GetLastValueAsync(uint handle, CancellationToken ct)
        {
            EnsureNotDisposed();

            var req = new GetRequest { Handle = handle };
            try
            {
                var resp = await _client.GetLastValueAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                return resp ?? new GetResponse { Found = false };
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return new GetResponse { Found = false };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneTransport.GetLastValueAsync (protobuf) exception: {ex.Message}");
                throw;
            }
        }

        public IDisposable SubscribeTag(uint handle, Action<Update> onUpdate)
        {
            EnsureNotDisposed();
            if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

            var group = _rawSubscriptions.GetOrAdd(handle, h =>
            {
                var g = new RawSubscriptionGroup(this, h);
                g.Start();
                return g;
            });

            return group.AddCallback(onUpdate);
        }

        public IDisposable SubscribeBatch(IEnumerable<RegisterTagInfo> tags, uint rpiMs, Action<Update> onUpdate)
        {
            EnsureNotDisposed();
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

            // Build a SubscribeRequest if server supports batch subscribe (falls back to per-handle subscribe)
            var enumerated = tags.ToList();
            if (enumerated.Count == 1)
            {
                return SubscribeTag(enumerated[0].Handle, onUpdate);
            }

            // For simplicity, create a group per first handle and forward all updates
            var first = enumerated[0];
            return SubscribeTag(first.Handle, onUpdate);
        }

        // -----------------------
        // Helpers & conversion
        // -----------------------
        private Any? ConvertToAny(object? value)
        {
            if (value == null) return null;

            switch (value)
            {
                case IMessage msg:
                    return Any.Pack(msg);
                case int i:
                    return Any.Pack(new Int32Value { Value = i });
                case long l:
                    return Any.Pack(new Int64Value { Value = l });
                case double d:
                    return Any.Pack(new DoubleValue { Value = d });
                case float f:
                    return Any.Pack(new DoubleValue { Value = f });
                case bool b:
                    return Any.Pack(new BoolValue { Value = b });
                case string s:
                    return Any.Pack(new StringValue { Value = s });
                default:
                    return Any.Pack(new StringValue { Value = value.ToString() ?? string.Empty });
            }
        }

        private object? ConvertFromAny(Any? any)
        {
            if (any == null) return null;

            try
            {
                if (any.Is(Int32Value.Descriptor))
                {
                    var v = any.Unpack<Int32Value>();
                    return v.Value;
                }
                if (any.Is(Int64Value.Descriptor))
                {
                    var v = any.Unpack<Int64Value>();
                    return v.Value;
                }
                if (any.Is(DoubleValue.Descriptor))
                {
                    var v = any.Unpack<DoubleValue>();
                    return v.Value;
                }
                if (any.Is(BoolValue.Descriptor))
                {
                    var v = any.Unpack<BoolValue>();
                    return v.Value;
                }
                if (any.Is(StringValue.Descriptor))
                {
                    var v = any.Unpack<StringValue>();
                    return v.Value;
                }
                if (any.Is(Struct.Descriptor))
                {
                    var v = any.Unpack<Struct>();
                    return v;
                }
                if (any.Is(TagValue.Descriptor))
                {
                    // if transport packed a TagValue, return it as TagValue instance
                    var tv = any.Unpack<TagValue>();
                    return tv;
                }

                // Unknown: return Any for caller to handle
                return any;
            }
            catch
            {
                // best effort: return raw Any
                return any;
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TiemaBackplaneTransport));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _subscriptions)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _subscriptions.Clear();

            foreach (var kv in _rawSubscriptions)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _rawSubscriptions.Clear();

            try
            {
                _channel?.Dispose();
            }
            catch { }
        }

        // -------- SubscriptionGroup: per-handle 本地值单流 + 本地 fan-out --------
        private sealed class SubscriptionGroup : IDisposable
        {
            private readonly TiemaBackplaneTransport _parent;
            private readonly uint _handle;
            private readonly ConcurrentDictionary<Guid, Action<object>> _callbacks = new();
            private CancellationTokenSource _cts = new();
            private Task? _readLoop;
            private AsyncServerStreamingCall<Update>? _call;
            private readonly object _startLock = new();

            public SubscriptionGroup(TiemaBackplaneTransport parent, uint handle)
            {
                _parent = parent;
                _handle = handle;
            }

            public IDisposable AddCallback(Action<object> callback)
            {
                var id = Guid.NewGuid();
                _callbacks.TryAdd(id, callback);
                return new CallbackSubscription(this, id);
            }

            public void Start()
            {
                lock (_startLock)
                {
                    if (_readLoop != null) return;

                    _readLoop = Task.Run(async () =>
                    {
                        try
                        {
                            var req = new SubscribeRequest { Handle = _handle, SubscriberId = Guid.NewGuid().ToString("N") };
                            _call = _parent._client.Subscribe(req, cancellationToken: _cts.Token);
                            var stream = _call.ResponseStream;
                            // 处理 Update.Tag 或 Update.Batch
                            while (await stream.MoveNext(_cts.Token).ConfigureAwait(false))
                            {
                                var upd = stream.Current;
                                object val = null;
                                switch (upd.PayloadCase)
                                {
                                    case Update.PayloadOneofCase.Tag:
                                        val = upd.Tag; // TagValue instance
                                        break;
                                    case Update.PayloadOneofCase.Batch:
                                        val = upd.Batch; // TagBatch instance
                                        break;
                                    default:
                                        val = null;
                                        break;
                                }

                                // snapshot callbacks
                                var cbs = _callbacks.Values.ToArray();
                                foreach (var cb in cbs)
                                {
                                    try
                                    {
                                        cb(val);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[WARN] subscriber callback error: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { /* expected on dispose */ }
                        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) { /* expected */ }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] SubscriptionGroup read loop error for handle={_handle}: {ex.Message}");
                        }
                    }, _cts.Token);
                }
            }

            internal void RemoveCallback(Guid id)
            {
                _callbacks.TryRemove(id, out _);
                if (_callbacks.IsEmpty)
                {
                    // no local consumers left: stop stream, remove from parent and dispose
                    Dispose();
                    _parent._subscriptions.TryRemove(_handle, out _);
                }
            }

            public void Dispose()
            {
                try
                {
                    _cts.Cancel();
                }
                catch { }

                try
                {
                    _call?.Dispose();
                }
                catch { }

                try
                {
                    _readLoop?.Wait(1000);
                }
                catch { }

                try
                {
                    _cts.Dispose();
                }
                catch { }

                // clear callbacks
                _callbacks.Clear();
            }

            // small disposable returned to caller for per-callback unsubscribe
            private sealed class CallbackSubscription : IDisposable
            {
                private readonly SubscriptionGroup _group;
                private readonly Guid _id;
                private int _disposed;

                public CallbackSubscription(SubscriptionGroup group, Guid id)
                {
                    _group = group;
                    _id = id;
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    {
                        _group.RemoveCallback(_id);
                    }
                }
            }
        }

        // -------- RawSubscriptionGroup: per-handle protobuf Update 流的本地 fan-out（传递 Update 原始对象） --------
        private sealed class RawSubscriptionGroup : IDisposable
        {
            private readonly TiemaBackplaneTransport _parent;
            private readonly uint _handle;
            private readonly ConcurrentDictionary<Guid, Action<Update>> _callbacks = new();
            private CancellationTokenSource _cts = new();
            private Task? _readLoop;
            private AsyncServerStreamingCall<Update>? _call;
            private readonly object _startLock = new();

            public RawSubscriptionGroup(TiemaBackplaneTransport parent, uint handle)
            {
                _parent = parent;
                _handle = handle;
            }

            public IDisposable AddCallback(Action<Update> callback)
            {
                var id = Guid.NewGuid();
                _callbacks.TryAdd(id, callback);
                return new CallbackSubscription(this, id);
            }

            public void Start()
            {
                lock (_startLock)
                {
                    if (_readLoop != null) return;

                    _readLoop = Task.Run(async () =>
                    {
                        try
                        {
                            var req = new SubscribeRequest { Handle = _handle, SubscriberId = Guid.NewGuid().ToString("N") };
                            _call = _parent._client.Subscribe(req, cancellationToken: _cts.Token);
                            var stream = _call.ResponseStream;
                            while (await stream.MoveNext(_cts.Token).ConfigureAwait(false))
                            {
                                var upd = stream.Current; // protobuf Update
                                // snapshot callbacks
                                var cbs = _callbacks.Values.ToArray();
                                foreach (var cb in cbs)
                                {
                                    try
                                    {
                                        cb(upd);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[WARN] raw subscriber callback error: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { /* expected on dispose */ }
                        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) { /* expected */ }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR] RawSubscriptionGroup read loop error for handle={_handle}: {ex.Message}");
                        }
                    }, _cts.Token);
                }
            }

            internal void RemoveCallback(Guid id)
            {
                _callbacks.TryRemove(id, out _);
                if (_callbacks.IsEmpty)
                {
                    Dispose();
                    _parent._rawSubscriptions.TryRemove(_handle, out _);
                }
            }

            public void Dispose()
            {
                try { _cts.Cancel(); } catch { }

                try { _call?.Dispose(); } catch { }

                try { _readLoop?.Wait(1000); } catch { }

                try { _cts.Dispose(); } catch { }

                _callbacks.Clear();
            }

            private sealed class CallbackSubscription : IDisposable
            {
                private readonly RawSubscriptionGroup _group;
                private readonly Guid _id;
                private int _disposed;

                public CallbackSubscription(RawSubscriptionGroup group, Guid id)
                {
                    _group = group;
                    _id = id;
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    {
                        _group.RemoveCallback(_id);
                    }
                }
            }
        }
    }
}