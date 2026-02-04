using System;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 默认模块上下文实现：将宿主暴露的 Racks/Slots/Tag/Message 服务映射为模块可用的上下文接口。
    /// Default module context implementation: delegate service lookups to host IServiceRegistry (exact key).
    /// 注意：不再在 context 层面立即触发注册（移除了 ModuleScopedTagService），由宿主在 LoadModule 后统一调用注册器。
    /// </summary>
    public class DefaultModuleContext : IModuleContext
    {
        private ISlot? _currentSlot;
        public ITagService Tags { get; }
        public IMessageService Messages { get; }
        public IServiceRegistry Services { get; }

        // module instance id (由宿主在创建 context 时传入)
        public string ModuleInstanceId { get; }

        public DefaultModuleContext(IModuleHost host,
            string moduleInstanceId,
            ITagService tagService,
            IMessageService messageService,
            IServiceRegistry serviceRegistry)
        {
            ModuleInstanceId = moduleInstanceId ?? throw new ArgumentNullException(nameof(moduleInstanceId));

            // 直接暴露宿主注入的 ITagService（不再包装为 ModuleScopedTagService）
            Tags = tagService ?? throw new ArgumentNullException(nameof(tagService));
            Messages = messageService ?? throw new ArgumentNullException(nameof(messageService));
            Services = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
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

        public void SetCurrentSlot(ISlot? slot) => _currentSlot = slot;
    }
}
