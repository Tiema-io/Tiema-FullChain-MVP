using System;

using Tiema.Contracts;
using Tiema.DataConnect.Core;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

namespace Tiema.Runtime
{
    /// <summary>
    /// TiemaHost 构建器：负责组装机架/插槽/服务注册表及核心运行时服务。
    /// TiemaHost builder: assembles racks/slots/service registry and core runtime services.
    /// </summary>
    public sealed class TiemaHostBuilder
    {
        private readonly TiemaConfig _config;

        // 允许将来替换实现的可配置项（当前使用默认实现）。
        // Configurable components for future replacement (now using defaults).
        private IRackManager? _rackManager;
        private ISlotManager? _slotManager;
        private SimpleServiceRegistry? _serviceRegistry;
        private ITagRegistrationManager? _tagRegistrationManager;
        private IBackplane? _backplane;
        private ITagService? _tagService;
        private IMessageService? _messageService;

        // new: optional gRPC url if caller prefers grpc backplane
        private string? _grpcBackplaneUrl;

        private TiemaHostBuilder(TiemaConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 使用给定的配置创建一个新的 HostBuilder。
        /// Create a new host builder with the given config.
        /// </summary>
        public static TiemaHostBuilder Create(TiemaConfig config) => new(config);

        /// <summary>
        /// 使用自定义的机架/插槽管理器。
        /// Use custom rack/slot managers.
        /// </summary>
        public TiemaHostBuilder UseRackAndSlotManagers(IRackManager rackManager, ISlotManager slotManager)
        {
            _rackManager = rackManager ?? throw new ArgumentNullException(nameof(rackManager));
            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            return this;
        }

        /// <summary>
        /// 使用自定义的核心运行时服务实现（Tag/Message/Registration/Backplane）。
        /// Use custom core runtime service implementations (Tag/Message/Registration/Backplane).
        /// </summary>
        public TiemaHostBuilder UseRuntimeServices(
            ITagService tagService,
            IMessageService messageService,
            ITagRegistrationManager tagRegistrationManager,
            IBackplane backplane)
        {
            _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _tagRegistrationManager = tagRegistrationManager ?? throw new ArgumentNullException(nameof(tagRegistrationManager));
            _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
            return this;
        }

        /// <summary>
        /// 强制使用 InMemoryBackplane（适合本地开发/调试）。
        /// Use in-memory backplane (recommended for local development / debugging).
        /// </summary>
        public TiemaHostBuilder UseInMemoryBackplane()
        {
            _backplane = new InMemoryBackplane();
            return this;
        }

        /// <summary>
        /// 使用 gRPC Backplane（远端服务地址），客户端将在 Build 时创建 GrpcBackplaneClient。
        /// Use gRPC backplane (remote service URL). A GrpcBackplaneClient will be created in Build().
        /// </summary>
        public TiemaHostBuilder UseGrpcBackplane(string grpcUrl)
        {
            if (string.IsNullOrWhiteSpace(grpcUrl)) throw new ArgumentException("grpcUrl required", nameof(grpcUrl));
            _grpcBackplaneUrl = grpcUrl;
            _backplane = null; // ensure Build will create grpc client
            return this;
        }

        /// <summary>
        /// 构建 TiemaHost 实例（若未提供自定义实现则使用内置默认实现）。
        /// Build TiemaHost instance (uses built-in defaults when overrides not provided).
        /// </summary>
        public TiemaHost Build()
        {
            // 1. 机架/插槽管理器
            var rackManager = _rackManager ?? new SimpleRackManager();
            var slotManager = _slotManager ?? new SimpleSlotManager(rackManager);

            // 2. 统一服务注册表
            var services = _serviceRegistry ?? new SimpleServiceRegistry(rackManager);

            // 3. 核心运行时服务（延迟决定注册管理器实现）
            //    不在此处默认创建 InMemoryTagRegistrationManager，优先使用注入实现。
            ITagRegistrationManager? tagRegistrationManager = _tagRegistrationManager;

            // Decide backplane implementation:
            IBackplane backplane;
            if (_backplane != null)
            {
                backplane = _backplane;
            }
            else if (!string.IsNullOrEmpty(_grpcBackplaneUrl))
            {
                // Create a GrpcBackplaneTransport (client). Real gRPC implementation can replace this class.
                backplane = new TiemaDataConnectTransport(_grpcBackplaneUrl);

                // 如果用户未显式提供 registration manager，则为 gRPC 模式创建远端注册管理器
                if (tagRegistrationManager == null)
                {
                    tagRegistrationManager = new TiemaDataConnectTagRegistrationManager(_grpcBackplaneUrl);
                }
            }
            else
            {
                backplane = new InMemoryBackplane();
            }

            // 最后回退：若仍未提供 registration manager，则使用 InMemory
            tagRegistrationManager ??= new InMemoryTagRegistrationManager();

            var tagService = _tagService ?? new BuiltInTagService(tagRegistrationManager, backplane);
            var messageService = _messageService ?? new BuiltInMessageService();

            // 4. 将这些服务以“宿主级”键注册到统一 ServiceRegistry
            //    Host-level key convention: rackName = "", slotId = 0.
            services.Register(string.Empty, 0, "TagService", tagService);
            services.Register(string.Empty, 0, "MessageService", messageService);
            services.Register(string.Empty, 0, "Backplane", backplane);
            services.Register(string.Empty, 0, "TagRegistrationManager", tagRegistrationManager);

            // 5. 构造 TiemaHost（使用内部构造函数）
            return new TiemaHost(
                _config,
                rackManager,
                slotManager,
                services,
                tagRegistrationManager,
                backplane,
                tagService,
                messageService);
        }
    }
}