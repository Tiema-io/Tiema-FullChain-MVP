using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Tiema.Runtime.Models
{

    /// <summary>
    /// 插槽配置（可选）：每个插槽的元数据与初始服务/参数
    /// Slot configuration (optional): slot metadata and initial parameters/services
    /// </summary>
    public class SlotConfig
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 插槽级别的服务或参数字典（例如硬件地址、驱动名等）。
        /// Slot-level service/params dictionary (e.g. hardware address, driver name).
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
