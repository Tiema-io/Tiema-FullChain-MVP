using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Tiema.Hosting.Abstractions;
using Tiema.Protocols.V1;

namespace Tiema.Runtime.Services
{
    /// <summary>
    /// 内存版 Tag 注册管理器：句柄分配与简单查询（支持释放/清理）。
    /// In-memory tag registration manager: handle allocation and simple lookup.
    /// </summary>
    public class InMemoryTagRegistrationManager : ITagRegistrationManager, IDisposable
    {
        private uint _nextHandle = 1;

        private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();
        private readonly ConcurrentDictionary<string, TagIdentity> _byPath =
            new(StringComparer.OrdinalIgnoreCase);

        private int _disposed;

        public IReadOnlyList<TagIdentity> RegisterModuleTags(
            string moduleInstanceId,
            IEnumerable<string> producerPaths,
            IEnumerable<string> consumerPaths)
        {
            if (moduleInstanceId is null)
                throw new ArgumentNullException(nameof(moduleInstanceId));

            var identities = new List<TagIdentity>();

            foreach (var path in producerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var identity = CreateOrGetIdentity(path, TagRole.Producer, moduleInstanceId);
                identities.Add(identity);
            }

            foreach (var path in consumerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var identity = CreateOrGetIdentity(path, TagRole.Consumer, moduleInstanceId);
                identities.Add(identity);
            }

            return identities;
        }

        public TagIdentity? GetByHandle(uint handle)
        {
            return _byHandle.TryGetValue(handle, out var identity) ? identity : null;
        }

        public TagIdentity? GetByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return _byPath.TryGetValue(path, out var identity) ? identity : null;
        }

        private TagIdentity CreateOrGetIdentity(string path, TagRole role, string moduleInstanceId)
        {
            if (_byPath.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var handle = System.Threading.Interlocked.Increment(ref _nextHandle);
            var identity = new TagIdentity(handle, path, role, moduleInstanceId);

            _byHandle[handle] = identity;
            _byPath[path] = identity;

            return identity;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            try
            {
                _byHandle.Clear();
                _byPath.Clear();
            }
            catch { /* best-effort */ }
        }
    }
}