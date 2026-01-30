using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tiema.Abstractions
{
    /// <summary>
    /// 插件基类：提供 Initialize/Start/Stop 的默认实现，插件自行控制内部循环。
    /// Plugin base class: provides default Initialize/Start/Stop implementation; plugin controls internal loop.
    ///
    /// 说明：
    /// - Initialize: 注入 Context 并执行一次性初始化（OnInitialize）。
    /// - Start: 启动内部后台循环，周期性调用受保护的 Execute()（PLC 风格）。
    /// - Stop: 请求停止并等待后台循环退出，然后调用 OnStop。
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        /// <summary>
        /// 插件名称 / Plugin name
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 插件版本 / Plugin version
        /// </summary>
        public virtual string Version => "1.0.0";

        /// <summary>
        /// 注入的插件上下文（派生类在 OnInitialize/OnStart/Execute/OnStop 中可安全使用）。
        /// The injected plugin context (derived classes can safely use in lifecycle hooks).
        /// </summary>
        protected IPluginContext Context { get; private set; } = null!;

        private bool _initialized;
        private bool _started;
        private CancellationTokenSource? _runCts;
        private Task? _runTask;

        /// <summary>
        /// 周期间隔（毫秒），派生类可覆写以改变默认频率。
        /// Run interval in milliseconds; override to change default cadence.
        /// </summary>
        protected virtual int RunIntervalMs => 1000;

        protected PluginBase() { }

        protected PluginBase(IPluginContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            OnInitialize();
            Console.WriteLine($"[{Name}] 初始化完成 (ctor) / initialized (ctor)");
        }

        public void Initialize(IPluginContext context)
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
                                Console.WriteLine($"[ERROR] 插件 {Name} 执行异常: {ex}");
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
                        Console.WriteLine($"[ERROR] 插件 {Name} 运行循环异常: {ex}");
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
        /// 插件的单周期逻辑（PLC 风格主程序）。派生类必须实现此方法（受保护，不对外暴露）。
        /// Single-cycle plugin logic (PLC-style main). Derived classes must implement this (protected).
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
                Console.WriteLine($"[ERROR] 停止插件 {Name} 时发生异常: {ex}");
            }
            finally
            {
                try
                {
                    OnStop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 插件 {Name} OnStop 异常: {ex}");
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
    }
}
