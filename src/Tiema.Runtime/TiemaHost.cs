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
    /// TiemaHost: core runtime hosting racks/slots/plugins and unified service registry.
    /// </summary>
    public class TiemaHost : IPluginHost
    {
        private readonly Dictionary<string, HostedPlugin> _plugins = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly TiemaConfig _config;

        // Rack/slot managers (in-memory)
        private readonly IRackManager _rackManager;
        private readonly ISlotManager _slotManager;

        // Unified host-level service registry
        private readonly SimpleServiceRegistry _services;

        // Core runtime services: Tag / Message / Registration / Backplane
        private readonly ITagRegistrationManager _tagRegistrationManager;
        private readonly IBackplane _backplane;
        private readonly ITagService _tagService;
        private readonly IMessageService _messageService;

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

            InitializeRacksFromConfig();
        }

        public IRackManager Racks => _rackManager;
        public ISlotManager Slots => _slotManager;
        public IServiceRegistry Services => _services;

        // Internal container entry for a hosted plugin
        private class HostedPlugin
        {
            public IPlugin Plugin { get; }
            public DefaultPluginContext Context { get; }
            public List<IDisposable> AutoRegistrations { get; } = new();

            public HostedPlugin(IPlugin plugin, DefaultPluginContext context)
            {
                Plugin = plugin;
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

                            var configuredId = slotCfg.Id >= 0 ? slotCfg.Id : -1;
                            var slotNameFromCfg = !string.IsNullOrEmpty(slotCfg.Name) ? slotCfg.Name : null;
                            ISlot? slot = null;

                            if (configuredId >= 0)
                            {
                                slot = rack.GetSlot(configuredId);
                                if (slot != null && !string.IsNullOrEmpty(slotNameFromCfg))
                                {
                                    (slot as SimpleSlot)?.SetName(slotNameFromCfg);
                                }
                            }

                            if (slot == null && !string.IsNullOrEmpty(slotNameFromCfg))
                            {
                                slot = rack.GetSlot(slotNameFromCfg);
                            }

                            if (slot == null && rack is SimpleRack sr)
                            {
                                var createId = configuredId >= 0 ? configuredId : (sr.AllSlots.Any() ? sr.AllSlots.Max(s => (s as SimpleSlot)?.Id ?? 0) + 1 : 0);
                                var createName = slotNameFromCfg ?? $"slot-{createId}";

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
                                        Console.WriteLine($"[WARN] Register slot param failed: {rackCfg.Name}/{slot.Name} - {ex.Message}");
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
                    Console.WriteLine($"[WARN] Initialize rack failed: {rackCfg.Name} - {ex.Message}");
                }
            }
        }

        // Run the host and block until Stop is called.
        public void Run()
        {
            Console.WriteLine("Container running. Press Ctrl+C to stop.");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("Cancel requested, stopping container...");
                Stop();
            };

            try
            {
                _cts.Token.WaitHandle.WaitOne();
            }
            finally
            {
                var pluginIds = _plugins.Keys.ToList();
                foreach (var id in pluginIds)
                {
                    try
                    {
                        UnplugPlugin(id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Unplug plugin {id} failed: {ex.Message}");
                    }
                }

                foreach (var entry in _plugins.Values.ToList())
                {
                    try
                    {
                        entry.Plugin.Stop();
                        foreach (var d in entry.AutoRegistrations) try { d.Dispose(); } catch { }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Stop plugin {entry.Plugin.Name} failed: {ex.Message}");
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
                Console.WriteLine($"[ERROR] Stop cancellation error: {ex.Message}");
            }
        }

        // Load plugins from configuration (legacy slotIndex converted to slotId).
        public void LoadPlugins()
        {
            if (_config.Plugins == null || _config.Plugins.Count == 0)
            {
                Console.WriteLine("No plugins configured");
                return;
            }

            foreach (var cfg in _config.Plugins)
            {
                if (!cfg.Enabled)
                {
                    Console.WriteLine($"Skipping disabled plugin: {cfg.Name}");
                    continue;
                }

                try
                {
                    var pluginId = LoadPlugin(cfg.Path);
                    if (!string.IsNullOrEmpty(pluginId))
                    {
                        Console.WriteLine($"Loaded plugin: {cfg.Name} ({pluginId})");

                        if (cfg.Configuration != null &&
                            cfg.Configuration.TryGetValue("rack", out var rackObj) &&
                            (cfg.Configuration.TryGetValue("slotId", out var slotIdObj) || cfg.Configuration.TryGetValue("slotIndex", out slotIdObj)))
                        {
                            var rackName = rackObj?.ToString() ?? string.Empty;
                            if (int.TryParse(slotIdObj?.ToString(), out var slotId))
                            {
                                var slotName = cfg.Configuration.TryGetValue("slotName", out var slotNameObj) ? slotNameObj?.ToString() : null;
                                var ok = PlugPluginToSlot(pluginId, rackName, slotId, slotName);
                                Console.WriteLine(ok
                                    ? $"Plugin {pluginId} plugged to {rackName}/id:{slotId}"
                                    : $"Plugin {pluginId} failed to plug to {rackName}/id:{slotId} (slot must pre-exist)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Load plugin failed: {cfg.Name}, path: {cfg.Path} - {ex}");
                }
            }

            Console.WriteLine($"total {_plugins.Count} plugins");
        }

        // Load a single plugin: instantiate, Initialize and Start (does not auto-plug).
        public string LoadPlugin(string pluginPath)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
            {
                Console.WriteLine("LoadPlugin: pluginPath is empty");
                return string.Empty;
            }

            try
            {
                var resolvedPath = pluginPath;
                if (!Path.IsPathRooted(resolvedPath))
                {
                    resolvedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, resolvedPath));
                }

                if (!File.Exists(resolvedPath))
                {
                    Console.WriteLine($"Plugin file not found, skipping: {resolvedPath}");
                    return string.Empty;
                }

                var pluginInstance = PluginLoader.Load(resolvedPath);
                if (pluginInstance == null)
                {
                    Console.WriteLine($"Failed to instantiate plugin: {resolvedPath}");
                    return string.Empty;
                }

                var pluginId = $"{pluginInstance.Name}_{Guid.NewGuid():N}".Substring(0, 20);
                var pluginContext = new DefaultPluginContext(this, pluginId, _tagService, _messageService, _services);

                var hosted = new HostedPlugin(pluginInstance, pluginContext);
                _plugins[pluginId] = hosted;

                pluginInstance.Initialize(pluginContext);

                try
                {
                    var disposables = TagAutoRegistrar.RegisterAndWire(pluginInstance, pluginContext, _tagRegistrationManager, _tagService);
                    if (disposables != null && disposables.Count > 0)
                    {
                        hosted.AutoRegistrations.AddRange(disposables);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] TagAutoRegistrar failed for plugin {pluginId}: {ex.Message}");
                }

                pluginInstance.Start();

                return pluginId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Load plugin failed: {pluginPath} - {ex}");
                return string.Empty;
            }
        }

        // Plug a loaded plugin into a slot identified by slotId (lookup by id).
        public bool PlugPluginToSlot(string pluginId, string rackName, int slotId, string? slotName = null)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;
            if (!_plugins.TryGetValue(pluginId, out var entry))
            {
                Console.WriteLine($"[DEBUG] PlugPluginToSlot: pluginId not found: {pluginId}");
                return false;
            }

            Console.WriteLine($"[DEBUG] PlugPluginToSlot: try plug pluginId={pluginId}, type={entry.Plugin.GetType().FullName}, rack={rackName}, slotId={slotId}, slotName={slotName}");

            var rack = Racks.GetRack(rackName);
            if (rack == null)
            {
                Console.WriteLine($"[ERROR] PlugPluginToSlot: rack '{rackName}' not found");
                return false;
            }

            ISlot? slot = null;
            try
            {
                slot = rack.GetSlot(slotId);
            }
            catch
            {
                if (!string.IsNullOrEmpty(slotName))
                {
                    slot = rack.GetSlot(slotName);
                }
            }

            if (slot == null)
            {
                Console.WriteLine($"[ERROR] PlugPluginToSlot: slot id {slotId} on rack '{rackName}' not found");
                return false;
            }

            var slotIdLog = (slot as Tiema.Runtime.Models.SimpleSlot)?.Id ?? -1;
            Console.WriteLine($"[DEBUG] PlugPluginToSlot: found slot Name={slot.Name}, Id={slotIdLog}, IsOccupied={slot.IsOccupied}");

            lock (slot)
            {
                if (!slot.Plug(entry.Plugin))
                {
                    Console.WriteLine($"[WARN] Plug failed, slot occupied: {rackName}/id:{slotId}");
                    return false;
                }
            }

            try { entry.Context.SetCurrentSlot(slot); } catch (Exception ex) { Console.WriteLine($"[ERROR] Set Context.CurrentSlot failed: {ex}"); }

            try
            {
                entry.Plugin.OnPlugged(slot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Plugin {entry.Plugin.Name} OnPlugged error: {ex}");
            }

            return true;
        }

        // Unplug plugin from its current slot.
        public bool UnplugPlugin(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return false;
            if (!_plugins.TryGetValue(pluginId, out var entry)) return false;

            ISlot slot;
            try
            {
                slot = entry.Context.CurrentSlot;
            }
            catch (Exception)
            {
                Console.WriteLine($"[WARN] Plugin {entry.Plugin.Name} has no current slot info");
                return false;
            }

            try
            {
                entry.Plugin.OnUnplugged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Plugin {entry.Plugin.Name} OnUnplugged error: {ex}");
            }

            entry.Context.SetCurrentSlot(null);

            try
            {
                foreach (var d in entry.AutoRegistrations)
                {
                    try { d.Dispose(); } catch { }
                }
                entry.AutoRegistrations.Clear();
            }
            catch { /* best effort */ }

            try
            {
                lock (slot)
                {
                    slot.Unplug();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unplug slot failed: {ex}");
            }

            return true;
        }
    }
}