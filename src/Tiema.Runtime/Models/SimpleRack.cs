using System;
using System.Collections.Generic;
using System.Linq;

using Tiema.Contracts;

namespace Tiema.Runtime.Models
{
    /// <summary>
    /// 简单机架实现：以 int Id 为插槽主键（Dictionary<int, ISlot>），并维护 name->id 的映射以便按名称查找。
    /// Simple rack implementation: uses int Id as slot key (Dictionary<int, ISlot>) and keeps name->id map for lookup by name.
    /// </summary>
    public class SimpleRack : IRack
    {
        // 以 slotId(int) 为主键存储插槽
        private readonly Dictionary<int, ISlot> _slots = new();

        // 名称到 id 的映射，便于按名称查找
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);

        public SimpleRack(string name, int slotCount)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));

            // 预创建 slotCount 个默认插槽（id 从 0 开始）
            for (int i = 0; i < slotCount; i++)
            {
                CreateSlot(i, $"slot-{i}", this);
            }
        }

        /// <summary>
        /// 创建一个插槽，使用提供的 id 与 name；若 id 或 name 已存在则返回已存在的插槽。
        /// Create a slot with provided id and name; if id or name exists return existing slot.
        /// </summary>
        public ISlot CreateSlot(int id, string name, IRack rack)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (rack == null) throw new ArgumentNullException(nameof(rack));
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));

            // 若 id 已存在，返回现有插槽（并确保 name 映射一致）
            if (_slots.TryGetValue(id, out var existingById))
            {
                // 若名称不同，更新 name->id 映射以保证能按新 name 查到该 id（将覆盖旧映射）
                _nameToId[name] = id;
                return existingById;
            }

            // 若 name 已存在且对应的 id 不同，则返回已有按 name 找到的插槽（避免重复 name）
            if (_nameToId.TryGetValue(name, out var existingId))
            {
                if (_slots.TryGetValue(existingId, out var slotByName))
                {
                    return slotByName;
                }
            }

            var slot = new SimpleSlot(id, name, rack);
            _slots[id] = slot;
            _nameToId[name] = id;
            return slot;
        }

        /// <summary>
        /// 根据 id 删除插槽（若存在），并在删除前尝试卸载其中的模块。
        /// Remove a slot by id if exists; unplug module first if occupied.
        /// </summary>
        public bool RemoveSlot(int id)
        {
            if (!_slots.TryGetValue(id, out var slot))
                return false;

            if (slot.IsOccupied)
                slot.Unplug();

            _slots.Remove(id);

            // 移除对应的 name->id 映射（如果存在且匹配）
            var name = slot.Name;
            if (!string.IsNullOrEmpty(name) && _nameToId.TryGetValue(name, out var mappedId) && mappedId == id)
            {
                _nameToId.Remove(name);
            }

            return true;
        }

        public string Name { get; }

        /// <summary>
        /// 按 id 获取插槽（若不存在返回 null）。
        /// Get slot by id (returns null if not found).
        /// </summary>
        public ISlot? GetSlot(int id) => _slots.TryGetValue(id, out var s) ? s : null;

        /// <summary>
        /// 按名称查找插槽（先通过 name->id 映射，再从 id 表中读取）。
        /// Get slot by name (use name->id map then lookup by id).
        /// </summary>
        public ISlot? GetSlot(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            
        var slot=   _slots.Values.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

return slot;
        }

        /// <summary>
        /// 返回机架内所有插槽（按 id 无特定排序）。
        /// Return all slots in the rack (no guaranteed order).
        /// </summary>
        public IEnumerable<ISlot> AllSlots => _slots.Values;
    }
}
