using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Monitoring;

namespace FocusGuard.Service.Tests;

public sealed class EnforcementRestartTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"FocusGuard.Restart.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ActiveStrictPolicySurvivesRestartAndEachChannelReacknowledgesIndependently()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(now);
        var store = new StrictSessionStore(_directory, () => "boot-before-restart");
        var firstReceiver = new ProcessMonitorPolicyReceiver(clock);
        var firstCoordinator = new PolicyDistributionCoordinator(firstReceiver, clock);
        var firstService = new InMemoryFocusService(
            clock,
            store,
            extensionReadyProvider: () => true,
            policyDistribution: firstCoordinator);
        var recovery = RecoveryCodeService.Generate();
        var activation = await firstService.ActivateAsync(
            new ActivateSessionRequest(
                Guid.NewGuid(),
                300,
                true,
                900,
                recovery.Credential,
                ApplicationRules:
                [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "FocusGuard.TestTarget.exe")],
                WebsiteRules:
                [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
                EnforcementEnabled: true),
            CancellationToken.None);
        Assert.True(activation.Value!.Accepted);

        var firstDelivery = firstCoordinator.ExchangeBrowserAcknowledgement(null)!;
        firstCoordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(firstDelivery.PolicyId, firstDelivery.PolicyHash));
        Assert.True((await firstService.GetStatusAsync(CancellationToken.None)).EnforcementConnected);

        var restartedReceiver = new ProcessMonitorPolicyReceiver(clock);
        var restartedCoordinator = new PolicyDistributionCoordinator(restartedReceiver, clock);
        var restartedStore = new StrictSessionStore(_directory, () => "boot-after-restart");
        var restartedService = new InMemoryFocusService(
            clock,
            restartedStore,
            extensionReadyProvider: () => true,
            policyDistribution: restartedCoordinator);

        var pending = await restartedService.GetStatusAsync(CancellationToken.None);
        var recoveredDelivery = restartedCoordinator.ExchangeBrowserAcknowledgement(null)!;
        Assert.Equal("Active", pending.SessionState);
        Assert.False(pending.EnforcementConnected);
        Assert.NotNull(restartedReceiver.GetActiveDelivery());
        Assert.Equal(firstDelivery.Policy.SessionId, recoveredDelivery.Policy.SessionId);
        Assert.Equal(firstDelivery.Policy.ApplicationRules, recoveredDelivery.Policy.ApplicationRules);
        Assert.Equal(firstDelivery.Policy.WebsiteRules, recoveredDelivery.Policy.WebsiteRules);

        restartedCoordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(recoveredDelivery.PolicyId, recoveredDelivery.PolicyHash));
        var active = await restartedService.GetStatusAsync(CancellationToken.None);
        var stillActive = await restartedService.GetStatusAsync(CancellationToken.None);
        Assert.Equal("Active", active.SessionState);
        Assert.True(active.EnforcementConnected);
        Assert.Equal("Active", stillActive.SessionState);
        Assert.True(stillActive.EnforcementConnected);
        Assert.NotNull(restartedReceiver.GetActiveDelivery());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
