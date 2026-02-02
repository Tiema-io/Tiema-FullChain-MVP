using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tiema.Contracts
{
    /// <summary>
    /// 输出指示器服务（例如面板指示灯、继电器等）。
    /// Minimal indicator service for on/off/toggle and state notifications.
    /// </summary>
    public interface IIndicatorService
    {
        /// <summary>异步打开指示器 / turn on</summary>
        Task TurnOnAsync(CancellationToken cancellationToken = default);

        /// <summary>异步关闭指示器 / turn off</summary>
        Task TurnOffAsync(CancellationToken cancellationToken = default);

        /// <summary>设置状态 / set state</summary>
        Task SetStateAsync(bool on, CancellationToken cancellationToken = default);

        /// <summary>切换状态 / toggle</summary>
        Task ToggleAsync(CancellationToken cancellationToken = default);

        /// <summary>当前状态（只读）/ current state</summary>
        bool IsOn { get; }

        /// <summary>状态改变事件（方便 UI 或其它模块订阅）/ state changed event</summary>
        event EventHandler<bool>? StateChanged;
    }
}