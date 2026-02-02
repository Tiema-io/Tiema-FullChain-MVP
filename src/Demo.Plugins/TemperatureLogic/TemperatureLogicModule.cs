using System;
using System.Threading.Tasks;
using Tiema.Contracts;
using Tiema.Sdk;


namespace TemperatureLogic
{
    /// <summary>
    /// 逻辑处理插件：读取温度并在超限时发布高温报警消息（在内部循环中运行）。
    /// Logic processing plugin: reads temperature and publishes high-temp alarm when threshold exceeded (runs in internal loop).
    /// </summary>
    public class TemperatureLogicModule : ModuleBase
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
        /// 执行周期（毫秒），插件将在此周期内重复执行逻辑。
        /// Run interval in milliseconds. The plugin logic executes repeatedly in this interval.
        /// </summary>
        protected override int RunIntervalMs => 1000;

        /// <summary>
        /// 初始化逻辑，插件加载时调用。
        /// Initialization logic, called when the plugin is loaded.
        /// </summary>
        protected override void OnInitialize()
        {
            base.OnInitialize();
            // 声明 Consumer Tag 并订阅更新。
            // Declare consumer tag and subscribe to updates.
            Context.Tags.DeclareConsumer("Plant/Temperature");
            _subscription = Context.Tags.SubscribeTag("Plant/Temperature", OnTemperatureUpdated);
        }

        private IDisposable _subscription;

        private void OnTemperatureUpdated(object value)
        {
            if (value is int temp)
            {
                Console.WriteLine($"[{Name}] 自动接收温度: {temp}°C / Auto-received temperature: {temp}°C");
                if (temp > ALARM_THRESHOLD)
                {
                    Context.Messages.Publish("alarm.high_temperature", new { Temperature = temp, Threshold = ALARM_THRESHOLD });
                    Console.WriteLine($"[{Name}] ⚠️ 高温报警: {temp}°C > {ALARM_THRESHOLD}°C");
                }
            }
        }

        /// <summary>
        /// 执行周期逻辑：从 Tag 系统读取温度并根据阈值决定是否发布报警消息。
        /// Execution logic per cycle: read temperature from Tag system and publish alarm message if threshold exceeded.
        /// </summary>
        protected override void Execute()
        {
            // 不再需要手动 GetTag，数据通过订阅自动推送。
            // No longer need manual GetTag; data is pushed via subscription.
        }

        /// <summary>
        /// 停止逻辑，插件卸载时调用。
        /// Stop logic, called when the plugin is unloaded.
        /// </summary>
        protected override void OnStop()
        {
            base.OnStop();
            _subscription?.Dispose();
        }
    }
}
