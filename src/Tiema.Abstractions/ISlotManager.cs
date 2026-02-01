using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tiema.Abstractions
{
    public interface ISlotManager
    {
        // 获取或创建插槽
        ISlot GetSlot(string path, bool createIfNotExist = false);

        // 所有插槽
        IReadOnlyList<ISlot> AllSlots { get; }
    }
}
