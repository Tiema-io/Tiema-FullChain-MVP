using System;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Sdk;
using Tiema.Tags.Grpc.V1;
using Tiema.Contracts;     // TagRole for attribute

namespace TemperatureLogic
{
    /// <summary>
    /// 逻辑处理插件：读取温度并在超限时发布高温报警消息（使用宿主 Tags 订阅）。
    /// </summary>
    public class TemperatureLogicModule : PluginBase
    {
        public override string Name => "TemperatureLogic";

        private const int ALARM_THRESHOLD = 30;
        protected override int RunIntervalMs => 1000;

        private IDisposable? _subscription;

        // 标注为 Tiema Tag 的 consumer：宿主会自动为此路径注册并可建立订阅。
        [TiemaTag("Plant/Temperature", Role = TagRole.Consumer)]
        private int LatestTemperature { get; set; }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            // 不在此处建立网络订阅或 DeclareConsumer；由宿主统一注册
        }

        protected override void OnStart()
        {
            base.OnStart();

            // 订阅宿主提供的 Tags 服务（如果宿主的 TagAutoRegistrar 已建立订阅，此处作为保底/兼容）
            _subscription = Context.Tags.SubscribeTag("Plant/Temperature", OnTemperatureUpdated);
        }

        private void OnTemperatureUpdated(object value)
        {
            if (value is int ti)
            {
                HandleTemperature(ti);
            }
            else if (value is long tl)
            {
                HandleTemperature((int)tl);
            }
            else if (value is double td)
            {
                HandleTemperature((int)Math.Round(td));
            }
            else if (value is string ts && int.TryParse(ts, out var ti2))
            {
                HandleTemperature(ti2);
            }
            else
            {
                Console.WriteLine($"[{Name}] Received unexpected tag payload type: {value?.GetType().Name}");
            }
        }

        private void HandleTemperature(int temp)
        {
            Console.WriteLine($"[{Name}] 自动接收温度: {temp}°C / Auto-received temperature: {temp}°C");
            if (temp > ALARM_THRESHOLD)
            {
                Context.Messages.Publish("alarm.high_temperature", new { Temperature = temp, Threshold = ALARM_THRESHOLD });
                Console.WriteLine($"[{Name}] ⚠️ 高温报警: {temp}°C > {ALARM_THRESHOLD}°C");
            }
        }

        protected override void OnStop()
        {
            base.OnStop();
            try { _subscription?.Dispose(); } catch { }
        }

        protected override void Execute()
        {
            // 逻辑由订阅驱动，周期逻辑可留空或保留备用检查
        }
    }
}
