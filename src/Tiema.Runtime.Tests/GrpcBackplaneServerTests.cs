using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tiema.Protocols.V1;
using Tiema.Runtime.Services;
using Xunit;

namespace Tiema.Runtime.Tests
{
    public class GrpcBackplaneServerTests
    {
        // Helper fake server stream writer to capture server pushes
        private sealed class FakeServerStreamWriter : IServerStreamWriter<Update>
        {
            public readonly ConcurrentBag<Update> Received = new();

            public WriteOptions WriteOptions { get; set; }

            public Task WriteAsync(Update message)
            {
                Received.Add(message);
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task RegisterPublish_GetLastValue_ShouldReturnPublishedValue()
        {
            // arrange
            var server = new GrpcBackplaneServer();

            // register a producer path; server assigns handle
            var regReq = new RegisterModuleTagsRequest
            {
                ModuleInstanceId = "test-producer"
            };
            regReq.ProducerPaths.Add("Plant/Temperature");

            var regResp = await server.RegisterModuleTags(regReq, TestServerCallContext.Create());
            Assert.NotNull(regResp);
            Assert.Single(regResp.Identities);
            var identity = regResp.Identities[0];
            var handle = identity.Handle;

            // publish an integer value wrapped in Int32Value -> Any
            var intWrapper = new Int32Value { Value = 123 };
            var any = Google.Protobuf.WellKnownTypes.Any.Pack(intWrapper);

            var pubReq = new PublishRequest
            {
                Handle = handle,
                Value = any,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Quality = 0,
                SourceModuleId = "test-producer"
            };

            var pubResp = await server.Publish(pubReq, TestServerCallContext.Create());
            Assert.True(pubResp.Ok);

            // read back last value
            var getReq = new GetRequest { Handle = handle };
            var getResp = await server.GetLastValue(getReq, TestServerCallContext.Create());
            Assert.True(getResp.Found);
            Assert.NotNull(getResp.Value);

            // unpack Any -> Int32Value
            var unpacked = getResp.Value.Unpack<Int32Value>();
            Assert.Equal(123, unpacked.Value);
        }

        [Fact]
        public async Task Subscribe_ShouldReceivePublishedUpdates()
        {
            // arrange
            var server = new GrpcBackplaneServer();

            // register path
            var regReq = new RegisterModuleTagsRequest
            {
                ModuleInstanceId = "producer-2"
            };
            regReq.ProducerPaths.Add("Plant/Pressure");
            var regResp = await server.RegisterModuleTags(regReq, TestServerCallContext.Create());
            var handle = regResp.Identities[0].Handle;

            // create fake writer and inject into server's private _subscribers via reflection
            var fakeWriter = new FakeServerStreamWriter();

            // reflectively get _subscribers field
            var field = typeof(GrpcBackplaneServer).GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var subs = (ConcurrentDictionary<uint, List<IServerStreamWriter<Update>>>)field.GetValue(server);
            // add list if not exists
            var list = subs.GetOrAdd(handle, _ => new List<IServerStreamWriter<Update>>());
            lock (list)
            {
                list.Add(fakeWriter);
            }

            // publish value
            var intWrapper = new Int32Value { Value = 777 };
            var any = Google.Protobuf.WellKnownTypes.Any.Pack(intWrapper);
            var pubReq = new PublishRequest
            {
                Handle = handle,
                Value = any,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Quality = 0,
                SourceModuleId = "producer-2"
            };

            var pubResp = await server.Publish(pubReq, TestServerCallContext.Create());
            Assert.True(pubResp.Ok);

            // small delay for async broadcast tasks to complete
            await Task.Delay(50);

            // assert the fake writer received update
            Assert.False(fakeWriter.Received.IsEmpty);
            var received = fakeWriter.Received.First();
            var unpack = received.Value.Unpack<Int32Value>();
            Assert.Equal(777, unpack.Value);

            // cleanup: remove fake writer
            lock (list)
            {
                list.Remove(fakeWriter);
            }
        }
    }

    // Minimal TestServerCallContext helper to pass into server methods (no networking)
    internal static class TestServerCallContext
    {
        public static ServerCallContext Create()
        {
            // create a dummy ServerCallContext by using the internal helper in Grpc.Core testing is not available,
            // but server methods here only read CancellationToken from context; to be safe create a simple derived stub.
            return new SimpleServerCallContext();
        }

        private sealed class SimpleServerCallContext : ServerCallContext
        {
            private readonly CancellationTokenSource _cts = new();
            protected override string MethodCore => "test";
            protected override string HostCore => "localhost";
            protected override string PeerCore => "test-peer";
            protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
            protected override Metadata RequestHeadersCore => new Metadata();
            protected override CancellationToken CancellationTokenCore => _cts.Token;
            protected override Metadata ResponseTrailersCore => new Metadata();
            protected override Status StatusCore { get; set; }
            protected override WriteOptions? WriteOptionsCore { get; set; }
            protected override AuthContext AuthContextCore => new AuthContext("", new Dictionary<string, List<AuthProperty>>());
            protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions options) => null;
            protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
        }
    }
}