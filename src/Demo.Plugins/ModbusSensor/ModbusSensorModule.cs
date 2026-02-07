using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Sdk;

using Tiema.Tags.Grpc.V1;
using Tiema.Contracts;     // TagRole for attribute

namespace ModbusSensor
{
    /// <summary>
    /// 模拟 Modbus 传感器插件：周期生成温度并写入 Tag 系统（仅使用宿主提供的 Context.Tags）。
    /// </summary>
    public class ModbusSensorPlugin : PluginBase
    {
        public override string Name => "ModbusSensor";

        private Random _random = new Random();
        private int _sensorValue = 25;
        protected override int RunIntervalMs => 1000;

  
        [TiemaTag("Plant/Temperature", Role = TagRole.Producer)]
        private int AutoPublishedTemperature => _sensorValue;

        protected override void OnInitialize()
        {
            base.OnInitialize();
       
        }

        protected override void OnStart()
        {
            base.OnStart();
      
        }

        protected override void Execute()
        {
            _sensorValue = 25 + _random.Next(-5, 7);

            // 始终使用宿主提供的 Tags 服务发布
            Context.Tags.SetTag("Plant/Temperature", _sensorValue);

            Console.WriteLine($"[{Name}] 温度: {_sensorValue}°C / Temperature: {_sensorValue}°C");
        }

        protected override void OnStop()
        {
            base.OnStop();
         
        }
    }
}
