using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Sdk;


namespace ModbusSensor
{
    /// <summary>
    /// 模拟 Modbus 传感器插件：在内部循环中周期性生成温度并写入 Tag 系统。
    /// Simulated Modbus sensor plugin: periodically generates temperature and writes to Tag system in internal loop.
    /// </summary>



    public class ModbusSensorPlugin : ModuleBase
    {
        public override string Name => "ModbusSensor";

        private Random _random = new Random();
        private int _sensorValue = 25;
        protected override int RunIntervalMs => 1000;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            // 声明 Producer Tag。
            // Declare producer tag.
            Context.Tags.DeclareProducer("Plant/Temperature");
        }

        protected override void OnStart()
        {
            base.OnStart();
            // 可在此启动额外的后台任务（默认 RunLoop 会调用 Execute）
        }

        /// <summary>
        /// 单周期逻辑（由 PluginBase 的 RunLoop 调用）。
        /// Single-cycle logic (called by PluginBase RunLoop).
        /// </summary>
        protected override void Execute()
        {
            _sensorValue = 25 + _random.Next(-5, 7);
            Context.Tags.SetTag("Plant/Temperature", _sensorValue);
            Console.WriteLine($"[{Name}] 温度: {_sensorValue}°C / Temperature: {_sensorValue}°C");
        }

        protected override void OnStop()
        {
            base.OnStop();
            // 清理资源
        }
    }
}
