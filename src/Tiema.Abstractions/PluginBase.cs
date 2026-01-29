namespace Tiema.Abstractions
{
    public abstract class PluginBase : IPlugin
    {
        public abstract string Name { get; }
        public virtual string Version => "1.0.0";

        protected IPluginContext Context { get; private set; }

        public virtual void Initialize(IPluginContext context)
        {
            Context = context;
            Console.WriteLine($"[{Name}] 初始化完成");
        }

        public abstract void Execute(ICycleContext context);
    }
}
