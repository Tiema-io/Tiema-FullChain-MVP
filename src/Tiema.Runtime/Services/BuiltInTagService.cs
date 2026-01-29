using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;

namespace Tiema.Runtime.Services
{
    // 容器中的简单实现
    public class BuiltInTagService : ITagService
    {
        private readonly Dictionary<string, object> _tags = new();

        public void SetTag(string path, object value)
        {
            _tags[path] = value;
        }

        public T GetTag<T>(string path)
        {
            return _tags.TryGetValue(path, out var value) ? (T)value : default;
        }

 
    }
}
