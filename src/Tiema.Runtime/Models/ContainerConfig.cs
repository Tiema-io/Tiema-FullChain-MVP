using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{

    /// <summary>
    /// 容器配置 / Container configuration
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
}
