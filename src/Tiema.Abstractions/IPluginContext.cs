using System;
using System.Collections.Generic;
using System.Text;


namespace Tiema.Abstractions
{
    // PluginContext.cs - 插件上下文
    public interface IPluginContext
    {
        IPluginContainer Container { get; }
        ITagService Tags { get; }
        IMessageService Messages { get; }
    }
}
