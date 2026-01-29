using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;

namespace Tiema.Runtime.Services
{
    public class DefaultCycleContext : ICycleContext
    {
        public DefaultCycleContext(
          
             ITagService tagService,
             IMessageService messageService
         )
        {
        
            Tags = tagService;
            Messages = messageService;
           
        }

        public ITagService Tags { get; }
        public IMessageService Messages { get; }
    }
}
