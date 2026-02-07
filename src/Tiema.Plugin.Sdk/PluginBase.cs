using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;

namespace Tiema.Sdk
{
    // Plugin base class: default Initialize/Start/Stop; plugin controls its own loop.
    // Lifecycle:
    // - Initialize: inject context and run one-time setup (OnInitialize).
    // - Start: start background run-loop periodically calling Execute().
    // - Stop: request stop, wait for loop exit, then call OnStop.
    // - OnPlugged/OnUnplugged: slot hooks called by host.
    public abstract class PluginBase : IPlugin
    {
        public abstract string Name { get; }
        public virtual string Version => "1.0.0";

        protected IPluginContext Context { get; private set; } = null!;

        private bool _initialized;
        private bool _started;
        private CancellationTokenSource? _runCts;
        private Task? _runTask;

        private ISlot? _currentSlot;
        protected ISlot? CurrentSlot => _currentSlot;

        protected virtual int RunIntervalMs => 1000;
        public virtual PluginType PluginType => PluginType.Other;

        protected PluginBase() { }

        protected PluginBase(IPluginContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            OnInitialize();
            Console.WriteLine($"[{Name}] initialized (ctor)");
        }

        public void Initialize(IPluginContext context)
        {
            if (_initialized) return;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            _initialized = true;
            OnInitialize();
            Console.WriteLine($"[{Name}] initialized");
        }

        protected virtual void OnInitialize() { }

        public virtual void Start()
        {
            if (_started) return;
            _runCts = new CancellationTokenSource();
            _started = true;

            try
            {
                OnStart();

                _runTask = Task.Run(async () =>
                {
                    var token = _runCts.Token;
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var cycleStart = DateTime.UtcNow;
                            try { Execute(); }
                            catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} execute error: {ex}"); }

                            var elapsed = (int)(DateTime.UtcNow - cycleStart).TotalMilliseconds;
                            var wait = RunIntervalMs - elapsed;
                            if (wait > 0)
                            {
                                try { await Task.Delay(wait, token); } catch (TaskCanceledException) { break; }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} run-loop error: {ex}"); }
                }, _runCts.Token);

                Console.WriteLine($"[{Name}] started");
            }
            catch
            {
                _runCts?.Dispose();
                _runCts = null;
                _started = false;
                throw;
            }
        }

        protected virtual void OnStart() { }
        protected abstract void Execute();

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
                        Console.WriteLine($"[{Name}] stop timeout (5s)");
                    }
                }
            }
            catch (AggregateException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] stop plugin {Name} error: {ex}");
            }
            finally
            {
                try { OnStop(); }
                catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} OnStop error: {ex}"); }

                _runCts?.Dispose();
                _runCts = null;
                _runTask = null;
                _started = false;
                Console.WriteLine($"[{Name}] stopped");
            }
        }

        protected virtual void OnStop() { }

        public virtual void OnPlugged(ISlot slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            _currentSlot = slot;

            try { OnSlotPlugged(slot); }
            catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} OnSlotPlugged error: {ex}"); }
        }

        public virtual void OnUnplugged()
        {
            var old = _currentSlot;

            try { OnSlotUnplugged(old); }
            catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} OnSlotUnplugged error: {ex}"); }

            try { Stop(); }
            catch (Exception ex) { Console.WriteLine($"[ERROR] plugin {Name} stop on unplug failed: {ex}"); }

            _currentSlot = null;
        }

        protected virtual void OnSlotPlugged(ISlot slot) { }
        protected virtual void OnSlotUnplugged(ISlot? slot) { }
    }
}
