using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{
    /// <summary>
    /// Tiema 容器配置（含 modules 与可选的 racks/slots 配置）
    /// Tiema container configuration (includes modules and optional racks/slots)
    /// </summary>
    public class TiemaConfig
    {
        [JsonPropertyName("container")]
        public ContainerConfig Container { get; set; } = new ContainerConfig();

        [JsonPropertyName("plugins")]
        public List<PluginConfig> Plugins { get; set; } = new();

        [JsonPropertyName("racks")]
        public List<RackConfig> Racks { get; set; } = new();

        [JsonPropertyName("tags")]
        public TagConfig Tags { get; set; } = new TagConfig();

        [JsonPropertyName("messaging")]
        public MessagingConfig Messaging { get; set; } = new MessagingConfig();
    }

  




    /// <summary>
    /// Tag 系统配置 / Tag system configuration
    /// </summary>
    public class TagConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("persistToFile")]
        public bool PersistToFile { get; set; } = false;

        [JsonPropertyName("persistencePath")]
        public string PersistencePath { get; set; } = "./data/tags.json";

        [JsonPropertyName("defaultTags")]
        public Dictionary<string, object> DefaultTags { get; set; } = new();
    }

    /// <summary>
    /// 消息系统配置 / Messaging configuration
    /// </summary>
    public class MessagingConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "inmemory"; // inmemory, grpc, mqtt

        [JsonPropertyName("host")]
        public string Host { get; set; } = "localhost";

        [JsonPropertyName("port")]
        public int Port { get; set; } = 50051;
    }
}
