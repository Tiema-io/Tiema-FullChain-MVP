using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Abstractions
{
    public  interface IMessageService
    {
        void Publish(string topic, object message)  ;
        void Subscribe(string topic, Action<object> handler);

    }
}
