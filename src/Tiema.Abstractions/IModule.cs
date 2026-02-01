using System;

namespace Tiema.Abstractions
{
    /// <summary>
    /// 插件接口：唯一的初始化入口（Initialize），以及宿主可控制的生命周期方法 Start/Stop。
    /// Plugin interface: single initialization entry (Initialize) and host-controlled lifecycle Start/Stop.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// 插件名称 / Plugin name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 插件版本 / Plugin version
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 一次性初始化：注入上下文、订阅背板 / prepare resources.
        /// Initialize once: inject context, subscribe to backplane / prepare resources.
        /// </summary>
        void Initialize(IModuleContext context);

        /// <summary>
        /// 启动插件运行（宿主调用）。插件可在此启动内部循环或后台任务。
        /// Start plugin (called by host). Plugin may start internal loop or background tasks here.
        /// </summary>
        void Start();

        /// <summary>
        /// 停止插件（宿主调用）：请求插件优雅退出并释放资源。
        /// Stop plugin (called by host): request graceful shutdown and release resources.
        /// </summary>
        void Stop();

        ModuleType ModuleType { get; }  // Robot, Vision, PLC, Database, etc.
    

        // 
        void OnPlugged(ISlot slot);
        void OnUnplugged();
    }


    public enum ModuleType
    {
        Robot,
        Vision,
        PLC,
        Database,
        Other
    }
}
