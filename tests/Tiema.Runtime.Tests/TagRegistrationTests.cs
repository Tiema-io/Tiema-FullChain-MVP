using System;
using System.Linq;
using System.Threading.Tasks;
using Tiema.Runtime.Services;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime.Services;
using Tiema.Hosting.Abstractions;
using Tiema.Runtime;
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
            var tagService = new BuiltInTagService(reg, backplane);

            // Simulate host registering module's producer
            var identities = reg.RegisterModuleTags("mod-producer", new[] { "Plant/Temperature" }, null);
            // Notify tag service so it can subscribe consumers (if any)
            tagService.OnTagsRegistered(identities);

            // Act: subscribe to path, publish via SetTag
            object received = null;
            var sub = tagService.SubscribeTag("Plant/Temperature", v => received = v);

            // publish
            tagService.SetTag("Plant/Temperature", 123);

            // small wait to let async publish/subscribe complete
            await Task.Delay(50);

            // Assert: subscriber got value and GetTag returns it
            Assert.NotNull(received);
            Assert.Equal(123, Convert.ToInt32(received));

            var got = tagService.GetTag<int>("Plant/Temperature");
            Assert.Equal(123, got);

            sub.Dispose();
        }

        [Fact]
        public async Task GetTag_WhenNoValue_ReturnsDefaultAndDoesNotThrow()
        {
            var reg = new InMemoryTagRegistrationManager();
            var backplane = new InMemoryBackplane();
            var tagService = new BuiltInTagService(reg, backplane);

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
    }
}