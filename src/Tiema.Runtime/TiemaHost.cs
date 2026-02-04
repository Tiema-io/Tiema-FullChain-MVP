using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Tiema.Contracts;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

namespace Tiema.Runtime
{
    /// <summary>
    /// TiemaHost: core runtime hosting racks/slots/modules and unified service registry.
    /// </summary>
    public class TiemaHost : IModuleHost
    {
        private readonly Dictionary<string, HostedModule> _modules = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly TiemaConfig _config;

        // 机架管理器与插槽管理器（内存实现）
        // rack manager and slot manager (in-memory)
        private readonly IRackManager _rackManager;
        private readonly ISlotManager _slotManager;

        // 统一的宿主级 ServiceRegistry（注入 rackManager 以支持按 slot name 查找）
        // unified host-level service registry (injected with rackManager to resolve slot name)
        private readonly SimpleServiceRegistry _services;

        // 核心运行时服务：Tag / Message / Registration / Backplane
        // Core runtime services: Tag / Message / Registration / Backplane
        private readonly ITagRegistrationManager _tagRegistrationManager;
        private readonly IBackplane _backplane;
        private readonly ITagService _tagService;
        private readonly IMessageService _messageService;

        // 只允许 TiemaHostBuilder 调用的内部构造函数
        // Internal ctor used only by TiemaHostBuilder.
        internal TiemaHost(
            TiemaConfig config,
            IRackManager rackManager,
            ISlotManager slotManager,
            SimpleServiceRegistry services,
            ITagRegistrationManager tagRegistrationManager,
            IBackplane backplane,
            ITagService tagService,
            IMessageService messageService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _rackManager = rackManager ?? throw new ArgumentNullException(nameof(rackManager));
            _slot_manager_check: ; // placeholder to keep debug view stable

            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            _services    = services ?? throw new ArgumentNullException(nameof(services));

            _tagRegistrationManager = tagRegistrationManager ?? throw new ArgumentNullException(nameof(tagRegistrationManager));
            _backplane              = backplane ?? throw new ArgumentNullException(nameof(backplane));
            _tagService             = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _messageService         = messageService ?? throw new ArgumentNullException(nameof(messageService));

            // 根据配置初始化机架与插槽，并注册 slot 参数
            InitializeRacksFromConfig();
        }

        public IRackManager Racks => _rackManager;
        public ISlotManager Slots => _slotManager;
        public IServiceRegistry Services => _services;

        // 内部类型：模块条目封装模块实例与其上下文
        private class HostedModule
        {
            public IModule Module { get; }
            public DefaultModuleContext Context { get; }
            public List<IDisposable> AutoRegistrations { get; } = new();

            public HostedModule(IModule module, DefaultModuleContext context)
            {
                Module = module;
                Context = context;
            }
        }

