using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Contracts
{
    public interface IRack
    {
        string Name { get; }
        ISlot CreateSlot(int id, string name, IRack rack);
        bool RemoveSlot(int id);

        ISlot GetSlot(int id);
        ISlot GetSlot(string name);

        IEnumerable<ISlot> AllSlots { get; }

        // 机架级别服务
   
    }
}
