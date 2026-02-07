using System;
using System.Threading.Tasks;
using Grpc.Net.Client;

using Google.Protobuf.WellKnownTypes;
using Tiema.Connect.Grpc.V1;
using Tiema.Tags.Grpc.V1;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // allow HTTP/2 without TLS in .NET (for local CI)
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        var host = Environment.GetEnvironmentVariable("DataConnect_HOST") ?? "127.0.0.1";
        var port = Environment.GetEnvironmentVariable("DataConnect_PORT") ?? "50051";
        var address = $"http://{host}:{port}";

        Console.WriteLine($"[smoke] Connecting to TB at {address}");

        try
        {
            using var channel = GrpcChannel.ForAddress(address);
            var client = new DataConnect.DataConnectClient(channel);

            // 1) Register a tag
            var registerInfo = new RegisterTagInfo
            {
                SourcePluginInstanceId = "smoketest-client",
                TagPath = "smoke/test/value",
                Role = TagRole.Producer
            };

            var regReq = new RegisterTagsRequest
            {
                PluginInstanceId = "smoketest-client",
            };
            regReq.Tags.Add(registerInfo);

            Console.WriteLine("[smoke] Registering tag...");
            var regResp = await client.RegisterTagsAsync(regReq);
            if (regResp == null || regResp.Assigned.Count == 0)
            {
                Console.WriteLine("[smoke][ERROR] RegisterTags returned no assigned entries");
                return 2;
            }

            var handle = regResp.Assigned[0].Handle;
            Console.WriteLine($"[smoke] Assigned handle: {handle}");

            // 2) Publish a TagValue
            var tv = new TagValue
            {
                Handle = handle,
                Path = "smoke/test/value",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                IntValue = 123,
                Quality = QualityCode.QualityGood,
                Role = TagRole.Producer,
                SourcePluginInstanceId = "smoketest-client"
            };

            var pubReq = new PublishRequest { Tag = tv };
            Console.WriteLine("[smoke] Publishing value...");
            var pubResp = await client.PublishAsync(pubReq);
            if (pubResp == null || !pubResp.Success)
            {
                Console.WriteLine("[smoke][ERROR] Publish failed: " + (pubResp?.Message ?? "(no message)"));
                return 3;
            }

            // small delay to allow server to process
            await Task.Delay(500);

            // 3) GetLastValue
            var getReq = new GetRequest { Handle = handle };
            var getResp = await client.GetLastValueAsync(getReq);
            if (getResp == null || !getResp.Found)
            {
                Console.WriteLine("[smoke][ERROR] GetLastValue did not find value");
                return 4;
            }

            Console.WriteLine("[smoke] GetLastValue succeeded. Value info:");
            Console.WriteLine($"  handle={getResp.Value.Handle}, int_value={getResp.Value.IntValue}");

            Console.WriteLine("[smoke] Smoke test succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[smoke][ERROR] Exception: " + ex);
            return 10;
        }
    }
}
