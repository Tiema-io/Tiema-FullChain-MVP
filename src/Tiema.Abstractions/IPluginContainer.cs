using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Abstractions
{
    /// <summary>
    /// 插件容器接口（替换具体的TiemaContainer）
    /// </summary>
    public interface IPluginContainer
    {
        void Run();
        void LoadPlugins();

        ITagService TagService { get; }
        IMessageService MessageService { get; }
    }
}
    
