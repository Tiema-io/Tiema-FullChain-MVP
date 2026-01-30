using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;

namespace TemperatureLogic
{
    /// <summary>
    /// 逻辑处理插件：读取温度并在超限时发布高温报警消息。
    /// Logic processing plugin: reads temperature and publishes a high-temperature alarm message when threshold is exceeded.
    /// </summary>
    public class TemperatureLogicPlugin : PluginBase
    {
        /// <summary>
        /// 插件名称 / Plugin name
        /// </summary>
        public override string Name => "TemperatureLogic";

        /// <summary>
        /// 报警阈值（摄氏度），超出此值将触发报警消息发布。
        /// Alarm threshold (Celsius). Exceeding this value triggers an alarm message.
        /// </summary>
        private const int ALARM_THRESHOLD = 30;

        /// <summary>
        /// 执行周期逻辑：从 Tag 系统读取温度并根据阈值决定是否发布报警消息。
        /// Execution logic per cycle: read temperature from Tag system and publish alarm message if threshold exceeded.
        /// </summary>
        public override void Execute()
        {
            // 从 Tag 系统读取温度（示例 key: "Plant/Temperature"）
            // Read temperature from Tag system (example key: "Plant/Temperature")
            var temperature = Context.Tags.GetTag<int>("Plant/Temperature");

            // 判断是否超过阈值，超过则发布高温报警消息，订阅了 "alarm.high_temperature" 的插件将被通知
            // Check threshold and publish high-temperature alarm message; plugins subscribed to "alarm.high_temperature" will be notified.
            if (temperature > ALARM_THRESHOLD)
            {
                Context.Messages.Publish("alarm.high_temperature", new
                {
                    Temperature = temperature,
                    Threshold = ALARM_THRESHOLD,
                    Message = "温度超限! / Temperature exceeded!"
                });

                // 控制台输出用于演示与调试
                // Console output for demo and debugging
                Console.WriteLine($"[{Name}] ⚠️ 高温报警: {temperature}°C > {ALARM_THRESHOLD}°C / High temp alarm: {temperature}°C > {ALARM_THRESHOLD}°C");
            }
        }
    }
}
