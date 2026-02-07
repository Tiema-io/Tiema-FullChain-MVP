using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Contracts;
using Tiema.Sdk;


namespace LedSimulator
{
    /// <summary>
    /// LED 模拟器模块：实现 IIndicatorService，并在插槽插入时注册到宿主 registry。
    /// Registers as service name: "device.output.indicator.led" (slot-scoped).
    /// </summary>
    public class LedSimulatorModule : PluginBase, IIndicatorService
    {
        public override string Name => "LedSimulator";

        private readonly object _lock = new();
        private bool _isOn;

        public bool IsOn
        {
            get { lock (_lock) { return _isOn; } }
            private set { lock (_lock) { _isOn = value; } }
        }

        public event EventHandler<bool>? StateChanged;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            // 读取配置（如果需要）: Context.GetServiceFromRack / Context.Tags 等
        }

        protected override void OnSlotPlugged(ISlot slot)
        {
            base.OnSlotPlugged(slot);
            try
            {
                // 使用 slot.Rack.RegisterService 进行注册
              
                Context.Services.Register(CurrentSlot.Rack.Name,
                                          CurrentSlot.Id,
                                          "device.output.indicator.led",
                                          this  );
                
                Console.WriteLine($"[{Name}] registered device.output.indicator.led at {slot.Rack.Name}/{slot.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Name}] register failed: {ex.Message}");
            }
        }

        protected override void OnSlotUnplugged(ISlot? slot)
        {
            base.OnSlotUnplugged(slot);
            // 可选：尝试在宿主中清除注册（若实现支持）
            Console.WriteLine($"[{Name}] unplugged");
        }

        protected override void Execute()
        {
            // 模拟指示器状态检查或闪烁逻辑（空实现即可）

        }

        protected override void OnStop()
        {
            base.OnStop();
            // 清理资源
        }

        // IIndicatorService 实现（异步、支持 CancellationToken）
        public Task TurnOnAsync(CancellationToken cancellationToken = default)
        {
            SetState(true);
            return Task.CompletedTask;
        }

        public Task TurnOffAsync(CancellationToken cancellationToken = default)
        {
            SetState(false);
            return Task.CompletedTask;
        }

        public Task SetStateAsync(bool on, CancellationToken cancellationToken = default)
        {
            SetState(on);
            return Task.CompletedTask;
        }

        public Task ToggleAsync(CancellationToken cancellationToken = default)
        {
            SetState(!IsOn);
            return Task.CompletedTask;
        }

        private void SetState(bool on)
        {
            var changed = false;
            lock (_lock)
            {
                if (_isOn != on)
                {
                    _isOn = on;
                    changed = true;
                }
            }
            if (changed)
            {
                Console.WriteLine($"[{Name}] state -> {(on ? "ON" : "OFF")}");
                StateChanged?.Invoke(this, on);
            }
        }
    }
}
