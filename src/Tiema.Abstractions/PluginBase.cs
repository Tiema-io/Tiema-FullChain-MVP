using System;

namespace Tiema.Abstractions
{
    /// <summary>
    /// 插件基类，提供统一的初始化流程与可选的构造器注入。
    /// Plugin base class that provides a unified initialization flow and optional constructor injection.
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        /// <summary>
        /// 插件名称，派生类必须实现。
        /// Plugin name, must be implemented by derived classes.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 插件版本，默认 "1.0.0"。
        /// Plugin version, defaults to "1.0.0".
        /// </summary>
        public virtual string Version => "1.0.0";

        /// <summary>
        /// 注入的插件上下文，派生类可通过此属性访问运行时服务（只读）。
        /// The injected plugin context; derived classes can use this to access runtime services (read-only).
        /// </summary>
        protected IPluginContext Context { get; private set; }

        // 标记是否已完成初始化，防止构造器注入与 Initialize 双重执行
        // Marks whether initialization has completed to avoid double initialization via ctor + Initialize.
        private bool _initialized;

        /// <summary>
        /// 无参构造，保持对只支持无参创建的宿主兼容。
        /// Parameterless ctor to stay compatible with hosts that only support parameterless creation.
        /// </summary>
        protected PluginBase() { }

        /// <summary>
        /// 可选的构造器注入：当宿主支持构造器注入时可直接使用该构造器完成初始化。
        /// Optional constructor injection: when the host supports constructor injection, this ctor allows
        /// initializing the plugin immediately.
        /// </summary>
        /// <param name="context">插件上下文 / plugin context</param>
        protected PluginBase(IPluginContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            try
            {
                OnInitialize();
                Console.WriteLine($"[{Name}] Initialization completed (ctor)"); // Initialization completed (ctor)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] Initialization failed (ctor): {ex.Message}"); // Initialization failed (ctor)
                throw;
            }
        }

        /// <summary>
        /// 兼容的初始化入口：仅当尚未通过构造器初始化时执行。
        /// Compatible Initialize entry: executes only if not already initialized via ctor.
        /// </summary>
        /// <param name="context">插件上下文 / plugin context</param>
        public void Initialize(IPluginContext context)
        {
            if (_initialized) return;

            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            try
            {
                OnInitialize();
                Console.WriteLine($"[{Name}] initialized completed (ctor)"); // Initialization completed
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] initialization failed {ex.Message}"); // Initialization failed
                throw;
            }
        }

        /// <summary>
        /// 派生类可覆写此方法实现初始化逻辑，此时可以安全使用 <see cref="Context"/>。
        /// Derived classes can override this to implement initialization logic. It is safe to use <see cref="Context"/> here.
        /// </summary>
        protected virtual void OnInitialize() { }

        /// <summary>
        /// 执行插件逻辑，派生类必须实现。
        /// Execute plugin logic; must be implemented by derived classes.
        /// </summary>
        public abstract void Execute();
    }
}
