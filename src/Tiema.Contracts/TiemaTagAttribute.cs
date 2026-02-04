
using System;
using Tiema.Protocols.V1;

namespace Tiema.Contracts

{
    /// <summary>
    /// 标注模块成员为 Tiema Tag 的声明属性。
    /// 用法示例：
    /// [TiemaTag("Plant/Temperature", Role = TagRole.Producer, AutoPublishIntervalMs = 1000)]
    /// public int CurrentTemperature { get; set; }
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class TiemaTagAttribute : Attribute
    {
        public string Path { get; }
        public TagRole Role { get; set; } = TagRole.Producer;
        public string? Reference { get; set; }
        public int AutoPublishIntervalMs { get; set; } = 0; // 0 = 不自动发布

        public TiemaTagAttribute(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }
    }
}