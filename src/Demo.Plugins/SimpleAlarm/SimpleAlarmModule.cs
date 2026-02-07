using System;
using Tiema.Sdk;


namespace SimpleAlarm
{
    /// <summary>
    /// 报警插件：订阅高温报警并记录/标记告警状态（在 Initialize 中订阅，内部循环检查状态）。
    /// Alarm plugin: subscribes to high-temp alarms and records/flags alarm state (subscribe in Initialize, internal loop checks state).
    /// </summary>
    public class SimpleAlarmModule : PluginBase
    {
        /// <summary>
        /// 插件名称 / Plugin name
        /// </summary>
        public override string Name => "SimpleAlarm";

        /// <summary>
        /// 插件初始化钩子：在此处订阅消息或完成其它一次性初始化工作。
        /// Plugin initialization hook: subscribe to messages or perform one-time setup here.
        /// </summary>
        protected override void OnInitialize()
        {
            base.OnInitialize();

            // 订阅报警消息：当有高温报警时会回调 OnHighTemperature
            // Subscribe to alarm messages: OnHighTemperature will be called on alarm.
            Context.Messages.Subscribe("alarm.high_temperature", OnHighTemperature);

            // 声明本模块将生产的 Tag（避免 SetTag 时未注册）
            Context.Tags.DeclareProducer("Alarms/Active");
            Context.Tags.DeclareProducer("Alarms/LastMessage");
        }

        /// <summary>
        /// 高温报警回调：接收到报警后记录日志并在 Tag 系统中标记活动告警与最后一条消息。
        /// High-temperature alarm callback: logs the alarm and marks active alarm / last message in Tag system.
        /// </summary>
        /// <param name="message">来自消息系统的载荷 / payload from message system</param>
        private void OnHighTemperature(object message)
        {
            Console.WriteLine($"[{Name}] 🚨 接收到高温报警! / Received high-temperature alarm!");

            // 写入 Tag 系统：标记为有活动告警并保存最后一条消息
            // Write to Tag system: flag active alarm and save last message.
            Context.Tags.SetTag("Alarms/Active", true);
            Context.Tags.SetTag("Alarms/LastMessage", message);
        }

        /// <summary>
        /// 执行周期逻辑：每个周期检查告警状态并输出提示。
        /// Periodic execution logic: check alarm state each cycle and print a warning if active.
        /// </summary>
        protected override void Execute()
        {
            // 从 Tag 系统读取告警状态（若不存在默认返回 false）
            // Read alarm state from Tag system (defaults to false if missing).
            var hasAlarm = Context.Tags.GetTag<bool>("Alarms/Active");

            if (hasAlarm)
            {
                Console.WriteLine($"[{Name}]: warning  Alarms/Active=true");
            }
        }
    }
}
