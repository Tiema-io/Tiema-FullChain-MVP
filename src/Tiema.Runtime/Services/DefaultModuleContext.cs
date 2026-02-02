using System;
using System.Linq;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;


namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 默认模块上下文实现：将宿主暴露的 Racks/Slots/Tag/Message 服务映射为模块可用的上下文接口。
    /// Default module context implementation: delegate service lookups to host IServiceRegistry (exact key).
    /// 此实现为每个模块创建一个 module-scoped ITagService 代理，使 DeclareProducer/DeclareConsumer 能立即触发注册。
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

            // 用 module-scoped 代理包装 tagService，使得 DeclareProducer/DeclareConsumer 可以立即注册到 RegistrationManager
            Tags = new ModuleScopedTagService(ModuleInstanceId, tagService, serviceRegistry);
            Messages = messageService;
            Services = serviceRegistry;
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

        // -------- 内部实现：module-scoped 的 ITagService 代理 ----------
        // 目的：在模块调用 DeclareProducer/DeclareConsumer 时，立即调用 RegistrationManager.RegisterModuleTags，
        // 并把分配的 TagIdentity 通知到底层 BuiltInTagService（如果可用），从而消除注册竞态。
        private sealed class ModuleScopedTagService : ITagService
        {
            private readonly string _moduleId;
            private readonly ITagService _inner;
            private readonly IServiceRegistry _registry;

            public ModuleScopedTagService(string moduleId, ITagService inner, IServiceRegistry registry)
            {
                _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            }

            // 直接委托其余方法
            public void SetTag(string path, object value) => _inner.SetTag(path, value);
            public T GetTag<T>(string path) => _inner.GetTag<T>(path);
            public bool TryGetTag<T>(string path, out T value) => _inner.TryGetTag<T>(path, out value);
            public object GetTag(string path) => _inner.GetTag(path);
            public IDisposable SubscribeTag(string path, Action<object> onUpdate) => _inner.SubscribeTag(path, onUpdate);

            // DeclareProducer：先在本地 inner 标记 producer，然后立即调用 RegistrationManager 注册并通知 BuiltInTagService（若实现）
            public void DeclareProducer(string path)
            {
                _inner.DeclareProducer(path);

                // 从 registry 读取 host-level registration manager
                var reg = _registry.Get<ITagRegistrationManager>(string.Empty, 0, "TagRegistrationManager");

                // 较安全的尝试两种查法（兼容不同 IServiceRegistry 实现）
                if (reg != null)
                {
                    try
                    {
                        var identities = reg.RegisterModuleTags(_moduleId, new[] { path }, null);

                        // 如果底层是 BuiltInTagService，通知它已完成注册（便于建立 backplane 订阅）
                        if (_inner is Tiema.Runtime.Services.BuiltInTagService bis)
                        {
                            bis.OnTagsRegistered(identities);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Immediate RegisterModuleTags failed for producer {path}: {ex.Message}");
                    }
                }
            }

            // DeclareConsumer：同上，立即注册 consumer
            public void DeclareConsumer(string path)
            {
                _inner.DeclareConsumer(path);

                var reg = _registry.Get<ITagRegistrationManager>(string.Empty, 0, "TagRegistrationManager");

                if (reg != null)
                {
                    try
                    {
                        var identities = reg.RegisterModuleTags(_moduleId, null, new[] { path });
                        if (_inner is Tiema.Runtime.Services.BuiltInTagService bis)
                        {
                            bis.OnTagsRegistered(identities);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Immediate RegisterModuleTags failed for consumer {path}: {ex.Message}");
                    }
                }
            }
        }
    }
}
