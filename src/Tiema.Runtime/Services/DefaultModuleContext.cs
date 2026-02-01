using System;
using System.Linq;
using Tiema.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 默认模块上下文实现：将宿主暴露的 Racks/Slots/Tag/Message 服务映射为模块可用的上下文接口。
    /// Default module context implementation: delegate service lookups to host IServiceRegistry (exact key).
    /// </summary>
    public class DefaultModuleContext : IModuleContext
    {
        private readonly IModuleHost _host;
        private ISlot? _currentSlot;

        public DefaultModuleContext(IModuleHost host, ISlot? currentSlot = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _currentSlot = currentSlot;
        }

        public DefaultModuleContext(Tiema.Runtime.TiemaContainer container)
            : this((IModuleHost)container, null)
        {
        }

        // 注意：接口里 CurrentSlot 可能为非空，但运行时仍可能为空（模块未插入）
        public ISlot CurrentSlot
        {
            get
            {
                if (_currentSlot == null) throw new InvalidOperationException("CurrentSlot is not set for this context / 当前上下文未设置插槽");
                return _currentSlot;
            }
        }

        public ISlotManager Slots => _host.Slots;
        public IRackManager Racks => _host.Racks;
        public ITagService Tags => _host.Tags;
        public IMessageService Messages => _host.Messages;

        // 暴露宿主的 ServiceRegistry 便于模块通过 Context 查找服务
        public IServiceRegistry Services => _host.Services;

        public void SetCurrentSlot(ISlot? slot) => _currentSlot = slot;

    
    }
}
