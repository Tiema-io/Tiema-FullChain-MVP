using System;

namespace Tiema.Contracts
{
    /// <summary>
    /// 统一的服务注册表（Host 级别存储）。
    /// 所有服务通过 (rackName, slotId, serviceName) 精确键注册与查找。
    /// Unified host-level service registry. Services are keyed by (rackName, slotId, serviceName).
    /// </summary>
    public interface IServiceRegistry
    {
        /// <summary>
        /// 在宿主注册表中以精确键注册服务实例（会覆盖同名服务）。
        /// Register a service under exact (rackName, slotId, serviceName) key. Overwrites existing.
        /// </summary>
        /// <param name="rackName">机架名称 / rack name</param>
        /// <param name="slotId">插槽不变 Id（整数）/ immutable slot id (int)</param>
        /// <param name="serviceName">服务名称 / service name</param>
        /// <param name="implementation">服务实例 / implementation</param>
        void Register(string rackName, int slotId, string serviceName, object implementation);

        /// <summary>
        /// 取消注册指定键的服务（如果存在）。
        /// Unregister a service for the exact key if exists.
        /// </summary>
        /// <returns>是否成功 / true if removed</returns>
        bool Unregister(string rackName, int slotId, string serviceName);

        /// <summary>
        /// 尝试按精确键解析服务（返回 true 并输出实例）。
        /// Try to resolve a service by exact (rackName, slotId, serviceName).
        /// </summary>
        bool TryGet<T>(string rackName, int slotId, string serviceName, out T? instance) where T : class;

        /// <summary>
        /// 按精确键解析服务，找不到返回 null。
        /// Get a service by exact (rackName, slotId, serviceName); returns null if not found.
        /// </summary>
        T? Get<T>(string rackName, int slotId, string serviceName) where T : class;

        // --------------------------------------------------------------------
        // 兼容便捷方法：按 slotName（字符串标签）查找（不改变主键设计，宿主实现需将 slotName -> slotId 映射后查找）
        // Convenience lookup by slot name (string). Host implementation should resolve slotName -> slotId internally.
        // --------------------------------------------------------------------

        /// <summary>
        /// 尝试按机架名 + 插槽名称（字符串） + 服务名解析服务，返回 true 并输出实例。
        /// Try to resolve a service by (rackName, slotName, serviceName). Host implementation should map slotName -> slotId.
        /// </summary>
        bool TryGetBySlotName<T>(string rackName, string slotName, string serviceName, out T? instance) where T : class;

        /// <summary>
        /// 通过机架名 + 插槽名称（字符串） + 服务名解析服务，找不到返回 null。
        /// Get a service by (rackName, slotName, serviceName); host maps slotName -> slotId internally.
        /// </summary>
        T? GetBySlotName<T>(string rackName, string slotName, string serviceName) where T : class;
    }
}