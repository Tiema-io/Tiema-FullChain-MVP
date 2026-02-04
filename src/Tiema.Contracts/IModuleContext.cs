using System;
using System.Collections.Generic;
using System.Text;


namespace Tiema.Contracts
{
    // PluginContext.cs - 插件上下文
    public interface IModuleContext
    {
        /// <summary>
        /// 模块实例 Id（由宿主在创建 Context 时赋值）
        /// Module instance id (set by host when creating the context)
        /// </summary>
        string ModuleInstanceId { get; }

        ISlot CurrentSlot { get; }

        /// <summary>
        /// 统一的服务注册/发现入口（宿主提供的单一 Registry）
        /// Host-level service registry for register/resolve services.
        /// </summary>
        IServiceRegistry Services { get; }
        /// <summary>
        /// Tag service for tag system operations
        /// </summary>
        ITagService Tags { get; }

        /// <summary>
        /// Message service for message system operations
        /// </summary>
        IMessageService Messages { get; }
    }
}
