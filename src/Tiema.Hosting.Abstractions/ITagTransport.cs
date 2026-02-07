using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Tags.Grpc.V1;




namespace Tiema.Hosting.Abstractions
{
    /// <summary>
    /// Tag 层的传输抽象：面向 protobuf 的 TagValue / TagBatch / RegisterTags 等网络传输语义。
    /// - 使网络/协议实现（gRPC、MQ 等）与运行时本地 Backplane 分离。
    /// - Adapter 层（GrpcBackplaneAdapter）依赖此接口与远端协同 assembly/周期交换。
    /// </summary>
    public interface ITagTransport : IDisposable
    {
        /// <summary>
        /// 注册插件声明的 tags，返回带分配 handle 的结果（protobuf 类型）。
        /// </summary>
        Task<RegisterTagsResponse> RegisterTagsAsync(RegisterTagsRequest request, CancellationToken ct = default);

        /// <summary>
        /// 发布单个 TagValue（protobuf 表示，原样传输）。
        /// </summary>
        Task<PublishResponse> PublishTagAsync(TagValue tag, CancellationToken ct = default);

        /// <summary>
        /// 发布 TagBatch（批量）。
        /// </summary>
        Task<PublishResponse> PublishBatchAsync(TagBatch batch, CancellationToken ct = default);

        /// <summary>
        /// 获取单个句柄的最新 TagValue（protobuf 表示）。
        /// </summary>
        Task<GetResponse> GetLastValueAsync(uint handle, CancellationToken ct = default);

        /// <summary>
        /// 订阅单个句柄的更新，回调接收 protobuf 层的 Update（可能为 TagValue 或 TagBatch）。
        /// 返回 IDisposable 以取消订阅。
        /// </summary>
        IDisposable SubscribeTag(uint handle, Action<Update> onUpdate);

        /// <summary>
        /// 批量/assembly 订阅：按 tags 列表和 rpi_ms 订阅，回调接收 Update（通常为 TagBatch）。
        /// 返回 IDisposable 以取消订阅。
        /// </summary>
        IDisposable SubscribeBatch(IEnumerable<RegisterTagInfo> tags, uint rpiMs, Action<Update> onUpdate);
    }
}