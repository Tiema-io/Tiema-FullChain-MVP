using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{
    /// <summary>
    /// 机架配置（可选）：在容器启动时创建机架与插槽
    /// Rack configuration (optional): create racks/slots at host startup
    /// </summary>
    public class RackConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 如果直接给出 slotCount，会创建该数量的空插槽。
        /// If slotCount provided, create that many empty slots.
        /// </summary>
        [JsonPropertyName("slotCount")]
        public int SlotCount { get; set; } = 0;

        /// <summary>
        /// 可选：显式列出每个插槽的配置（优先于 slotCount）。
        /// Optional: explicit per-slot configs (preferred over slotCount).
        /// </summary>
        [JsonPropertyName("slots")]
        public List<SlotConfig> Slots { get; set; } = new();
    }
}
