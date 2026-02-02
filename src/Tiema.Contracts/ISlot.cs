using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Tiema.Contracts
{
    public interface ISlot
    {
      
        // 不变的插槽标识，用于注册与发现（immutable identifier for slot）
        int Id { get; }

        // 可变标签，仅用于显示（mutable display label）
        string Name { get; }




        IRack Rack { get; }
        IModule Module { get; }

        bool IsOccupied { get; }
        bool Plug(IModule module);
        void Unplug();

     
    }
}
