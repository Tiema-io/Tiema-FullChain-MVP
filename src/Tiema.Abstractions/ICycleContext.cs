using System;
using System.Collections.Generic;
using System.Text;



namespace Tiema.Abstractions

{
    public interface ICycleContext
    {
        // ========== 核心服务 ==========

        /// <summary>
        /// Tag系统服务
        /// </summary>
        ITagService Tags { get; }

        /// <summary>
        /// 消息系统服务
        /// </summary>
        IMessageService Messages { get; }
    }
}
