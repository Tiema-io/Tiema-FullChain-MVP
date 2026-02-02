using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tiema.Hosting.Abstractions
{
    /// <summary>
    /// Tag 数据传输平面抽象：负责存储、发布和订阅 Tag 更新。
    /// Tag data transport backplane abstraction: handles storage, publishing, and subscribing to tag updates.
    /// </summary>
    public interface IBackplane
    {
        /// <summary>
        /// 发布 Tag 更新（Producer 调用）。
        /// Publish tag update (called by producer).
        /// </summary>
        Task PublishAsync(uint handle, object value, CancellationToken ct = default);

        /// <summary>
        /// 获取 Tag 最新值（Consumer 拉取用）。
        /// Get latest tag value (for consumer pull).
        /// </summary>
        Task<object?> GetLastValueAsync(uint handle, CancellationToken ct = default);

        /// <summary>
        /// 订阅 Tag 更新（单进程用回调；跨进程时改为消息驱动）。
        /// Subscribe to tag updates (callback for single-process; message-driven for cross-process).
        /// </summary>
        /// <param name="handle">Tag 句柄 / Tag handle.</param>
        /// <param name="onUpdate">更新回调 / Update callback.</param>
        /// <returns>订阅句柄 / Subscription handle.</returns>
        IDisposable Subscribe(uint handle, Action<object> onUpdate);
    }
}