using System;
using Tiema.Abstractions;


namespace ModbusSensor
{
    /// <summary>
    /// 模拟 Modbus 传感器插件：周期性生成温度数据并写入 Tag 系统。
    /// Simulated Modbus sensor plugin: periodically generates temperature data and writes to the Tag system.
    /// </summary>
    public class ModbusSensorPlugin : PluginBase
    {
        /// <summary>
        /// 插件名称 / Plugin name
        /// </summary>
        public override string Name => "ModbusSensor";

        /// <summary>
        /// 用于生成模拟数据的随机数生成器（线程不安全，仅用于示例）。
        /// Random number generator used to simulate data (not thread-safe, used for demo).
        /// </summary>
        private Random _random = new Random();

        /// <summary>
        /// 模拟传感器当前读数（默认 25°C）。
        /// Simulated current sensor value (default 25°C).
        /// </summary>
        private int _sensorValue = 25;

        /// <summary>
        /// 执行周期逻辑：模拟读取温度并写入 Tag 系统，同时输出日志。
        /// Execution logic per cycle: simulate reading temperature, write to Tag system, and log.
        /// </summary>
        public override void Execute()
        {
            // 模拟读取温度（25°C ± 5°C）
            // Simulate reading temperature (25°C ± 5°C)
            _sensorValue = 25 + _random.Next(-5, 7);

            // 将温度写入 Tag 系统，供其它插件读取/处理
            // Write the temperature into the Tag system for other plugins to read/process
            Context.Tags.SetTag("Plant/Temperature", _sensorValue);

            // 输出当前温度到控制台（便于演示）
            // Print current temperature to console for demo purposes
            Console.WriteLine($"[{Name}]  Temperature: {_sensorValue}°C");
        }
    }
}
