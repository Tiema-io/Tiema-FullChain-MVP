using System.ComponentModel.DataAnnotations;
using Tiema.Abstractions;

namespace CpuLogicPlugin
{
    // 1. CPU逻辑插件 (用户编写的业务逻辑)
    public class CpuLogicPlugin : PluginBase
    {
        private IBackplaneClient _backplane;

        public override void Initialize(IPluginContext ctx)
        {
            _backplane = ctx.GetService<IBackplaneClient>();

            // 注册需要访问的数据点
            _backplane.RegisterDataPoint("Device1/Temperature",
                new DataPointInfo
                {
                    SourcePlugin = "ModbusDriver",
                    Address = "40001",
                    DataType = DataType.Float
                });
        }
        public override void Execute(CycleContext ctx)
        {
            // 发起数据读取请求
            var request = new ReadRequest
            {
                RequestId = Guid.NewGuid(),
                DataPoint = "Device1/Temperature",
                Timestamp = DateTime.UtcNow
            };

            // 通过Backplane发送到Modbus插件
            var response = await _backplane.SendRequest<ReadResponse>(
                "ModbusDriver",  // 目标插件
                request,         // 请求内容
                TimeSpan.FromSeconds(1)  // 超时
            );

            if (response.Success)
            {
                var temperature = response.Value;
                // 业务逻辑处理...
                Console.WriteLine($"CPU插件读到温度: {temperature}°C");
            }
        }
    }
}
