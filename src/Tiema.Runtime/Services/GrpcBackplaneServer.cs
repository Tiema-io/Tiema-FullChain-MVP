using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using static Tiema.Protocols.V1.Backplane;
using Tiema.Protocols.V1;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 简单的 gRPC Backplane 服务端实现（内存镜像 + 订阅广播）。
    /// 目的：提供可运行的服务器骨架用于集成测试与本地验证。
    /// 注意：生产环境需要完善安全、持久化、高可用、限流等。
    /// </summary>
    public class GrpcBackplaneServer : BackplaneBase
    {
        private readonly ConcurrentDictionary<uint, (Any Value, Timestamp Ts, uint Quality, string Source)> _mirror =
            new();

        // path -> TagIdentity (mirrors registration)
        private readonly ConcurrentDictionary<string, TagIdentity> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();

        private uint _nextHandle = 0;

        // subscribers: handle -> list of server stream writers (protected by lock per list)
        private readonly ConcurrentDictionary<uint, List<IServerStreamWriter<Update>>> _subscribers =
            new();

        // simple lock per handle list
        private object GetLockForHandle(uint handle) => (_subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>()));

        public GrpcBackplaneServer()
        {
        }

        /// <summary>
        /// 注册模块声明的 producers/consumers，并返回分配到的 identities（幂等：同一路径复用 handle）。
        /// 建议在分布式场景中把 registration 逻辑集中到此服务端以保证全局一致性。
        /// </summary>
        public override Task<RegisterModuleTagsResponse> RegisterModuleTags(RegisterModuleTagsRequest request, ServerCallContext context)
        {
            var resp = new RegisterModuleTagsResponse();

            if (request == null) return Task.FromResult(resp);

            // producers
            foreach (var path in request.ProducerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var identity = GetOrCreateIdentity(path, "Producer", request.ModuleInstanceId);
                resp.Identities.Add(identity);
            }

            // consumers
            foreach (var path in request.ConsumerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var identity = GetOrCreateIdentity(path, "Consumer", request.ModuleInstanceId);
                resp.Identities.Add(identity);
            }

            return Task.FromResult(resp);
        }

        private TagIdentity GetOrCreateIdentity(string path, string role, string moduleInstanceId)
        {
            if (_byPath.TryGetValue(path, out var existing))
            {
                // update role/owner if needed (keeps single handle per path)
                existing.Role = role;
                existing.OwnerModuleId = moduleInstanceId ?? existing.OwnerModuleId;
                _byHandle[existing.Handle] = existing;
                return existing;
            }

            var handle = System.Threading.Interlocked.Increment(ref _nextHandle);
            var identity = new TagIdentity
            {
                Handle = handle,
                Path = path,
                Role = role,
                OwnerModuleId = moduleInstanceId ?? string.Empty
            };

            _byPath[path] = identity;
            _byHandle[handle] = identity;
            return identity;
        }

        /// <summary>
        /// Publish: 更新镜像并广播到订阅该 handle 的客户端。
        /// </summary>
        public override Task<PublishResponse> Publish(PublishRequest request, ServerCallContext context)
        {
            if (request == null)
            {
                return Task.FromResult(new PublishResponse { Ok = false, Error = "null request" });
            }

            // update mirror
            _mirror[request.Handle] = (request.Value, request.Timestamp, request.Quality, request.SourceModuleId ?? string.Empty);

            // broadcast (fire-and-forget writes; capture exceptions per-subscriber)
            if (_subscribers.TryGetValue(request.Handle, out var list))
            {
                List<IServerStreamWriter<Update>> snapshot;
                lock (list)
                {
                    snapshot = list.ToList();
                }

                var update = new Update
                {
                    Handle = request.Handle,
                    Value = request.Value,
                    Timestamp = request.Timestamp,
                    Quality = request.Quality,
                    SourceModuleId = request.SourceModuleId ?? string.Empty
                };

                foreach (var writer in snapshot)
                {
                    // send asynchronously, do not block Publish
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await writer.WriteAsync(update).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // best-effort: log and continue; stale/closed streams will be removed by subscription handler
                            Console.WriteLine($"[WARN] Failed to push update to subscriber (handle={request.Handle}): {ex.Message}");
                        }
                    });
                }
            }

            return Task.FromResult(new PublishResponse { Ok = true });
        }

        /// <summary>
        /// GetLastValue: 从镜像读取最新值（若存在）。
        /// </summary>
        public override Task<GetResponse> GetLastValue(GetRequest request, ServerCallContext context)
        {
            var resp = new GetResponse { Found = false };
            if (request == null) return Task.FromResult(resp);

            if (_mirror.TryGetValue(request.Handle, out var entry))
            {
                resp.Value = entry.Value;
                resp.Timestamp = entry.Ts;
                resp.Quality = entry.Quality;
                resp.Found = true;
            }

            return Task.FromResult(resp);
        }

        /// <summary>
        /// Subscribe: 建立 server-stream，服务端在 Publish 时会往该流写 Update。
        /// 在订阅期间阻塞直到客户端取消（断开）。
        /// </summary>
        public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<Update> responseStream, ServerCallContext context)
        {
            if (request == null) return;

            var handle = request.Handle;

            var list = _subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>());

            // add
            lock (list)
            {
                list.Add(responseStream);
            }

            try
            {
                // on subscribe, optionally send current snapshot if exists
                if (_mirror.TryGetValue(handle, out var entry))
                {
                    var initial = new Update
                    {
                        Handle = handle,
                        Value = entry.Value,
                        Timestamp = entry.Ts,
                        Quality = entry.Quality,
                        SourceModuleId = entry.Source
                    };
                    await responseStream.WriteAsync(initial).ConfigureAwait(false);
                }

                // wait until client cancels
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
                // remove
                lock (list)
                {
                    list.Remove(responseStream);
                }
            }
        }
    }
}