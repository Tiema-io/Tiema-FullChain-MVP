using System;
using System.Collections.Generic;
using Tiema.Contracts;

namespace Tiema.Hosting.Abstractions
{
    /// <summary>
    /// 平台宿主 / Module host
    /// 提供运行时生命周期、rack/slot/module 管理和核心服务访问点。
    /// Runtime host: lifecycle control, rack/slot/module management and core services access.
    /// </summary>
    public interface IModuleHost
    {
        /// <summary>
        /// 启动并阻塞运行宿主（若实现为守护进程）。
        /// Start and run the host (may block for daemon-style hosts).
        /// </summary>
        void Run();

        /// <summary>
        /// 请求停止宿主，触发插件/模块的优雅停止。
        /// Request host stop; triggers graceful shutdown of modules.
        /// </summary>
        void Stop();

        
        IRackManager Racks { get; }

        /// <summary>
        /// 插槽管理器（便于按插槽查找/访问模块）。
        /// Slot manager for finding/accessing modules by slot.
        /// </summary>
        ISlotManager Slots { get; }

        /// <summary>
        /// 平台的服务注册/发现入口（只读视图或可注册接口，视实现而定）。
        /// Service registry / discovery entrypoint for the platform.
        /// </summary>
        IServiceRegistry Services { get; }

    }
}