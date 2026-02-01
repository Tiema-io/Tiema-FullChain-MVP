using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{

    /// <summary>
    /// 模块配置（原 PluginConfig -> ModuleConfig）
    /// Module configuration (renamed from PluginConfig)
    /// </summary>
    public class ModuleConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 0;

        // 通用配置字典（可以包含 rack/slotIndex 等）
        [JsonPropertyName("configuration")]
        public Dictionary<string, object> Configuration { get; set; } = new();
    }
}
