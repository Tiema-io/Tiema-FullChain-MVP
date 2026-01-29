using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{
    /// <summary>
    /// Tiema容器配置
    /// </summary>
    public class TiemaConfig
    {
        [JsonPropertyName("container")]
        public ContainerConfig Container { get; set; } = new ContainerConfig();

        [JsonPropertyName("plugins")]
        public List<PluginConfig> Plugins { get; set; } = new();

        [JsonPropertyName("tags")]
        public TagConfig Tags { get; set; } = new TagConfig();

        [JsonPropertyName("messaging")]
        public MessagingConfig Messaging { get; set; } = new MessagingConfig();
    }

    /// <summary>
    /// 容器配置
    /// </summary>
    public class ContainerConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Tiema Container";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("scanIntervalMs")]
        public int ScanIntervalMs { get; set; } = 100;

        [JsonPropertyName("maxConcurrentCycles")]
        public int MaxConcurrentCycles { get; set; } = 5;

        [JsonPropertyName("logLevel")]
        public string LogLevel { get; set; } = "Information";
    }

    /// <summary>
    /// 插件配置
    /// </summary>
    public class PluginConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;

        [JsonPropertyName("configuration")]
        public Dictionary<string, object> Configuration { get; set; } = new();
    }
    /// <summary>
    /// Tag系统配置
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
    /// 消息系统配置
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
