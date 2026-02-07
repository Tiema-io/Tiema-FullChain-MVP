using System;
using Tiema.Contracts;
using Tiema.Hosting.Abstractions;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// Default plugin context implementation: maps host-exposed Racks/Slots/Tag/Message services to plugin context interfaces.
    /// Note: registration is not triggered at context level; host calls TagAutoRegistrar after loading the plugin.
    /// </summary>
    public class DefaultPluginContext : IPluginContext
    {
        private ISlot? _currentSlot;

        // Tag service exposed by host (no module-scoped wrapper)
        public ITagService Tags { get; }

        // Message service exposed by host
        public IMessageService Messages { get; }

        // Unified host-level service registry
        public IServiceRegistry Services { get; }

        // Stable plugin instance id (assigned by host when creating the context)
        public string PluginInstanceId { get; }

        public DefaultPluginContext(
            IPluginHost host,
            string pluginInstanceId,
            ITagService tagService,
            IMessageService messageService,
            IServiceRegistry serviceRegistry)
        {
            PluginInstanceId = pluginInstanceId ?? throw new ArgumentNullException(nameof(pluginInstanceId));
            Tags = tagService ?? throw new ArgumentNullException(nameof(tagService));
            Messages = messageService ?? throw new ArgumentNullException(nameof(messageService));
            Services = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
        }

        // CurrentSlot is non-null after the plugin is plugged into a slot; otherwise throws.
        public ISlot CurrentSlot
        {
            get
            {
                if (_currentSlot == null)
                    throw new InvalidOperationException("CurrentSlot is not set for this context.");
                return _currentSlot;
            }
        }

        // Host sets the current slot when plugging/unplugging the plugin.
        public void SetCurrentSlot(ISlot? slot) => _currentSlot = slot;
    }
}
