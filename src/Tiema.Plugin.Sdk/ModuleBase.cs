using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;

namespace Tiema.Sdk
{
    /// <summary>
    /// 模块基类：提供 Initialize/Start/Stop 的默认实现，模块自行控制内部循环。
    /// Module base class: provides default Initialize/Start/Stop implementation; module controls internal loop.
    ///
    /// 说明：
    /// - Initialize: 注入 Context 并执行一次性初始化（OnInitialize）。
    /// - Start: 启动内部后台循环，周期性调用受保护的 Execute()（PLC 风格）。
    /// - Stop: 请求停止并等待后台循环退出，然后调用 OnStop。
    /// - OnPlugged/OnUnplugged: 插槽插拔事件钩子，由宿主在模块被插入/拔出时调用。
    /// </summary>
    public abstract class ModuleBase : IModule
    {
        /// <summary>
        /// 模块名称 / Module name
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 模块版本 / Module version
        /// </summary>
        public virtual string Version => "1.0.0";

        /// <summary>
        /// 注入的模块上下文（派生类在 OnInitialize/OnStart/Execute/OnStop/OnSlotPlugged/OnSlotUnplugged 可安全使用）。
        /// The injected module context (derived classes can safely use in lifecycle hooks).
        /// </summary>
        protected IModuleContext Context { get; private set; } = null!;

        private bool _initialized;
        private bool _started;
        private CancellationTokenSource? _runCts;
        private Task? _runTask;

        // 当前所在插槽（当模块被插入时由宿主设置）
        // Current slot the module is plugged into (set by host when plugged)
        private ISlot? _currentSlot;

        /// <summary>
        /// 公开可读的当前插槽（派生类可通过此属性访问插槽信息）。
        /// Public read-only current slot (derived classes can inspect slot info).
        /// </summary>
        protected ISlot? CurrentSlot => _currentSlot;

        /// <summary>
        /// 周期间隔（毫秒），派生类可覆写以改变默认频率。
        /// Run interval in milliseconds; override to change default cadence.
        /// </summary>
        protected virtual int RunIntervalMs => 1000;

        public ModuleType ModuleType => throw new NotImplementedException();

        protected ModuleBase() { }

        protected ModuleBase(IModuleContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            OnInitialize();
            Console.WriteLine($"[{Name}] 初始化完成 (ctor) / initialized (ctor)");
        }

        public void Initialize(IModuleContext context)
        {
            if (_initialized) return;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            OnInitialize();
            Console.WriteLine($"[{Name}] 初始化完成 / initialized");
        }

        /// <summary>
        /// 初始化钩子：派生类在此完成一次性初始化（此时 Context 可用）。
        /// Initialization hook: derived classes perform one-time initialization here (Context is available).
        /// </summary>
        protected virtual void OnInitialize() { }

