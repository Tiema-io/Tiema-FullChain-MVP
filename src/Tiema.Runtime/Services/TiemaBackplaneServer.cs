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
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 简单的 gRPC Backplane 服务端实现（内存镜像 + 订阅广播）。
    /// 目的：提供可运行的服务器骨架用于集成测试与本地验证。
    /// 注意：生产环境需要完善安全、持久化、高可用、限流等。
    /// </summary>
    public class TiemaBackplaneServer : BackplaneBase
    {
        private readonly ConcurrentDictionary<uint, (Any Value, Timestamp Ts, uint Quality, string Source)> _mirror =
            new();

        // path -> TagIdentity (mirrors registration)  使用宿主抽象层的 TagIdentity
        private readonly ConcurrentDictionary<string, TagIdentity> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();

        private uint _nextHandle = 0;

        // subscribers: handle -> list of server stream writers (protected by lock per list)
        private readonly ConcurrentDictionary<uint, List<IServerStreamWriter<Update>>> _subscribers =
            new();

        // simple lock per handle list
        private object GetLockForHandle(uint handle) => (_subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>()));

        public TiemaBackplaneServer()
        {
        }

        /// <summary>
        /// 注册插件声明的 tags（基于 tagsystem.proto 的 RegisterTagsRequest/Response）。
        /// - 内部保持 path -> 宿主 TagIdentity 映射；
        /// - 返回值为 protobuf 的 RegisterTagsResponse，其中 assigned 为 RegisterTagInfo 列表。
        /// </summary>
        public override Task<RegisterTagsResponse> RegisterTags(RegisterTagsRequest request, ServerCallContext context)
        {
            var resp = new RegisterTagsResponse
            {
                Success = true,
                Message = string.Empty
            };

            if (request == null) return Task.FromResult(resp);

            // 请求级模块实例 id（fallback）
            var requestModuleId = request.ModuleInstanceId ?? string.Empty;

            // 逐条处理注册请求，保持幂等：若 path 已存在且 role/module 相同则复用已存在 handle
            foreach (var info in request.Tags ?? Enumerable.Empty<RegisterTagInfo>())
            {
                if (string.IsNullOrWhiteSpace(info.TagPath)) continue;

                // 选择 source module id：优先使用单条的 source_module_instance_id，否则回退到 request.module_instance_id
                var sourceModuleId = !string.IsNullOrWhiteSpace(info.SourceModuleInstanceId)
                    ? info.SourceModuleInstanceId
                    : requestModuleId;

                // 将 proto 的 Role 映射为宿主字符串 role（Producer/Consumer）
                var roleStr = info.Role == TagRole.Producer ? "Producer" : "Consumer";

                // 幂等检查：若已有相同 path 的 identity，且 role 与 moduleInstanceId 匹配，则复用
                if (_byPath.TryGetValue(info.TagPath, out var existing))
                {
                    var desiredRole = roleStr.Equals("Producer", StringComparison.OrdinalIgnoreCase)
                        ? TagRole.Producer
                        : TagRole.Consumer;

                    if (existing.Role == desiredRole && existing.ModuleInstanceId == (sourceModuleId ?? string.Empty))
                    {
                        // 复用已有 identity，返回已分配信息
                        var assignedExisting = new RegisterTagInfo
                        {
                            SourceModuleInstanceId = existing.ModuleInstanceId,
                            Handle = existing.Handle,
                            TagPath = existing.Path,
                            Role = existing.Role,
                            ReferenceModuleInstanceId = existing.ModuleInstanceId
                        };
                        resp.Assigned.Add(assignedExisting);
                        continue;
                    }
                    // 若 path 存在但 role/module 不同，继续走创建/更新流程（GetOrCreateIdentity 会覆盖/更新）
                }

                // 在宿主映射中创建/获取 identity（GetOrCreateIdentity 会处理新建或更新）
                var identity = GetOrCreateIdentity(info.TagPath, roleStr, sourceModuleId);

                // 把宿主身份信息映射回 protobuf 的 RegisterTagInfo 并加入响应 assigned
                var assigned = new RegisterTagInfo
                {
                    SourceModuleInstanceId = identity.ModuleInstanceId,
                    Handle = identity.Handle,
                    TagPath = identity.Path,
                    Role = identity.Role,
                    ReferenceModuleInstanceId = identity.ModuleInstanceId
                };

                resp.Assigned.Add(assigned);
            }

            return Task.FromResult(resp);
        }

        // 使用 Tiema.Hosting.Abstractions.TagIdentity（不可变 record）
        // 同一路径复用同一 handle；如需要更新 role/module 则创建新的 record 替换
        private TagIdentity GetOrCreateIdentity(string path, string role, string moduleInstanceId)
        {
            if (_byPath.TryGetValue(path, out var existing))
            {
                // compute desired role
                var desiredRole = role?.Equals("Producer", StringComparison.OrdinalIgnoreCase) == true
                    ? TagRole.Producer
                    : TagRole.Consumer;

                if (existing.Role == desiredRole && existing.ModuleInstanceId == (moduleInstanceId ?? string.Empty))
                {
                    return existing;
                }

                // create updated record
                var updated = new TagIdentity(existing.Handle, existing.Path, desiredRole, moduleInstanceId ?? string.Empty);
                _byPath[path] = updated;
                _byHandle[existing.Handle] = updated;
                return updated;
            }

            // 新建 identity（生成唯一 handle）
            var handle = Interlocked.Increment(ref _nextHandle);
            var mappedRole = role?.Equals("Producer", StringComparison.OrdinalIgnoreCase) == true
                ? TagRole.Producer
                : TagRole.Consumer;

            var identityNew = new TagIdentity(handle, path, mappedRole, moduleInstanceId ?? string.Empty);

            _byPath[path] = identityNew;
            _byHandle[handle] = identityNew;
            return identityNew;
        }

        /// <summary>
        /// Publish: 更新镜像并广播到订阅该 handle 的客户端（使用 TagValue / Update）。
        /// </summary>
        public override Task<PublishResponse> Publish(PublishRequest request, ServerCallContext context)
        {
            if (request == null) return Task.FromResult(new PublishResponse { Success = false, Message = "null request" });

            // 处理单条或批量
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
                // 复用现有单条处理逻辑：存镜像并广播
                try
                {
                    var any = Any.Pack(tag);
                    var ts = tag.Timestamp ?? Timestamp.FromDateTime(DateTime.UtcNow);

                    _mirror[tag.Handle] = (any, ts, (uint)tag.Quality, tag.SourceModuleInstanceId ?? string.Empty);
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

        /// <summary>
        /// GetLastValue: 从镜像读取最新值（若存在）。
        /// </summary>
        public override Task<GetResponse> GetLastValue(GetRequest request, ServerCallContext context)
        {
            var resp = new GetResponse { Found = false };
            if (request == null) return Task.FromResult(resp);

            if (_mirror.TryGetValue(request.Handle, out var entry))
            {
                try
                {
                    // 尝试把存的 Any 解包为 TagValue（如果是 TagValue）
                    var tv = entry.Value.Unpack<TagValue>();
                    resp.Value = tv;
                    resp.Found = true;
                }
                catch
                {
                    // 如果不是 TagValue（历史原因），可以选择不返回或转换为 TagValue 包装
                    resp.Found = false;
                }
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
                    try
                    {
                        var tv = entry.Value.Unpack<TagValue>();
                        var initial = new Update { Tag = tv };
                        await responseStream.WriteAsync(initial).ConfigureAwait(false);
                    }
                    catch
                    {
                        // fallback: 原有逻辑或忽略
                    }
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

        // 新增到 GrpcBackplaneServer 类中：一个内部测试用注入方法
        internal void AddTestSubscriber(uint handle, IServerStreamWriter<Update> writer)
        {
            var list = _subscribers.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>());
            lock (list)
            {
                list.Add(writer);
            }
        }
    }
}