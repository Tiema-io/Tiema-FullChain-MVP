using System;
using System.Collections.Generic;

namespace Tiema.Contracts
{
    /// <summary>
    /// Tag 系统接口：支持声明 Producer/Consumer、读写，以及订阅更新。
    /// Tag system interface: declare producer/consumer, read/write, and subscribe to updates.
    /// </summary>
    public interface ITagService
    {
        // === 基础读写（保持兼容） / Basic read-write (backward compatible) ===

        /// <summary>
        /// 设置/更新一个 Tag 值（按路径）。例如：Plant/Temperature。
        /// Set or update a tag value by path, e.g. "Plant/Temperature".
        /// </summary>
        void SetTag(string path, object value);

        /// <summary>
        /// 读取指定路径的 Tag 值，不存在时返回 default(T)。
        /// Read tag value by path; returns default(T) if not present.
        /// </summary>
        T GetTag<T>(string path);

        /// <summary>
        /// 尝试读取 Tag，返回是否存在。
        /// Try to read a tag; returns true if present.
        /// </summary>
        bool TryGetTag<T>(string path, out T value);

        /// <summary>
        /// 非泛型读取，主要用于调试或动态场景。
        /// Non-generic read, mainly for diagnostics/dynamic usage.
        /// </summary>
        object GetTag(string path);

        // === 声明式 Tag 拓扑：Producer / Consumer（为后续 Backplane/Registration 做准备） ===
        // === Declarative tag topology: Producer / Consumer (for future backplane/registration) ===

        /// <summary>
        /// 在当前模块实例中声明一个生产者 Tag（此模块负责写入该 Tag）。
        /// Declare a producer tag in this module instance (this module will write it).
        /// </summary>
        /// <param name="path">Tag 路径，例如 "Plant/Temperature" / Tag path, e.g. "Plant/Temperature".</param>
        void DeclareProducer(string path);

        /// <summary>
        /// 在当前模块实例中声明一个消费者 Tag（此模块读取该 Tag，底层负责路由）。
        /// Declare a consumer tag in this module instance (this module reads it; runtime handles routing).
        /// </summary>
        /// <param name="path">Tag 路径 / Tag path.</param>
        void DeclareConsumer(string path);

        // === 订阅更新（单进程用回调；跨进程时改为消息驱动） ===
        // === Subscribe to updates (callback for single-process; message-driven for cross-process) ===

        /// <summary>
        /// 订阅指定路径的 Tag 更新，当 Producer 发布时自动调用回调。
        /// Subscribe to updates of a tag by path; callback invoked when producer publishes.
        /// </summary>
        /// <param name="path">Tag 路径 / Tag path.</param>
        /// <param name="onUpdate">更新回调，参数为新值 / Update callback with new value.</param>
        /// <returns>订阅句柄，可用于取消订阅 / Subscription handle for unsubscribing.</returns>
        IDisposable SubscribeTag(string path, Action<object> onUpdate);
    }
}
