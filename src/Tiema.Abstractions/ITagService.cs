using System;
using System.Collections.Generic;
using System.Text;

namespace Tiema.Abstractions
{
    public interface ITagService
    {
        void SetTag(string key, object value);
        T GetTag<T> (string key);
   

    }
}
