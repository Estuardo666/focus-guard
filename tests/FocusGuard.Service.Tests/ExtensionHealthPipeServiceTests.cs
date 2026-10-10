using FocusGuard.Contracts;
using FocusGuard.Domain;
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

    [Fact]
    public async Task StatusRequestDoesNotClearAcknowledgedBrowserPolicy()
    {
        var pipeName = $"FocusGuard.Tests.Health.StatusReadOnly.{Guid.NewGuid():N}";
        var health = new ExtensionHealthState();
        health.RecordHeartbeat(DateTimeOffset.UtcNow);
        var coordinator = new PolicyDistributionCoordinator(
            new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver());
        var focus = new InMemoryFocusService(
            extensionReadyProvider: () => health.IsExtensionReady(DateTimeOffset.UtcNow),
            policyDistribution: coordinator);
        var activation = await focus.ActivateAsync(
            new ActivateSessionRequest(
                Guid.NewGuid(),
                3600,
                StrictMode: false,
                RecoveryDelaySeconds: 0,
                ApplicationRules:
                [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "FocusGuard.TestTarget.exe")],
                WebsiteRules:
                [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
                EnforcementEnabled: true),
            CancellationToken.None);
        Assert.True(activation.Value!.Accepted);

        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        var browserAcknowledgement = new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash);
        coordinator.ExchangeBrowserAcknowledgement(browserAcknowledgement);
        var before = await focus.GetStatusAsync(CancellationToken.None);
        Assert.Equal("Active", before.SessionState);
        Assert.True(before.EnforcementConnected);

        using var server = new ExtensionHealthPipeService(
            health,
            focus,
            coordinator,
            pipeName: pipeName);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var client = new NamedPipeExtensionHealthClient(pipeName, TimeSpan.FromSeconds(2));
            var response = await client.QueryAsync(CreateRequest("status"), CancellationToken.None);

            Assert.True(response.ServiceReady);
            Assert.True(response.ExtensionReady);
            Assert.True(response.EnforcementConfirmed);
            Assert.Equal(delivery.PolicyId, response.Policy?.PolicyId);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }

        var after = await focus.GetStatusAsync(CancellationToken.None);
        Assert.Equal("Active", after.SessionState);
        Assert.True(after.EnforcementConnected);
        Assert.Equal(before.SessionId, after.SessionId);
    }

    private static ExtensionHealthPipeService CreateService(string pipeName) => new(
        new ExtensionHealthState(),
        new InMemoryFocusService(),
        pipeName: pipeName);

    private static ExtensionHealthRequest CreateRequest(string type = "heartbeat") => new(
        1,
        type,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow);
}
