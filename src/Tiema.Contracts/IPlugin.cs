using System;

namespace Tiema.Contracts
{
    // Plugin interface: single initialization entry and host-controlled lifecycle.
    public interface IPlugin
    {
        // Plugin name
        string Name { get; }

        // Plugin version
        string Version { get; }

        // Initialize once: inject plugin context; do not assume handles during Initialize.
        void Initialize(IPluginContext context);

        // Start plugin (host calls). Begin background tasks/loops here.
        void Start();

        // Stop plugin (host calls). Gracefully shutdown and release resources.
        void Stop();

        // Classification (Robot, Vision, PLC, Database, etc.)
        PluginType PluginType { get; }

        // Called when the plugin is plugged into a slot; host guarantees non-null slot.
        void OnPlugged(ISlot slot);

        // Called when the plugin is unplugged from its slot.
        void OnUnplugged();
    }

    public enum PluginType
    {
        Robot,
        Vision,
        PLC,
        Database,
        Other
    }
}