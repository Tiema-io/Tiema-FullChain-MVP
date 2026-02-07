using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Tiema.Tags.Grpc.V1;          // tagsystem.proto
using Tiema.Connect.Grpc.V1;       // connect.proto (DataConnect)
using Tiema.Hosting.Abstractions;
using static Tiema.Connect.Grpc.V1.DataConnect;

namespace Tiema.DataConnect.Core
{
    /// <summary>
    /// DataConnect gRPC server implementation (in-memory mirror + subscriptions).
    /// </summary>
    public class TiemaDataConnectServer : DataConnectBase
    {
        private readonly ConcurrentDictionary<uint, (Any Value, Timestamp Ts, uint Quality, string Source)> _mirror = new();

        private readonly ConcurrentDictionary<string, TagIdentity> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();

        private uint _nextHandle = 0;

        private readonly ConcurrentDictionary<uint, List<IServerStreamWriter<Update>>> _subscribers = new();

        private object GetLockForHandle(uint handle) => (_subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>()));

        public TiemaDataConnectServer() { }

        public override Task<RegisterTagsResponse> RegisterTags(RegisterTagsRequest request, ServerCallContext context)
        {
            var resp = new RegisterTagsResponse { Success = true, Message = string.Empty };
            if (request == null) return Task.FromResult(resp);

            // request-level plugin instance id (fallback)
            var requestPluginId = request.PluginInstanceId ?? string.Empty;

            foreach (var info in request.Tags ?? Enumerable.Empty<RegisterTagInfo>())
            {
                if (string.IsNullOrWhiteSpace(info.TagPath)) continue;

                // per-item source plugin id, fallback to request-level
                var sourcePluginId = !string.IsNullOrWhiteSpace(info.SourcePluginInstanceId)
                    ? info.SourcePluginInstanceId
                    : requestPluginId;

                // map role for host comparison
                var roleStr = info.Role == TagRole.Producer ? "Producer" : "Consumer";

                if (_byPath.TryGetValue(info.TagPath, out var existing))
                {
                    var desiredRole = roleStr.Equals("Producer", StringComparison.OrdinalIgnoreCase)
                        ? TagRole.Producer
                        : TagRole.Consumer;

                    if (existing.Role == desiredRole && existing.ModuleInstanceId == (sourcePluginId ?? string.Empty))
                    {
                        var assignedExisting = new RegisterTagInfo
                        {
                            SourcePluginInstanceId = existing.ModuleInstanceId,
                            Handle = existing.Handle,
                            TagPath = existing.Path,
                            Role = existing.Role,
                            ReferencePluginInstanceId = existing.ModuleInstanceId
                        };
                        resp.Assigned.Add(assignedExisting);
                        continue;
                    }
                }

                var identity = GetOrCreateIdentity(info.TagPath, roleStr, sourcePluginId);

                var assigned = new RegisterTagInfo
                {
                    SourcePluginInstanceId = identity.ModuleInstanceId,
                    Handle = identity.Handle,
                    TagPath = identity.Path,
                    Role = identity.Role,
                    ReferencePluginInstanceId = identity.ModuleInstanceId
                };

                resp.Assigned.Add(assigned);
            }

            return Task.FromResult(resp);
        }

        private TagIdentity GetOrCreateIdentity(string path, string role, string pluginInstanceId)
        {
            if (_byPath.TryGetValue(path, out var existing))
            {
                var desiredRole = role?.Equals("Producer", StringComparison.OrdinalIgnoreCase) == true
                    ? TagRole.Producer
                    : TagRole.Consumer;

                if (existing.Role == desiredRole && existing.ModuleInstanceId == (pluginInstanceId ?? string.Empty))
                {
                    return existing;
                }

                var updated = new TagIdentity(existing.Handle, existing.Path, desiredRole, pluginInstanceId ?? string.Empty);
                _byPath[path] = updated;
                _byHandle[existing.Handle] = updated;
                return updated;
            }

            var handle = Interlocked.Increment(ref _nextHandle);
            var mappedRole = role?.Equals("Producer", StringComparison.OrdinalIgnoreCase) == true
                ? TagRole.Producer
                : TagRole.Consumer;

            var identityNew = new TagIdentity(handle, path, mappedRole, pluginInstanceId ?? string.Empty);
            _byPath[path] = identityNew;
            _byHandle[handle] = identityNew;
            return identityNew;
        }

        public override Task<PublishResponse> Publish(PublishRequest request, ServerCallContext context)
        {
            if (request == null) return Task.FromResult(new PublishResponse { Success = false, Message = "null request" });

            IEnumerable<TagValue> toPublish;
            if (request.PayloadCase == PublishRequest.PayloadOneofCase.Tag && request.Tag != null)
            {
                toPublish = new[] { request.Tag };
            }
            else if (request.PayloadCase == PublishRequest.PayloadOneofCase.Batch && request.Batch != null)
            {
                toPublish = request.Batch.Tags;
            }
            else
            {
                return Task.FromResult(new PublishResponse { Success = false, Message = "empty publish payload" });
            }

            foreach (var tag in toPublish)
            {
                try
                {
                    var any = Any.Pack(tag);
                    var ts = tag.Timestamp ?? Timestamp.FromDateTime(DateTime.UtcNow);

                    _mirror[tag.Handle] = (any, ts, (uint)tag.Quality, tag.SourcePluginInstanceId ?? string.Empty);
                }
                catch { /* best-effort */ }

                if (_subscribers.TryGetValue(tag.Handle, out var list))
                {
                    List<IServerStreamWriter<Update>> snapshot;
                    lock (list) { snapshot = list.ToList(); }

                    var update = new Update { Tag = tag };
                    foreach (var writer in snapshot)
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await writer.WriteAsync(update).ConfigureAwait(false); }
                            catch (Exception ex) { Console.WriteLine($"[WARN] Failed to push update (handle={tag.Handle}): {ex.Message}"); }
                        });
                    }
                }
            }

            return Task.FromResult(new PublishResponse { Success = true, Message = string.Empty });
        }

        public override Task<GetResponse> GetLastValue(GetRequest request, ServerCallContext context)
        {
            var resp = new GetResponse { Found = false };
            if (request == null) return Task.FromResult(resp);

            if (_mirror.TryGetValue(request.Handle, out var entry))
            {
                try
                {
                    var tv = entry.Value.Unpack<TagValue>();
                    resp.Value = tv;
                    resp.Found = true;
                }
                catch
                {
                    resp.Found = false;
                }
            }

            return Task.FromResult(resp);
        }

        public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<Update> responseStream, ServerCallContext context)
        {
            if (request == null) return;

            var handle = request.Handle;
            var list = _subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>());

            lock (list) { list.Add(responseStream); }

            try
            {
                if (_mirror.TryGetValue(handle, out var entry))
                {
                    try
                    {
                        var tv = entry.Value.Unpack<TagValue>();
                        var initial = new Update { Tag = tv };
                        await responseStream.WriteAsync(initial).ConfigureAwait(false);
                    }
                    catch { }
                }

                var ct = context.CancellationToken;
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => tcs.TrySetResult(null)))
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                // client cancelled
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Subscribe stream error for handle={handle}: {ex.Message}");
            }
            finally
            {
                lock (list) { list.Remove(responseStream); }
            }
        }

        internal void AddTestSubscriber(uint handle, IServerStreamWriter<Update> writer)
        {
            var list = _subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>());
            lock (list) { list.Add(writer); }
        }
    }
}