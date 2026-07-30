using FocusGuard.Contracts;
using FocusGuard.NativeMessaging;
using FocusGuard.Service;

namespace FocusGuard.Service.Tests;

public sealed class ExtensionHealthPipeServiceTests
{
    [Fact]
    public async Task Client_ReportsDisconnectAndTimeout()
    {
        var disconnected = new NamedPipeExtensionHealthClient(
            $"FocusGuard.Tests.Missing.{Guid.NewGuid():N}",
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await disconnected.QueryAsync(CreateRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Client_ReconnectsAfterServiceRestartWithNewAuthoritativeInstance()
    {
        var pipeName = $"FocusGuard.Tests.Health.{Guid.NewGuid():N}";
        var first = CreateService(pipeName);
        await first.StartAsync(CancellationToken.None);
        try
        {
            var client = new NamedPipeExtensionHealthClient(pipeName, TimeSpan.FromSeconds(2));
            var firstResponse = await client.QueryAsync(CreateRequest(), CancellationToken.None);
            Assert.True(firstResponse.ServiceReady);
            Assert.True(firstResponse.ExtensionReady);
            Assert.Equal("Inactive", firstResponse.SessionState);

            await first.StopAsync(CancellationToken.None);
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await new NamedPipeExtensionHealthClient(pipeName, TimeSpan.FromMilliseconds(100))
                    .QueryAsync(CreateRequest(), CancellationToken.None));

            var second = CreateService(pipeName);
            await second.StartAsync(CancellationToken.None);
            try
            {
                var secondResponse = await client.QueryAsync(CreateRequest(), CancellationToken.None);
                Assert.True(secondResponse.ServiceReady);
                Assert.NotEqual(firstResponse.ServiceInstanceId, secondResponse.ServiceInstanceId);
            }
            finally
            {
                await second.StopAsync(CancellationToken.None);
                second.Dispose();
            }
        }
        finally
        {
            first.Dispose();
        }
    }

    private static ExtensionHealthPipeService CreateService(string pipeName) => new(
        new ExtensionHealthState(),
        new InMemoryFocusService(),
        pipeName: pipeName);

    private static ExtensionHealthRequest CreateRequest() => new(
        1,
        "heartbeat",
        Guid.NewGuid(),
        DateTimeOffset.UtcNow);
}
