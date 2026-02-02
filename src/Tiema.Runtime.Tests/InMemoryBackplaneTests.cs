using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Runtime.Services;
using Xunit;

namespace Tiema.Runtime.Tests
{
    public class InMemoryBackplaneTests
    {
        [Fact]
        public async Task Publish_GetLastValue_ReturnsPublishedValue()
        {
            var backplane = new InMemoryBackplane();

            uint handle = 1;
            await backplane.PublishAsync(handle, 123);

            var result = await backplane.GetLastValueAsync(handle);
            Assert.NotNull(result);
            Assert.Equal(123, Convert.ToInt32(result));
        }

        [Fact]
        public async Task Subscribe_ReceivePublishedUpdate()
        {
            var backplane = new InMemoryBackplane();
            uint handle = 2;

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sub = backplane.Subscribe(handle, value =>
            {
                tcs.TrySetResult(value);
            });

            await backplane.PublishAsync(handle, "hello");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(200));
            Assert.True(completed == tcs.Task, "Timed out waiting for subscriber callback");
            Assert.Equal("hello", tcs.Task.Result as string);
        }

        [Fact]
        public async Task Unsubscribe_NoLongerReceivesUpdates()
        {
            var backplane = new InMemoryBackplane();
            uint handle = 3;

            var received = new ConcurrentBag<object>();
            var sub = backplane.Subscribe(handle, value => received.Add(value));

            // publish once -> should be received
            await backplane.PublishAsync(handle, 1);
            await Task.Delay(50);
            Assert.Contains(1, received);

            // dispose subscription
            sub.Dispose();

            // publish again -> should NOT be received
            await backplane.PublishAsync(handle, 2);
            await Task.Delay(100);
            Assert.DoesNotContain(2, received);
        }

        [Fact]
        public async Task MultipleSubscribers_AllReceiveUpdates()
        {
            var backplane = new InMemoryBackplane();
            uint handle = 4;

            var bag1 = new ConcurrentBag<object>();
            var bag2 = new ConcurrentBag<object>();

            using var s1 = backplane.Subscribe(handle, v => bag1.Add(v));
            using var s2 = backplane.Subscribe(handle, v => bag2.Add(v));

            await backplane.PublishAsync(handle, "x");
            await Task.Delay(50);

            Assert.Contains("x", bag1);
            Assert.Contains("x", bag2);
        }
    }
}