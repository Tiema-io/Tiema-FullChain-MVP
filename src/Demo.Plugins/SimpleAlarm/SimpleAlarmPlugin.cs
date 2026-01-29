using System;
using Tiema.Abstractions;


namespace SimpleAlarm
{
  
    // 3. 报警插件
    public class SimpleAlarmPlugin : PluginBase
    {
        public override string Name => "SimpleAlarm";

        public override void Initialize(IPluginContext context)
        {
            base.Initialize(context);

            // 订阅报警消息
            //OnHighTemperature会被回调
            context.Messages.Subscribe("alarm.high_temperature", OnHighTemperature);
        }

        private void OnHighTemperature(object message)
        {
            Console.WriteLine($"[{Name}] 🚨 接收到高温报警!");

            // 这里可以：发邮件、发短信、控制设备等
            // MVP中只打印日志

            // 写入Tag系统
            Context.Tags.SetTag("Alarms/Active", true);
            Context.Tags.SetTag("Alarms/LastMessage", message);
        }

        public override void Execute(ICycleContext context)
        {
            // 每个周期检查报警状态
            var hasAlarm = Context.Tags.GetTag<bool>("Alarms/Active");
            if (hasAlarm)
            {
                Console.WriteLine($"[{Name}]: warning  Alarms/Active=true");
            }
        }
    }
}
