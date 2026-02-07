using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Contracts;


namespace Tiema.Runtime.Models
{
    public class SimpleSlot : ISlot
    {
    

        public SimpleSlot(int id, string name, IRack rack)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Rack = rack;
        }

        public int Id { get; }
        public string Name { get; private set; } // 只读属性
        public IRack Rack { get; }
        public IPlugin Plugin { get; private set; }
        public bool IsOccupied => Plugin != null;

        public bool Plug(IPlugin plugin)
        {
            if (plugin == null) return false;
            if (IsOccupied) return false;
            Plugin = plugin;

       
            return true;
        }

        public void Unplug()
        {
            Plugin = null;
        }


        // 仅供容器/管理界面修改显示标签
        public void SetName(string name)
        {
            Name = name;
        }
    }
}
