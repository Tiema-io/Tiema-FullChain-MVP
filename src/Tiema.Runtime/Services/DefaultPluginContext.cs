using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;

namespace Tiema.Runtime.Services
{
    public class DefaultPluginContext : IPluginContext
    {
        private readonly TiemaContainer _container;
  

        public DefaultPluginContext(
            TiemaContainer container
       
           )
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
      
        }

        public IPluginContainer Container => _container;
        public ITagService Tags => _container.TagService;
        public IMessageService Messages => _container.MessageService;
    }
}
