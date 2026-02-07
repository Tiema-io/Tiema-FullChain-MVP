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
using Tiema.Tags.Grpc.V1;       // tagsystem.proto -> Tiema.Tags.Grpc.V1
using Tiema.Connect.Grpc.V1;
using static Tiema.Connect.Grpc.V1.DataConnect;    // connect.proto -> Tiema.Connect.Grpc.V1

namespace Tiema.DataConnect.Core
{
    /// <summary>
    /// gRPC DataConnect transport: implements IBackplane (CLR) and ITagTransport (protobuf).
    /// </summary>
    public class TiemaDataConnectTransport : IBackplane, ITagTransport, IDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly DataConnectClient _client;

        private readonly ConcurrentDictionary<uint, SubscriptionGroup> _subscriptions = new();
        private readonly ConcurrentDictionary<uint, RawSubscriptionGroup> _rawSubscriptions = new();

        private bool _disposed;

        public TiemaDataConnectTransport(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            _channel = GrpcChannel.ForAddress(url);
            _client = new DataConnectClient(_channel);
        }

        public async Task PublishAsync(uint handle, object value, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            var tag = new TagValue
            {
                Handle = handle,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Quality = QualityCode.QualityUnknown,
                SourcePluginInstanceId = string.Empty
            };

            switch (value)
            {
                case bool b:
                    tag.BoolValue = b; break;
                case int i:
                    tag.IntValue = i; break;
                case long l:
                    tag.IntValue = l; break;
                case double d:
                    tag.DoubleValue = d; break;
                case string s:
                    tag.StringValue = s; break;
                case byte[] bytes:
                    tag.BytesValue = ByteString.CopyFrom(bytes); break;
                case TagValue tv:
                    tag = tv; break;
                default:
                    tag.StringValue = value?.ToString() ?? string.Empty; break;
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
                Console.WriteLine($"[ERROR] DataConnectTransport.PublishAsync exception: {ex.Message}");
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
                return resp.Value;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] DataConnectTransport.GetLastValueAsync exception: {ex.Message}");
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

        public async Task<RegisterTagsResponse> RegisterTagsAsync(RegisterTagsRequest request, CancellationToken ct = default)
        {
            EnsureNotDisposed();

            if (request == null)
            {
                return new RegisterTagsResponse { Success = false, Message = "null request" };
            }

            try
            {
                var rpcCall = _client.RegisterTagsAsync(request, cancellationToken: ct);
                var resp = await rpcCall.ResponseAsync.ConfigureAwait(false);
                return resp ?? new RegisterTagsResponse { Success = false, Message = "null response from server" };
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled || rex.StatusCode == StatusCode.DeadlineExceeded)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] DataConnectTransport.RegisterTagsAsync exception: {ex.Message}");
                return new RegisterTagsResponse { Success = false, Message = ex.Message };
            }
        }

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

            var tasks = new List<Task<PublishResponse>>();
            foreach (var tv in batch.Tags)
            {
                tasks.Add(PublishTagAsync(tv, ct));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var failed = tasks.Select(t => t.Result).FirstOrDefault(r => r == null || !r.Success);
            return failed ?? new PublishResponse { Success = true, Message = string.Empty };
        }

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
                Console.WriteLine($"[ERROR] DataConnectTransport.GetLastValueAsync (protobuf) exception: {ex.Message}");
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

            var enumerated = tags.ToList();
            if (enumerated.Count == 1)
            {
                return SubscribeTag(enumerated[0].Handle, onUpdate);
            }

            var first = enumerated[0];
            return SubscribeTag(first.Handle, onUpdate);
        }

        private Any? ConvertToAny(object? value)
        {
            if (value == null) return null;

            switch (value)
            {
                case IMessage msg: return Any.Pack(msg);
                case int i:        return Any.Pack(new Int32Value { Value = i });
                case long l:       return Any.Pack(new Int64Value { Value = l });
                case double d:     return Any.Pack(new DoubleValue { Value = d });
                case float f:      return Any.Pack(new DoubleValue { Value = f });
                case bool b:       return Any.Pack(new BoolValue { Value = b });
                case string s:     return Any.Pack(new StringValue { Value = s });
                default:           return Any.Pack(new StringValue { Value = value.ToString() ?? string.Empty });
            }
        }

        private object? ConvertFromAny(Any? any)
        {
            if (any == null) return null;

            try
            {
                if (any.Is(Int32Value.Descriptor))  return any.Unpack<Int32Value>().Value;
                if (any.Is(Int64Value.Descriptor))  return any.Unpack<Int64Value>().Value;
                if (any.Is(DoubleValue.Descriptor)) return any.Unpack<DoubleValue>().Value;
                if (any.Is(BoolValue.Descriptor))   return any.Unpack<BoolValue>().Value;
                if (any.Is(StringValue.Descriptor)) return any.Unpack<StringValue>().Value;
                if (any.Is(Struct.Descriptor))      return any.Unpack<Struct>();
                if (any.Is(TagValue.Descriptor))    return any.Unpack<TagValue>();
                return any;
            }
            catch
            {
                return any;
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TiemaDataConnectTransport));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kv in _subscriptions) { try { kv.Value.Dispose(); } catch { } }
            _subscriptions.Clear();

            foreach (var kv in _rawSubscriptions) { try { kv.Value.Dispose(); } catch { } }
            _rawSubscriptions.Clear();

            try { _channel?.Dispose(); } catch { }
        }

        private sealed class SubscriptionGroup : IDisposable
        {
            private readonly TiemaDataConnectTransport _parent;
            private readonly uint _handle;
            private readonly ConcurrentDictionary<Guid, Action<object>> _callbacks = new();
            private CancellationTokenSource _cts = new();
            private Task? _readLoop;
            private AsyncServerStreamingCall<Update>? _call;
            private readonly object _startLock = new();

            public SubscriptionGroup(TiemaDataConnectTransport parent, uint handle)
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
                            while (await stream.MoveNext(_cts.Token).ConfigureAwait(false))
                            {
                                var upd = stream.Current;
                                object val = null;
                                switch (upd.PayloadCase)
                                {
                                    case Update.PayloadOneofCase.Tag:
                                        val = upd.Tag; break;
                                    case Update.PayloadOneofCase.Batch:
                                        val = upd.Batch; break;
                                    default:
                                        val = null; break;
                                }

                                var cbs = _callbacks.Values.ToArray();
                                foreach (var cb in cbs)
                                {
                                    try { cb(val); }
                                    catch (Exception ex) { Console.WriteLine($"[WARN] subscriber callback error: {ex.Message}"); }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) { }
                        catch (Exception ex) { Console.WriteLine($"[ERROR] SubscriptionGroup read loop error for handle={_handle}: {ex.Message}"); }
                    }, _cts.Token);
                }
            }

            internal void RemoveCallback(Guid id)
            {
                _callbacks.TryRemove(id, out _);
                if (_callbacks.IsEmpty)
                {
                    Dispose();
                    _parent._subscriptions.TryRemove(_handle, out _);
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

        private sealed class RawSubscriptionGroup : IDisposable
        {
            private readonly TiemaDataConnectTransport _parent;
            private readonly uint _handle;
            private readonly ConcurrentDictionary<Guid, Action<Update>> _callbacks = new();
            private CancellationTokenSource _cts = new();
            private Task? _readLoop;
            private AsyncServerStreamingCall<Update>? _call;
            private readonly object _startLock = new();

            public RawSubscriptionGroup(TiemaDataConnectTransport parent, uint handle)
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
                                var upd = stream.Current;
                                var cbs = _callbacks.Values.ToArray();
                                foreach (var cb in cbs)
                                {
                                    try { cb(upd); }
                                    catch (Exception ex) { Console.WriteLine($"[WARN] raw subscriber callback error: {ex.Message}"); }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) { }
                        catch (Exception ex) { Console.WriteLine($"[ERROR] RawSubscriptionGroup read loop error for handle={_handle}: {ex.Message}"); }
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