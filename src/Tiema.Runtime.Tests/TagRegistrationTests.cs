using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Services;
using Xunit;

namespace Tiema.Runtime.Tests
{
    public class TagRegistrationTests
    {
        [Fact]
        public async Task RegistrationAndPublish_ShouldNotifySubscriberAndGetTag()
        {
            // Arrange: in-memory registration/backplane and built-in tag service
            var reg = new InMemoryTagRegistrationManager();
            var backplane = new InMemoryBackplane();
            using var tagService = new BuiltInTagService(reg, backplane);

            // Simulate host registering module's producer
            var identities = reg.RegisterModuleTags("mod-producer", new[] { "Plant/Temperature" }, null);
            // Notify tag service so it can subscribe consumers (if any)
            tagService.OnTagsRegistered(identities);

            // Act: subscribe to path, publish via SetTag
            object received = null;
            using var sub = tagService.SubscribeTag("Plant/Temperature", v => received = v);

            // publish
            tagService.SetTag("Plant/Temperature", 123);

            // small wait to let async publish/subscribe complete
            await Task.Delay(100);

            // Assert: subscriber got value and GetTag returns it
            Assert.NotNull(received);
            Assert.Equal(123, Convert.ToInt32(received));

            var got = tagService.GetTag<int>("Plant/Temperature");
            Assert.Equal(123, got);
        }

        [Fact]
        public async Task PendingValue_PublishedOnRegistration()
        {
            // Arrange: in-memory registration/backplane and built-in tag service
            var reg = new InMemoryTagRegistrationManager();
            var backplane = new InMemoryBackplane();
            using var tagService = new BuiltInTagService(reg, backplane);

            // Subscribe BEFORE registration (simulates module subscribing early)
            int notifyCount = 0;
            object? received = null;
            using var sub = tagService.SubscribeTag("Plant/Temperature", v =>
            {
                Interlocked.Increment(ref notifyCount);
                received = v;
            });

            // Act: set tag BEFORE registration -> should be cached and local subscribers invoked immediately
            tagService.SetTag("Plant/Temperature", 321);
            await Task.Delay(100);

            Assert.Equal(1, notifyCount);
            Assert.Equal(321, Convert.ToInt32(received));

            // Now host registers the tag and notifies tag service
            var identities = reg.RegisterModuleTags("mod-producer", new[] { "Plant/Temperature" }, null);
            tagService.OnTagsRegistered(identities);

            // allow async publish to complete
            await Task.Delay(150);

            // Verify backplane has the value for the assigned handle
            var id = identities.First();
            var last = await backplane.GetLastValueAsync(id.Handle);
            Assert.Equal(321, Convert.ToInt32(last));

            // Ensure subscriber was not notified a second time by OnTagsRegistered (policy: we invoked subscriber on SetTag)
            Assert.Equal(1, notifyCount);
        }

        [Fact]
        public async Task GetTag_WhenNoValue_ReturnsDefaultAndDoesNotThrow()
        {
            using var reg = new InMemoryTagRegistrationManager();
            using var backplane = new InMemoryBackplane();
            using var tagService = new BuiltInTagService(reg, backplane);

            // Register tag but do not publish value
            var identities = reg.RegisterModuleTags("mod-p", new[] { "Alarms/Active" }, null);
            tagService.OnTagsRegistered(identities);

            // read before any SetTag -> should return default(bool)=false without exception
            var val = tagService.GetTag<bool>("Alarms/Active");
            Assert.False(val);

            // TryGet should return false
            Assert.False(tagService.TryGetTag<bool>("Alarms/Active", out var outVal));
            Assert.False(outVal);
        }

        [Fact]
        public async Task GetTag_ReturnsPendingBeforeAndAfterRegistration()
        {
            var reg = new InMemoryTagRegistrationManager();
            var backplane = new InMemoryBackplane();
            using var tagService = new BuiltInTagService(reg, backplane);

            // Set pending before registration
            tagService.SetTag("X/Temp", "hello");

            // Before registration, GetTag should return pending value
            var before = tagService.GetTag<string>("X/Temp");
            Assert.Equal("hello", before);

            // Register and notify
            var identities = reg.RegisterModuleTags("mod-p", new[] { "X/Temp" }, null);
            tagService.OnTagsRegistered(identities);

            await Task.Delay(100);

            // After registration, backplane should hold the value and GetTag returns it
            var id = identities.First();
            var stored = await backplane.GetLastValueAsync(id.Handle);
            Assert.Equal("hello", stored as string);

            var after = tagService.GetTag<string>("X/Temp");
            Assert.Equal("hello", after);
        }
    }
}