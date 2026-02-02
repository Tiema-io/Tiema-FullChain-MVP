using System.Collections.Generic;

namespace Tiema.Hosting.Abstractions
{
    /// <summary>
    /// Tag 注册与拓扑管理。
    /// Tag registration and topology manager.
    /// </summary>
    public interface ITagRegistrationManager
    {
        /// <summary>
        /// 注册当前模块实例声明的 Producer/Consumer Tag。
        /// Register producer/consumer tags declared by a module instance.
        /// </summary>
        IReadOnlyList<TagIdentity> RegisterModuleTags(
            string moduleInstanceId,
            IEnumerable<string> producerPaths,
            IEnumerable<string> consumerPaths);

        /// <summary>
        /// 通过句柄获取 Tag 身份信息。
        /// Get tag identity by handle.
        /// </summary>
        TagIdentity? GetByHandle(uint handle);

        /// <summary>
        /// 通过路径获取 Tag 身份信息（简化版，同一路径只返回一个）。
        /// Get tag identity by path (simplified, single identity per path).
        /// </summary>
        TagIdentity? GetByPath(string path);
    }

    /// <summary>
    /// Tag 身份信息（后续可扩展）。
    /// Tag identity information (extensible).
    /// </summary>
    public sealed record TagIdentity(
        uint Handle,
        string Path,
        TagRole Role,
        string ModuleInstanceId);

    public enum TagRole
    {
        Producer = 0,
        Consumer = 1,
    }
}