        /// <summary>
        /// 启动：默认实现启动一个后台任务执行 RunLoop（周期性调用 Execute）。
        /// Start: default starts a background task that runs the run-loop calling Execute periodically.
        /// 派生类可覆写 OnStart 或在 OnStart 中启动自定义后台任务并忽略默认 RunLoop。
        /// Override OnStart to start custom tasks and optionally bypass default RunLoop.
        /// </summary>
        public virtual void Start()
        {
            if (_started) return;
            _runCts = new CancellationTokenSource();
            _started = true;

            try
            {
                OnStart();

                // 启动后台 RunLoop：周期性调用受保护的 Execute()
                _runTask = Task.Run(async () =>
                {
                    var token = _runCts.Token;
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var cycleStart = DateTime.UtcNow;
                            try
                            {
                                Execute();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR] 模块 {Name} 执行异常: {ex}");
                            }

                            var elapsed = (int)(DateTime.UtcNow - cycleStart).TotalMilliseconds;
                            var wait = RunIntervalMs - elapsed;
                            if (wait > 0)
                            {
                                try { await Task.Delay(wait, token); } catch (TaskCanceledException) { break; }
                            }
                        }
                    }
                    catch (OperationCanceledException) { /* canceled */ }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] 模块 {Name} 运行循环异常: {ex}");
                    }
                }, _runCts.Token);

                Console.WriteLine($"[{Name}] 启动 / started");
            }
            catch
            {
                // 如果 OnStart 抛出则清理状态
                _runCts?.Dispose();
                _runCts = null;
                _started = false;
                throw;
            }
        }

        /// <summary>
        /// 启动钩子：派生类在此启动额外后台任务或准备运行态资源。
        /// Start hook: derived classes start extra background tasks or prepare runtime resources here.
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// 模块的单周期逻辑（PLC 风格主程序）。派生类必须实现此方法（受保护，不对外暴露）。
        /// Single-cycle module logic (PLC-style main). Derived classes must implement this (protected).
        /// </summary>
        protected abstract void Execute();

        /// <summary>
        /// 停止：请求后台循环停止并等待其退出，然后调用 OnStop 清理。
        /// Stop: request run-loop stop, wait for exit, then call OnStop to cleanup.
        /// </summary>
        public virtual void Stop()
        {
            if (!_started) return;

            try
            {
                _runCts?.Cancel();
                if (_runTask != null)
                {
                    var completed = _runTask.Wait(TimeSpan.FromSeconds(5));
                    if (!completed)
                    {
                        Console.WriteLine($"[{Name}] 停止超时，后台任务未在 5s 内结束 / stop timeout");
                    }
                }
            }
            catch (AggregateException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 停止模块 {Name} 时发生异常: {ex}");
            }
            finally
            {
                try
                {
                    OnStop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 模块 {Name} OnStop 异常: {ex}");
                }

                _runCts?.Dispose();
                _runCts = null;
                _runTask = null;
                _started = false;
                Console.WriteLine($"[{Name}] 停止 / stopped");
            }
        }

        /// <summary>
        /// 停止钩子：派生类在此释放资源并停止自有后台任务。
        /// Stop hook: derived classes release resources and stop custom background tasks here.
        /// </summary>
        protected virtual void OnStop() { }

        // -------------------------
        // 插槽插拔相关
        // -------------------------

        /// <summary>
        /// 当模块被插入到插槽时由宿主调用。默认行为：保存当前插槽，并调用可覆写的 OnSlotPlugged(slot)。
        /// Host calls this when the module is plugged into a slot. Default: store slot and call OnSlotPlugged(slot).
        /// </summary>
        /// <param name="slot">插入的插槽 / the slot the module is plugged into</param>
        public virtual void OnPlugged(ISlot slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));

            // 保存当前插槽引用，供派生类访问
            _currentSlot = slot;

            try
            {
                // 派生类可在此覆写以执行与插槽相关的初始化（比如绑定硬件地址、注册槽级服务等）
                OnSlotPlugged(slot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 模块 {Name} OnSlotPlugged 异常: {ex}");
            }
        }

        /// <summary>
        /// 当模块从插槽拔出时由宿主调用。默认行为：调用可覆写的 OnSlotUnplugged(oldSlot)，尝试停止模块并清理插槽相关状态。
        /// Host calls this when the module is unplugged from a slot. Default: call OnSlotUnplugged(oldSlot), stop the module and clear slot state.
        /// </summary>
        public virtual void OnUnplugged()
        {
            var old = _currentSlot;

            try
            {
                // 派生类可在此处理拔出时的自定义清理
                OnSlotUnplugged(old);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 模块 {Name} OnSlotUnplugged 异常: {ex}");
            }

            // 默认尝试优雅停止模块（如果尚未停止）
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 模块 {Name} 在拔出时停止失败: {ex}");
            }

            // 清理当前插槽引用
            _currentSlot = null;
        }

        /// <summary>
        /// 插槽插入钩子：派生类在此执行与插槽相关的初始化（例如绑定物理地址、注册插槽级服务、订阅插槽事件等）。
        /// Slot plugged hook: override to perform slot-specific initialization (bind addresses, register slot services, subscribe to slot events).
        /// </summary>
        /// <param name="slot">插槽 / slot</param>
        protected virtual void OnSlotPlugged(ISlot slot) { }

        /// <summary>
        /// 插槽拔出钩子：派生类在此执行与插槽相关的清理（例如断开硬件、注销服务、取消订阅等）。
        /// Slot unplugged hook: override to perform slot-specific cleanup (disconnect hardware, unregister services, unsubscribe).
        /// </summary>
        /// <param name="slot">之前的插槽 / previous slot (may be null)</param>
        protected virtual void OnSlotUnplugged(ISlot? slot) { }
    }
}
