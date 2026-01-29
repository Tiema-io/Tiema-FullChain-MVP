using System;
using System.Collections.Generic;
using System.Text;
using Tiema.Abstractions;


namespace TemperatureLogic
{
    // 2. 逻辑处理插件
    public class TemperatureLogicPlugin : PluginBase
    {
        public override string Name => "TemperatureLogic";

        private const int ALARM_THRESHOLD = 30;

        public override void Execute(ICycleContext context)
        {
            // 从Tag系统读取温度
            var temperature = Context.Tags.GetTag<int>("Plant/Temperature");

            // 逻辑判断
            if (temperature > ALARM_THRESHOLD)
            {

                //订阅了alarm.high_temperature的SimpleAlarm，会得到消息
                Context.Messages.Publish("alarm.high_temperature", new
                {
                    Temperature = temperature,
                    Threshold = ALARM_THRESHOLD,
                    Message = "温度超限!"
                });

                Console.WriteLine($"[{Name}] ⚠️ 高温报警: {temperature}°C > {ALARM_THRESHOLD}°C");
            }
        }
    }
}
