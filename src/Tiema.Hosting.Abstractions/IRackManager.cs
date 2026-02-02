using System;
using System.Collections.Generic;
using System.Text;

using Tiema.Contracts;

namespace Tiema.Hosting.Abstractions
{
    public interface IRackManager
    {
        IRack CreateRack(string name, int slotCount);
        IRack GetRack(string name);
        IEnumerable<IRack> AllRacks { get; }
    }
}
