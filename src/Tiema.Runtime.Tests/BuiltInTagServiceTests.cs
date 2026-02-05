using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Hosting.Abstractions;
using Tiema.Protocols.V1;
using Tiema.Runtime.Services;
using Xunit;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;

namespace Tiema.Runtime.Tests
{
    /// <summary>
    /// Tests for BuiltInTagService:
    /// - 验证收到 protobuf TagValue / Any 时会被自动解包为 CLR 基本类型并分发给订阅者。
    /// - 验证多个本地订阅者能收到解包后的值。
    /// </summary>
    public class BuiltInTagServiceTests
    {
        // 简单的 ITagRegistrationManager stub：按 path 返回事先注册的 TagIdentity
        private sealed class StubRegistrationManager : ITagRegistrationManager
        {
            private readonly ConcurrentDictionary<string, TagIdentity> _byPath = new(StringComparer.OrdinalIgnoreCase);
            private readonly ConcurrentDictionary<uint, TagIdentity> _byHandle = new();

            public void Add(TagIdentity id)
            {
                _byPath[id.Path] = id;
                _byHandle[id.Handle] = id;
            }

            public System.Collections.Generic.IReadOnlyList<TagIdentity> RegisterModuleTags(string moduleInstanceId, System.Collections.Generic.IEnumerable<string> producerPaths, System.Collections.Generic.IEnumerable<string> consumerPaths)
            {
                throw new NotImplementedException();
            }

            public TagIdentity? GetByHandle(uint handle)
            {
                _byHandle.TryGetValue(handle, out var id);
                return id;
            }

            public TagIdentity? GetByPath(string path)
            {
                _byPath.TryGetValue(path, out var id);
                return id;
            }
        }

        [Fact]
        public async Task Subscribe_Receives_Unpacked_TagValue_Int()
        {
            // arrange
            var backplane = new InMemoryBackplane();
            var reg = new StubRegistrationManager();
            var path = "Plant/Temperature";
            var handle = 101u;
            reg.Add(new TagIdentity(handle, path, TagRole.Producer, "mod1"));

            var svc = new BuiltInTagService(reg, backplane);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = svc.SubscribeTag(path, v => tcs.TrySetResult(v));

            // act: publish a TagValue with int (int_value -> Int64 in proto)
            var tag = new TagValue
            {
                Handle = handle,
                IntValue = 123L,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            };

            await backplane.PublishAsync(handle, tag);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
            Assert.True(completed == tcs.Task, "Timed out waiting for subscriber callback");

            // assert: helper should unpack to CLR numeric (Int64)
            var received = tcs.Task.Result;
            Assert.NotNull(received);
            Assert.IsType<long>(received);
            Assert.Equal(123L, (long)received);
        }

        [Fact]
        public async Task Subscribe_Receives_Unpacked_AnyWrapped_TagValue_String()
        {
            // arrange
            var backplane = new InMemoryBackplane();
            var reg = new StubRegistrationManager();
            var path = "Plant/Status";
            var handle = 202u;
            reg.Add(new TagIdentity(handle, path, TagRole.Producer, "mod2"));

            var svc = new BuiltInTagService(reg, backplane);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sub = svc.SubscribeTag(path, v => tcs.TrySetResult(v));

            // act: create TagValue string, pack into Any and publish the Any (simulate transport that sends Any)
            var tag = new TagValue
            {
                Handle = handle,
                StringValue = "ok",
            
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            };

            var any = Any.Pack(tag);

            // publish Any (InMemoryBackplane preserves object as-is)
            await backplane.PublishAsync(handle, any);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
            Assert.True(completed == tcs.Task, "Timed out waiting for subscriber callback");

            var received = tcs.Task.Result;
            Assert.NotNull(received);
            Assert.IsType<string>(received);
            Assert.Equal("ok", (string)received);
        }

        [Fact]
        public async Task MultipleSubscribers_Receive_Unpacked_Values()
        {
            // arrange
            var backplane = new InMemoryBackplane();
            var reg = new StubRegistrationManager();
            var path = "Plant/Pressure";
            var handle = 303u;
            reg.Add(new TagIdentity(handle, path, TagRole.Producer, "mod3"));

            var svc = new BuiltInTagService(reg, backplane);

            var bag1 = new ConcurrentBag<object>();
            var bag2 = new ConcurrentBag<object>();

            using var s1 = svc.SubscribeTag(path, v => bag1.Add(v));
            using var s2 = svc.SubscribeTag(path, v => bag2.Add(v));

            // act: publish TagValue double
            var tag = new TagValue
            {
                Handle = handle,
                DoubleValue = 3.14,
         
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            };

            await backplane.PublishAsync(handle, tag);

            // small delay for async dispatch
            await Task.Delay(100);

            // assert: both bags got a double value unpacked
            Assert.Contains(bag1, o => o is double && Math.Abs((double)o - 3.14) < 1e-6);
            Assert.Contains(bag2, o => o is double && Math.Abs((double)o - 3.14) < 1e-6);
        }
    }
}