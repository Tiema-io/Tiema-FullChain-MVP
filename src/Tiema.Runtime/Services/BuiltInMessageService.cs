using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;

namespace Tiema.Runtime.Services
{
    public class BuiltInMessageService : IMessageService
    {
        private readonly Dictionary<string, List<Action<object>>> _subscriptions = new();

        public void Publish(string topic, object message)
        {
            if (_subscriptions.TryGetValue(topic, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    handler(message);
                }
            }
        }

        public void Subscribe(string topic, Action<object> handler)
        {
            if (!_subscriptions.ContainsKey(topic))
            {
                _subscriptions[topic] = new List<Action<object>>();
            }
            _subscriptions[topic].Add(handler);
        }
    }
}
