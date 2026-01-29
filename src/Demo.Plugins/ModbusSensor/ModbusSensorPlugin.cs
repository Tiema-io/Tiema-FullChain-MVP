using System;
using Tiema.Abstractions;


namespace ModbusSensor
{
    // 1. 模拟传感器插件
    public class ModbusSensorPlugin : PluginBase
    {
        public override string Name => "ModbusSensor";

        private Random _random = new Random();
        private int _sensorValue = 25;

        public override void Execute(ICycleContext context)
        {
            // 模拟读取温度（25°C ± 5°C）
            _sensorValue = 25 + _random.Next(-5, 7);

            // 写入Tag系统
            Context.Tags.SetTag("Plant/Temperature", _sensorValue);

          

            Console.WriteLine($"[{Name}] 温度: {_sensorValue}°C");
        }
    }
}
