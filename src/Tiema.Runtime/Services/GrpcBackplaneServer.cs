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
    public class GrpcBackplaneServer : BackplaneBase
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

        public GrpcBackplaneServer()
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

            // 如果请求里带有 tags（RegisterTagInfo），逐一处理
            foreach (var info in request.Tags ?? Enumerable.Empty<RegisterTagInfo>())
            {
                if (string.IsNullOrWhiteSpace(info.TagName)) continue;

                // 将 proto 的 Role 映射为宿主字符串 role（Producer/Consumer）
                var roleStr = info.Role == TagRole.Producer ? "Producer" : "Consumer";

                // 在宿主映射中创建/获取 identity
                var identity = GetOrCreateIdentity(info.TagName, roleStr, request.PluginInstanceId);

                // 把宿主身份信息映射回 protobuf 的 RegisterTagInfo 并加入响应 assigned
                var assigned = new RegisterTagInfo
                {
                    SourcePluginInstanceId = identity.ModuleInstanceId,
                    Handle = identity.Handle,
                    TagName = identity.Path,
                    Role = (identity.Role == TagRole.Producer) ? TagRole.Producer : TagRole.Consumer,
                    ReferencePluginInstanceId = identity.ModuleInstanceId
                };

                resp.Assigned.Add(assigned);
            }

            // 兼容：若 request.Tags 为空，但客户端可能期待按 ProducerPaths/ConsumerPaths 注册（保守兼容）
            // （如果不需要可删除下面代码）
            if ((request.Tags == null || request.Tags.Count == 0) && request.PluginInstanceId != null)
            {
                // no-op fallback: nothing to register beyond given tags
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
            if (request == null)
            {
                return Task.FromResult(new PublishResponse { Success = false, Message = "null request" });
            }

            var tag = request.Tag;
            if (tag == null)
                return Task.FromResult(new PublishResponse { Success = false, Message = "empty tag" });

            // 将 TagValue 打包为 Any 存镜像以兼容其他代码（也可直接存 TagValue）
            try
            {
                var any = Any.Pack(tag);
                var ts = Timestamp.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(
                    tag.TimestampUnixMs > 0 ? tag.TimestampUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                ).UtcDateTime);

                _mirror[tag.Handle] = (any, ts, (uint)tag.Quality, tag.SourcePluginId ?? string.Empty);
            }
            catch
            {
                // 如果 pack 失败，仍然继续广播原始 tag
            }

            if (_subscribers.TryGetValue(tag.Handle, out var list))
            {
                List<IServerStreamWriter<Update>> snapshot;
                lock (list)
                {
                    snapshot = list.ToList();
                }

                var update = new Update
                {
                    Tag = tag
                };

                foreach (var writer in snapshot)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await writer.WriteAsync(update).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARN] Failed to push update to subscriber (handle={tag.Handle}): {ex.Message}");
                        }
                    });
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