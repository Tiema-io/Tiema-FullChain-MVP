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
        public IModule Module { get; private set; }
        public bool IsOccupied => Module != null;

        public bool Plug(IModule module)
        {
            if (module == null) return false;
            if (IsOccupied) return false;
            Module = module;

       
            return true;
        }

        public void Unplug()
        {
            Module = null;
        }


        // 仅供容器/管理界面修改显示标签
        public void SetName(string name)
        {
            Name = name;
        }
    }
}