        private void InitializeRacksFromConfig()
        {
            if (_config == null || _config.Racks == null || _config.Racks.Count == 0) return;

            foreach (var rackCfg in _config.Racks)
            {
                try
                {
                    var slotCount = Math.Max(rackCfg.SlotCount, rackCfg.Slots?.Count ?? 0);
                    var rack = _rackManager.CreateRack(rackCfg.Name, Math.Max(slotCount, 1));

                    if (rackCfg.Slots != null && rackCfg.Slots.Count > 0)
                    {
                        foreach (var slotCfg in rackCfg.Slots)
                        {
                            if (slotCfg == null) continue;

                            // prefer explicit id from config if provided; fallback to count if negative
                            var configuredId = slotCfg.Id >= 0 ? slotCfg.Id : -1;
                            var slotNameFromCfg = !string.IsNullOrEmpty(slotCfg.Name) ? slotCfg.Name : null;
                            ISlot? slot = null;

                            // 1) 优先按 id 查找（如果配置里提供了 id）
                            if (configuredId >= 0)
                            {
                                slot = rack.GetSlot(configuredId);
                                if (slot != null && !string.IsNullOrEmpty(slotNameFromCfg))
                                {
                                    // 更新显示标签（name）以匹配配置
                                    (slot as SimpleSlot)?.SetName(slotNameFromCfg);
                                }
                            }

                            // 2) 若未按 id 找到，尝试按 name 查找（兼容旧风格）
                            if (slot == null && !string.IsNullOrEmpty(slotNameFromCfg))
                            {
                                slot = rack.GetSlot(slotNameFromCfg);
                            }

                            // 3) 若还未找到且 rack 支持创建（且配置提供 id 或我们允许按顺序创建），则创建槽
                            if (slot == null && rack is SimpleRack sr)
                            {
                                // 决定使用的 id：
                                // - 如果配置提供了 id 则使用它；
                                // - 否则如果 name 对应的 id 不存在，则使用当前最大 id+1（保持连续，但不会 collide）。
                                var createId = configuredId >= 0 ? configuredId : (sr.AllSlots.Any() ? sr.AllSlots.Max(s => (s as SimpleSlot)?.Id ?? 0) + 1 : 0);
                                var createName = slotNameFromCfg ?? $"slot-{createId}";

                                // 如果 id 已存在（并发或先前创建），取回现有并更新 name
                                var existing = sr.GetSlot(createId);
                                if (existing != null)
                                {
                                    slot = existing;
                                    (slot as SimpleSlot)?.SetName(createName);
                                }
                                else
                                {
                                    slot = sr.CreateSlot(createId, createName, rack);
                                    Console.WriteLine($"[DEBUG] InitializeRacksFromConfig: created slot id={createId}, name={createName} on rack={rackCfg.Name}");
                                }
                            }

                            if (slot == null) continue;

                            // 注册 slot 参数到 registry：使用 slot.Id（若实现为 SimpleSlot）为主键
                            if (slotCfg.Parameters != null)
                            {
                                foreach (var kv in slotCfg.Parameters)
                                {
                                    try
                                    {
                                        var sid = (slot as SimpleSlot)?.Id ?? -1;
                                        if (sid >= 0)
                                            _services.Register(rackCfg.Name, sid, kv.Key, kv.Value ?? string.Empty);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[WARN] 注册插槽参数到 registry 失败: {rackCfg.Name}/{slot.Name} - {ex.Message}");
                                    }
                                }
                            }

                            try
                            {
                                var sid = (slot as SimpleSlot)?.Id ?? -1;
                                if (sid >= 0)
                                    _services.Register(rackCfg.Name, sid, "slot.name", slot.Name);
                            }
                            catch { /* ignore */ }
                        }
                    }
                    else if (rackCfg.SlotCount > 0)
                    {
                        // 对于只指定 slotCount 的情况：确保 id 0..slotCount-1 的槽存在并设置默认 name
                        if (rack is SimpleRack sr)
                        {
                            for (int i = 0; i < rackCfg.SlotCount; i++)
                            {
                                var defaultName = $"slot-{i}";
                                var slot = sr.GetSlot(i) ?? sr.CreateSlot(i, defaultName, rack);
                                (slot as SimpleSlot)?.SetName(defaultName);

                                try
                                {
                                    _services.Register(rackCfg.Name, (slot as SimpleSlot)?.Id ?? -1, "slot.name", defaultName);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            // 如果 rack 不支持按 id 创建，则按现有行为按名称创建（较少见）
                            for (int i = 0; i < rackCfg.SlotCount; i++)
                            {
                                var defaultName = $"slot-{i}";
                                var slot = rack.GetSlot(defaultName);
                                if (slot == null) continue;
                                try
                                {
                                    _services.Register(rackCfg.Name, (slot as SimpleSlot)?.Id ?? -1, "slot.name", defaultName);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] 初始化机架失败: {rackCfg.Name} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 运行容器并阻塞直到 Stop 被调用。
        /// Run the host and block until Stop is called.
        /// </summary>
        public void Run()
        {
            Console.WriteLine("Container running. Press Ctrl+C to stop. / Container running.");

            // 捕获 Ctrl+C，触发 Stop（且不立即终止进程）
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("Cancel requested, stopping container...");
                Stop();
            };

            try
            {
                // 阻塞直到 Stop() 被调用（通过 CancellationToken 信号）
                _cts.Token.WaitHandle.WaitOne();
            }
            finally
            {
                // 优雅停止：先尝试拔出所有模块，再 Stop
                var moduleIds = _modules.Keys.ToList();
                foreach (var id in moduleIds)
                {
                    try
                    {
                        UnplugModule(id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 卸载模块 {id} 失败: {ex.Message}");
                    }
                }

                foreach (var entry in _modules.Values.ToList())
                {
                    try
                    {
                        entry.Module.Stop();
                        // dispose any auto-registrations remaining
                        foreach (var d in entry.AutoRegistrations) try { d.Dispose(); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 停止模块 {entry.Module.Name} 失败: {ex.Message}");
                    }
                }

                Console.WriteLine("Container stopped.");
            }
        }

        public void Stop()
        {
            try
            {
                if (!_cts.IsCancellationRequested)
                    _cts.Cancel();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Stop 触发取消时异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 从配置批量加载模块（兼容遗留 slotIndex 配置：会转换为 slotId）。
        /// Load modules from configuration (legacy slotIndex converted to slotId).
        /// </summary>
        public void LoadModules()
        {
            if (_config.Modules == null || _config.Modules.Count == 0)
            {
                Console.WriteLine("配置中没有找到模块 / No modules configured");
                return;
            }

            foreach (var cfg in _config.Modules)
            {
                if (!cfg.Enabled)
                {
                    Console.WriteLine($"跳过禁用模块: {cfg.Name} / Skipping disabled module: {cfg.Name}");
                    continue;
                }

                try
                {
                    var moduleId = LoadModule(cfg.Path);
                    if (!string.IsNullOrEmpty(moduleId))
                    {
                        Console.WriteLine($"已加载模块: {cfg.Name} ({moduleId}) / Loaded module");

                        // 兼容：优先接受 slotId 字段（整数或字符串数字），也兼容老 slotIndex 字段
                        if (cfg.Configuration != null &&
                            cfg.Configuration.TryGetValue("rack", out var rackObj) &&
                            (cfg.Configuration.TryGetValue("slotId", out var slotIdObj) || cfg.Configuration.TryGetValue("slotIndex", out slotIdObj)))
                        {
                            var rackName = rackObj?.ToString() ?? string.Empty;
                            if (int.TryParse(slotIdObj?.ToString(), out var slotId))
                            {
                                // optional slotName is just a label, not used to create slot
                                var slotName = cfg.Configuration.TryGetValue("slotName", out var slotNameObj) ? slotNameObj?.ToString() : null;
                                var ok = PlugModuleToSlot(moduleId, rackName, slotId, slotName);
                                Console.WriteLine(ok
                                    ? $"Module {moduleId} plugged to {rackName}/id:{slotId}"
                                    : $"Module {moduleId} failed to plug to {rackName}/id:{slotId} (slot must pre-exist)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 批量加载模块失败: {cfg.Name}, 路径: {cfg.Path} / Failed to load module: {ex}");
                }
            }

            Console.WriteLine($"total {_modules.Count} modules");
        }

        /// <summary>
        /// 单模块加载：实例化模块并 Initialize/Start（不自动插槽）。
        /// Load a single module: instantiate, Initialize and Start (does not auto-plug).
        /// </summary>
        public string LoadModule(string modulePath)
        {
            if (string.IsNullOrWhiteSpace(modulePath))
            {
                Console.WriteLine("LoadModule: modulePath 为空 / modulePath is empty");
                return string.Empty;
            }

            try
            {
                var resolvedPath = modulePath;
                if (!Path.IsPathRooted(resolvedPath))
                {
                    resolvedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, resolvedPath));
                }

                if (!File.Exists(resolvedPath))
                {
                    Console.WriteLine($"模块文件不存在，跳过: {resolvedPath} / Module file not found, skipping");
                    return string.Empty;
                }

                var moduleInstance = ModuleLoader.Load(resolvedPath);
                if (moduleInstance == null)
                {
                    Console.WriteLine($"未能实例化模块: {resolvedPath} / Failed to instantiate module");
                    return string.Empty;
                }

                // 先生成 moduleId，再构造 DefaultModuleContext（新的构造函数需要 moduleInstanceId）
                var moduleId = $"{moduleInstance.Name}_{Guid.NewGuid():N}".Substring(0, 20);
                var moduleContext = new DefaultModuleContext(this, moduleId, _tagService, _messageService, _services);

                // 保存模块条目（保证在 Initialize/Declare 时能通过 moduleId 找到上下文/状态）
                var hosted = new HostedModule(moduleInstance, moduleContext);
                _modules[moduleId] = hosted;

                // 调用模块初始化（模块的 DeclareProducer/DeclareConsumer 会通过 Context 立即注册）
                moduleInstance.Initialize(moduleContext);

                // 使用 TagAutoRegistrar 进行集中注册并自动建立订阅/发布 wiring（幂等）
                try
                {
                    var disposables = TagAutoRegistrar.RegisterAndWire(moduleInstance, moduleContext, _tagRegistrationManager, _tagService);
                    if (disposables != null && disposables.Count > 0)
                    {
                        hosted.AutoRegistrations.AddRange(disposables);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] TagAutoRegistrar failed for module {moduleId}: {ex.Message}");
                }

                // 启动模块（Start 可能会立即进入 Execute 循环）
                moduleInstance.Start();

                return moduleId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 加载模块失败: {modulePath} / Failed to load module: {ex}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 将已加载的模块插入到指定的 rack/slot（按 slotId 查找或创建）。
        /// Plug a loaded module into the slot identified by slotId (lookup by id).
        /// 若槽不存在且机架支持创建（SimpleRack），则以 slotId 创建新槽，name 可选用于标签。
        /// </summary>
        public bool PlugModuleToSlot(string moduleId, string rackName, int slotId, string? slotName = null)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            if (!_modules.TryGetValue(moduleId, out var entry))
            {
                Console.WriteLine($"[DEBUG] PlugModuleToSlot: moduleId not found: {moduleId}");
                return false;
            }

            Console.WriteLine($"[DEBUG] PlugModuleToSlot: try plug moduleId={moduleId}, moduleType={entry.Module.GetType().FullName}, rack={rackName}, slotId={slotId}, slotName={slotName}");

            // 1) 检查机架是否存在（严格，不创建）
            var rack = Racks.GetRack(rackName);
            if (rack == null)
            {
                Console.WriteLine($"[ERROR] PlugModuleToSlot: rack '{rackName}' not found (will NOT create).");
                return false;
            }

            // 2) 按 id 查找插槽（严格，不创建）
            ISlot? slot = null;
            try
            {
                slot = rack.GetSlot(slotId);
            }
            catch
            {
                // 如果实现抛出，则尝试按名称解析（兼容），但不会创建
                if (!string.IsNullOrEmpty(slotName))
                {
                    slot = rack.GetSlot(slotName);
                }
            }

            if (slot == null)
            {
                Console.WriteLine($"[ERROR] PlugModuleToSlot: slot id {slotId} on rack '{rackName}' not found (will NOT create).");
                return false;
            }

            // 3) 插入
            var slotIdLog = (slot as Tiema.Runtime.Models.SimpleSlot)?.Id ?? -1;
            Console.WriteLine($"[DEBUG] PlugModuleToSlot: found slot Name={slot.Name}, Id={slotIdLog}, IsOccupied={slot.IsOccupied}");

            lock (slot)
            {
                if (!slot.Plug(entry.Module))
                {
                    Console.WriteLine($"[WARN] 插入模块失败，插槽已被占用: {rackName}/id:{slotId}");
                    return false;
                }
            }

            // 4) 设置上下文并通知模块
            try { entry.Context.SetCurrentSlot(slot); } catch (Exception ex) { Console.WriteLine($"[ERROR] 设置模块 Context.CurrentSlot 失败: {ex}"); }

            try
            {
                entry.Module.OnPlugged(slot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 模块 {entry.Module.Name} OnPlugged 异常: {ex}");
            }

            return true;
        }

        /// <summary>
        /// 将模块从所在插槽拔出。
        /// Unplug module from its current slot.
        /// </summary>
        public bool UnplugModule(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            if (!_modules.TryGetValue(moduleId, out var entry)) return false;

            // 从 ModuleContext 获取当前插槽（接口契约：CurrentSlot 可能在未插入时抛异常）
            ISlot slot;
            try
            {
                slot = entry.Context.CurrentSlot;
            }
            catch (Exception)
            {
                // 如果 CurrentSlot 不可用，则回退到尝试通过模块保存的 CurrentSlot（若模块继承 ModuleBase）
                // 但优先以 context 为准；若确实没有插槽，则无法拔出
                Console.WriteLine($"[WARN] 模块 {entry.Module.Name} 当前无插槽信息 / no current slot info");
                return false;
            }

            // 通知模块拔出（接口调用，支持任意实现）
            try
            {
                entry.Module.OnUnplugged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 模块 {entry.Module.Name} OnUnplugged 异常: {ex}");
            }

            // 清理上下文中的当前插槽引用
            entry.Context.SetCurrentSlot(null);

            // Dispose auto-registrations created for this module (subscriptions, tasks...)
            try
            {
                foreach (var d in entry.AutoRegistrations)
                {
                    try { d.Dispose(); } catch { }
                }
                entry.AutoRegistrations.Clear();
            }
            catch { /* best effort */ }

            // 卸载插槽中的模块引用
            try
            {
                lock (slot)
                {
                    slot.Unplug();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 卸载插槽失败: {ex}");
            }

            return true;
        }
    }
}