using System;
using Tiema.Contracts;

namespace Tiema.Hosting.Abstractions
{
    // Runtime host: lifecycle control, rack/slot/plugin management, and core services access.
    public interface IPluginHost
    {
        // Start and run the host (may block for daemon-style hosts).
        void Run();

        // Request host stop; triggers graceful shutdown of plugins.
        void Stop();

        IRackManager Racks { get; }

        // Slot manager for finding/accessing plugins by slot.
        ISlotManager Slots { get; }

        // Service registry / discovery entrypoint for the platform.
        IServiceRegistry Services { get; }
    }
}