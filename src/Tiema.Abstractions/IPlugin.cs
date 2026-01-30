using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Abstractions
{
    // Interface for plugins in the Tiema runtime
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }

        void Initialize(IPluginContext context);

        void Execute();

    }


}
