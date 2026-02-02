using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Models;

namespace Tiema.Runtime
{
    // 简单的内存机架管理器（按名称存放机架）
    public class SimpleRackManager : IRackManager
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
}
