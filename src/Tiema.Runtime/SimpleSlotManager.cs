using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Models;

namespace Tiema.Runtime
{
    // 插槽管理器：路径格式 "rackName/slotName"（不再默认使用 index）
    public class SimpleSlotManager : ISlotManager
    {
        private readonly IRackManager _rackManager;

        public SimpleSlotManager(IRackManager rackManager)
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
}
