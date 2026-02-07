using System;

namespace Tiema.Contracts
{
    // Plugin context: runtime-provided environment and services for a plugin instance.
    public interface IPluginContext
    {
        // Stable plugin instance id assigned by the host (used for registration/routing/audit).
        string PluginInstanceId { get; }

        // Current slot the plugin is plugged into; non-null after PlugModuleToSlot.
        ISlot CurrentSlot { get; }

        // Unified host-level service registry for registering/resolving services.
        IServiceRegistry Services { get; }

        // Tag service for tag system operations.
        ITagService Tags { get; }

        // Message service for message system operations.
        IMessageService Messages { get; }
    }
}
