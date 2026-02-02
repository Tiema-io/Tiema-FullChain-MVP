using System;
using System.Collections.Concurrent;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;


namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 线程安全的内存 ServiceRegistry 实现：以 (rack,slotId,service) 精确键存储服务。
    /// 支持通过 slotId 直接查找，也支持通过 slotName 的便捷查找（通过注入的 IRackManager 将 slotName 映射为 slotId）。
    /// Thread-safe in-memory service registry storing services by exact (rack,slotId,service) key.
    /// Also supports convenience lookup by slot name via an injected IRackManager resolver.
    /// </summary>
    public class SimpleServiceRegistry : IServiceRegistry
    {
        private readonly Dictionary<(string rackName, int slotId, string serviceName), object> _services
          = new();
        private readonly IRackManager? _rackManager; // 可选，用于从 slotName 映射到 slotId

        // 宿主级服务（不绑定具体 slot），例如 Tag、Message、Backplane、RegistrationManager 等。
        // Host-level services (not bound to a specific slot), e.g. Tag, Message, Backplane, RegistrationManager.
        public BuiltInTagService TagService { get; }
        public BuiltInMessageService MessageService { get; }
        public InMemoryBackplane Backplane { get; }
        public InMemoryTagRegistrationManager TagRegistrationManager { get; }

        public SimpleServiceRegistry(IRackManager? rackManager = null)
        {    // 初始化核心服务实例 / Initialize core services.
          

            _rackManager = rackManager;
        }
    



        // Key 格式："{rack}::{slotId}::{serviceName}"
        // Key format: "{rack}::{slotId}::{serviceName}"
        private static string Key(string rack, int slotId, string name) =>
            $"{(rack ?? "null")}::{slotId}::{(name ?? "default")}";
        /// <summary>
        /// 服务注册
        /// Register a service under exact (rackName, slotId, serviceName) key.
        /// </summary>
        /// <param name="rackName">机架名称 / rack name</param>
        /// <param name="slotId">插槽 Id（不可变整数标识）/ slot id (immutable int identifier)</param>
        /// <param name="serviceName">服务名称 / service name</param>
        /// <param name="implementation">服务实例 / implementation</param>
        public void Register(string rackName, int slotId, string serviceName, object implementation)
        {
            if (serviceName == null) throw new ArgumentNullException(nameof(serviceName));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            _services[(rackName, slotId, serviceName)] = implementation!;
        }

        /// <summary>
        /// 服务注销
        /// Unregister a service for the exact key if exists.
        /// </summary>
        /// <param name="rackName">机架名称 / rack name</param>
        /// <param name="slotId">插槽 Id / slot id</param>
        /// <param name="serviceName">服务名称 / service name</param>
        /// <returns>是否成功 / true if removed</returns>
        public bool Unregister(string rackName, int slotId, string serviceName)
        {
            if (serviceName == null) throw new ArgumentNullException(nameof(serviceName));
            return _services.Remove((rackName, slotId, serviceName));
        }

        /// <summary>
        /// 尝试按精确键解析服务（返回 true 并输出实例）。
        /// Try to resolve a service by exact (rackName, slotId, serviceName).
        /// </summary>
        /// <param name="rackName">机架名称 / rack name</param>
        /// <param name="slotId">插槽 Id / slot id</param>
        /// <param name="serviceName">服务名称 / service name</param>
        /// <param name="instance">输出实例或 null / out instance or null</param>
        /// <typeparam name="T">期望类型 / expected type</typeparam>
        /// <returns>是否找到 / true if found</returns>
        public bool TryGet<T>(string rackName, int slotId, string serviceName, out T? instance) where T : class
        {
            instance = null;
            if (serviceName == null) return false;

            // 修正：将 slotId 转为 string，与字典键类型一致 / Fix: convert slotId to string to match dictionary key type
            if (_services.TryGetValue((rackName, slotId, serviceName), out var obj) && obj is T t)
            {
                instance = t;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 按精确键解析服务，找不到返回 null。
        /// Get a service by exact (rackName, slotId, serviceName); returns null if not found.
        /// </summary>
        /// <typeparam name="T">期望类型 / expected type</typeparam>
        /// <param name="rackName">机架名称 / rack name</param>
        /// <param name="slotId">插槽 Id / slot id</param>
        /// <param name="serviceName">服务名称 / service name</param>
        /// <returns>实例或 null / instance or null</returns>
        public T? Get<T>(string rackName, int slotId, string serviceName) where T : class
        {
            TryGet<T>(rackName, slotId, serviceName, out var inst);
            return inst;
        }


        public T? GetHostService<T>(string serviceName) where T : class
        {
            return Get<T>(string.Empty, -1, serviceName);
        }

        // --------------------------------------------------------------------
        // Convenience: lookup by slot name (string). Host implementation uses rackManager to map slotName -> slotId.
        // --------------------------------------------------------------------

        /// <summary>
        /// 尝试按机架名 + 插槽名称（字符串） + 服务名解析服务，返回 true 并输出实例。
        /// If rackManager is not provided, this will attempt to parse slotName as int.
        /// </summary>
        public bool TryGetBySlotName<T>(string rackName, string slotName, string serviceName, out T? instance) where T : class
        {
            instance = null;
            if (serviceName == null) return false;

            try
            {
                // 1) 如果 slotName 可以解析为整数，直接当作 slotId
                if (int.TryParse(slotName, out var parsedId))
                {
                    return TryGet<T>(rackName, parsedId, serviceName, out instance);
                }

                // 2) 否则尝试通过注入的 IRackManager 映射 slotName -> slotId
                if (_rackManager != null)
                {
                    var rack = _rackManager.GetRack(rackName);
                    if (rack != null)
                    {
                        var slot = rack.GetSlot(slotName);
                        if (slot != null)
                        {
                            return TryGet<T>(rackName, slot.Id, serviceName, out instance);
                        }
                    }
                }

                // 3) 未能解析
                return false;
            }
            catch
            {
                // 吞掉异常，返回 false（调用方应处理失败情形）
                instance = null;
                return false;
            }
        }

        /// <summary>
        /// 通过机架名 + 插槽名称（字符串） + 服务名解析服务，找不到返回 null。
        /// Convenience wrapper for TryGetBySlotName.
        /// </summary>
        public T? GetBySlotName<T>(string rackName, string slotName, string serviceName) where T : class
        {
            TryGetBySlotName<T>(rackName, slotName, serviceName, out var inst);
            return inst;
        }
    }
}