using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Tiema.Abstractions;
using Tiema.Runtime.Models;
using Tiema.Runtime.Services;

namespace Tiema.Runtime
{
    /// <summary>
    /// Tiema 容器：运行时主体，管理机架/插槽/模块与统一 ServiceRegistry。
    /// Tiema container: core runtime hosting racks/slots/modules and unified service registry.
    /// </summary>
    public class TiemaContainer : IModuleHost
    {
        private readonly Dictionary<string, ModuleEntry> _modules = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly TiemaConfig _config;

        private readonly ITagService _tag_service;
        private readonly IMessageService _message_service;

        // 机架管理器与插槽管理器（内存实现）
        // rack manager and slot manager (in-memory)
        private readonly IRackManager _rackManager = new InMemoryRackManager();
        private readonly ISlotManager _slotManager;
        // 统一的宿主级 ServiceRegistry（注入 rackManager 以支持按 slot name 查找）
        // unified host-level service registry (injected with rackManager to resolve slot name)
        private readonly IServiceRegistry _serviceRegistry;

        public TiemaContainer(
            TiemaConfig config,
            ITagService tagService,
            IMessageService messageService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tag_service = tagService ?? throw new ArgumentNullException(nameof(tagService));
            _message_service = messageService ?? throw new ArgumentNullException(nameof(messageService));

            _slotManager = new InMemorySlotManager(_rackManager);

            // 使用能按 slotName 映射的 registry 实现（便于兼容 name 查询）
            // Use registry that can resolve slotName -> slotId for convenience lookups
            _serviceRegistry = new SimpleServiceRegistry(_rackManager);

            // 从配置预建机架与插槽，并把 slot 参数注册到 registry（使用 slot.Id）
            // Pre-create racks/slots from config and register slot parameters into registry (using slot.Id)
            InitializeRacksFromConfig();
        }

        public ITagService Tags => _tag_service;
        public IMessageService Messages => _message_service;
        public IRackManager Racks => _rackManager;
        public ISlotManager Slots => _slotManager;
        public IServiceRegistry Services => _serviceRegistry;

        // 内部类型：模块条目封装模块实例与其上下文
        private class ModuleEntry
        {
            public IModule Module { get; }
            public DefaultModuleContext Context { get; }

            public ModuleEntry(IModule module, DefaultModuleContext context)
            {
                Module = module;
                Context = context;
            }
        }

        // 简单的内存机架管理器（按名称存放机架）
        private class InMemoryRackManager : IRackManager
        {
            private readonly Dictionary<string, IRack> _racks = new(StringComparer.OrdinalIgnoreCase);

            public IRack CreateRack(string name, int slotCount)
            {
                if (_racks.ContainsKey(name)) return _racks[name];
                var r = new SimpleRack(name, slotCount);
                _racks[name] = r;
                return r;
            }

            public IRack GetRack(string name) => _racks.TryGetValue(name, out var r) ? r : null;

            public IEnumerable<IRack> AllRacks => _racks.Values;
        }

        // 插槽管理器：路径格式 "rackName/slotName"（不再默认使用 index）
        private class InMemorySlotManager : ISlotManager
        {
            private readonly IRackManager _rackManager;

            public InMemorySlotManager(IRackManager rackManager)
            {
                _rackManager = rackManager;
            }

            /// <summary>
            /// 通过路径获取插槽，路径格式为 "rackName/slotName"。
            /// 若 createIfNotExist 为 true 且 rack 为 SimpleRack，实现会尝试创建名为 slotName 的插槽。
            /// Path format: "rackName/slotName". If createIfNotExist and rack is SimpleRack, attempt to create named slot.
            /// </summary>
            public ISlot GetSlot(string path, bool createIfNotExist = false)
            {
                if (string.IsNullOrEmpty(path)) return null;

                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var rackName = parts[0];
                var slotName = parts.Length > 1 ? parts[1] : null;

                var rack = _rackManager.GetRack(rackName);
                if (rack == null)
                {
                    if (!createIfNotExist) return null;
                    // 如果机架不存在，创建一个最小机架
                    rack = _rackManager.CreateRack(rackName, 1);
                }

                ISlot slot = null;
                if (!string.IsNullOrEmpty(slotName))
                {
                    slot = rack.GetSlot(slotName);
                }

                // 若需要创建并且还未找到，则尝试在 SimpleRack 上创建
                if (slot == null && createIfNotExist && !string.IsNullOrEmpty(slotName))
                {
                    if (rack is SimpleRack sr)
                    {
                        // 使用当前槽数作为新槽的顺序位置（仅用于创建，Id/Name 由 SimpleSlot 管理）
                        var insertIndex = sr.AllSlots.Count();
                        slot = sr.CreateSlot(insertIndex, slotName, rack);
                    }
                }

                return slot;
            }

            public IReadOnlyList<ISlot> AllSlots => _rackManager.AllRacks.SelectMany(r => r.AllSlots).ToList();
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
                                            _serviceRegistry.Register(rackCfg.Name, sid, kv.Key, kv.Value ?? string.Empty);
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
                                    _serviceRegistry.Register(rackCfg.Name, sid, "slot.name", slot.Name);
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
                                    _serviceRegistry.Register(rackCfg.Name, (slot as SimpleSlot)?.Id ?? -1, "slot.name", defaultName);
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
                                    _serviceRegistry.Register(rackCfg.Name, (slot as SimpleSlot)?.Id ?? -1, "slot.name", defaultName);
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

                var moduleContext = new DefaultModuleContext(this);

                var moduleId = $"{moduleInstance.Name}_{Guid.NewGuid():N}".Substring(0, 20);
                _modules[moduleId] = new ModuleEntry(moduleInstance, moduleContext);

                moduleInstance.Initialize(moduleContext);
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