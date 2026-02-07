using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using Tiema.Hosting.Abstractions;
using Tiema.Tags.Grpc.V1;        // tagsystem.proto C# namespace
using Tiema.Connect.Grpc.V1;
using static Tiema.Connect.Grpc.V1.DataConnect;     // connect.proto C# namespace

namespace Tiema.DataConnect.Core
{
    /// <summary>
    /// gRPC-based ITagRegistrationManager: forwards RegisterTags to remote DataConnect and caches TagIdentity.
    /// </summary>
    public sealed class TiemaDataConnectTagRegistrationManager : ITagRegistrationManager, IDisposable
    {
        private readonly DataConnectClient _client;
        private readonly Channel _channel;
        private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();
        private readonly ConcurrentDictionary<string, TagIdentity> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public TiemaDataConnectTagRegistrationManager(string grpcUrl)
        {
            if (string.IsNullOrWhiteSpace(grpcUrl)) throw new ArgumentNullException(nameof(grpcUrl));

            // Accept "host:port" and "http(s)://host:port"; normalize to host:port for Grpc.Core.Channel.
            string target = grpcUrl.Trim();
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : (uri.Scheme == "https" ? 443 : 80);
                target = $"{host}:{port}";
            }

            _channel = new Channel(target, ChannelCredentials.Insecure);
            _client = new DataConnectClient(_channel);
        }

        public IReadOnlyList<TagIdentity> RegisterModuleTags(string moduleInstanceId, IEnumerable<string> producerPaths, IEnumerable<string> consumerPaths)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TiemaDataConnectTagRegistrationManager));

            // Use plugin terminology in proto: RegisterTagsRequest.plugin_instance_id
            var req = new RegisterTagsRequest { PluginInstanceId = moduleInstanceId ?? string.Empty };

            foreach (var p in producerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                // RegisterTagInfo.tag_path + role
                req.Tags.Add(new RegisterTagInfo { TagPath = p, Role = TagRole.Producer });
            }

            foreach (var c in consumerPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                req.Tags.Add(new RegisterTagInfo { TagPath = c, Role = TagRole.Consumer });
            }

            try
            {
                var resp = _client.RegisterTagsAsync(req).ResponseAsync.ConfigureAwait(false).GetAwaiter().GetResult();
                var result = new List<TagIdentity>();
                if (resp?.Assigned != null)
                {
                    foreach (var a in resp.Assigned)
                    {
                        // Assigned fields: tag_path / source_plugin_instance_id
                        var path = a.TagPath ?? string.Empty;
                        var role = a.Role;
                        var handle = a.Handle;
                        var src = a.SourcePluginInstanceId ?? moduleInstanceId ?? string.Empty;

                        var identity = new TagIdentity(handle, path, role, src);

                        _byHandle[handle] = identity;
                        _byPath[path] = identity;
                        result.Add(identity);
                    }
                }
                return result;
            }
            catch (RpcException rex)
            {
                Console.WriteLine($"[WARN] TagRegistrationManager.RegisterModuleTags RPC failed: {rex.Status.Detail}");
                return Array.Empty<TagIdentity>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] TagRegistrationManager.RegisterModuleTags failed: {ex.Message}");
                return Array.Empty<TagIdentity>();
            }
        }

        public TagIdentity? GetByHandle(uint handle) => _byHandle.TryGetValue(handle, out var id) ? id : null;

        public TagIdentity? GetByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return _byPath.TryGetValue(path, out var id) ? id : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _channel?.ShutdownAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}