using System;
using System.Collections.Concurrent;
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
    /// 真正的 gRPC Backplane 客户端实现（实现 IBackplane）。
    /// 支持基本类型与 protobuf IMessage 的序列化（Int32/Int64/Double/Bool/String/Struct/Any）。
    /// 订阅使用 server-stream 背景读取并回调 onUpdate。
    /// </summary>
    public class GrpcBackplaneClient : IBackplane, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly Backplane.BackplaneClient _client;
        private readonly ConcurrentDictionary<uint, Subscription> _subscriptions = new();
        private bool _disposed;

        public GrpcBackplaneClient(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            _channel = GrpcChannel.ForAddress(url);
            _client = new Backplane.BackplaneClient(_channel);
        }

        public async Task PublishAsync(uint handle, object value, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var any = ConvertToAny(value);
            var req = new PublishRequest
            {
                Handle = handle,
                Value = any ?? new Any(),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime()),
                Quality = 0,
                SourceModuleId = string.Empty
            };

            try
            {
                var resp = await _client.PublishAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                if (resp == null || !resp.Ok)
                {
                    Console.WriteLine($"[WARN] Publish failed for handle={handle}: {resp?.Error}");
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled || ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneClient.PublishAsync exception: {ex.Message}");
                throw;
            }
        }

        public async Task<object?> GetLastValueAsync(uint handle, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var req = new GetRequest { Handle = handle };
            try
            {
                var resp = await _client.GetLastValueAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                if (resp == null || !resp.Found) return null;
                return ConvertFromAny(resp.Value);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] GrpcBackplaneClient.GetLastValueAsync exception: {ex.Message}");
                throw;
            }
        }

        public IDisposable Subscribe(uint handle, Action<object> onUpdate)
        {
            EnsureNotDisposed();

            if (onUpdate == null) throw new ArgumentNullException(nameof(onUpdate));

            var sub = new Subscription(this, handle, onUpdate);
            if (!_subscriptions.TryAdd(handle, sub))
            {
                // allow multiple subscribers for same handle: store separate keyed by generated id
                // but interface demanded IDisposable per subscribe; to keep simple, generate a unique handle-key
                // here we support only one subscription per handle in dictionary; allow multiple by using unique key if needed.
                // For now, create a separate subscription and return it without placing in dictionary.
                return new Subscription(this, handle, onUpdate);
            }

            sub.Start();
            return sub;
        }

        private Any? ConvertToAny(object? value)
        {
            if (value == null) return null;

            switch (value)
            {
                case IMessage msg:
                    var any = Any.Pack(msg);
                    return any;
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
                //case Struct st:
                //    return Any.Pack(st);


                default:
                    // fallback: put string representation into StringValue
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
            if (_disposed) throw new ObjectDisposedException(nameof(GrpcBackplaneClient));
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

            try
            {
                _channel?.Dispose();
            }
            catch { }
        }

        // Internal subscription helper
        private sealed class Subscription : IDisposable
        {
            private readonly GrpcBackplaneClient _parent;
            private readonly uint _handle;
            private readonly Action<object> _callback;
            private readonly CancellationTokenSource _cts = new();
            private Task? _readLoop;
            private AsyncServerStreamingCall<Update>? _call;

            public Subscription(GrpcBackplaneClient parent, uint handle, Action<object> callback)
            {
                _parent = parent;
                _handle = handle;
                _callback = callback;
            }

            public void Start()
            {
                _readLoop = Task.Run(async () =>
                {
                    try
                    {
                        var req = new SubscribeRequest { Handle = _handle, SubscriberId = Guid.NewGuid().ToString("N") };
                        _call = _parent._client.Subscribe(req, cancellationToken: _cts.Token);
                        var stream = _call.ResponseStream;
                        while (await stream.MoveNext(_cts.Token).ConfigureAwait(false))
                        {
                            var upd = stream.Current;
                            var val = _parent.ConvertFromAny(upd.Value);
                            try
                            {
                                _callback(val);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WARN] subscriber callback error: {ex.Message}");
                            }
                        }
                    }
                    catch (OperationCanceledException) { /* expected on dispose */ }
                    catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) { /* expected */ }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Subscription read loop error for handle={_handle}: {ex.Message}");
                    }
                }, _cts.Token);
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

                _cts.Dispose();
            }
        }
    }
}