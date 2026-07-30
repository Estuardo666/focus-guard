using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service.Tests;

public sealed class PolicyDistributionCoordinatorTests
{
    [Fact]
    public async Task ProcessHeartbeatHealthDoesNotDependOnAlreadyConfirmedEnforcement()
    {
        var relay = new ProcessAgentPolicyRelay();
        var coordinator = new PolicyDistributionCoordinator(relay);
        var service = new InMemoryFocusService(
            extensionReadyProvider: () => true,
            policyDistribution: coordinator,
            processReadyProvider: relay.IsReady);
        await service.ActivateAsync(Request(), CancellationToken.None);
        relay.RecordHeartbeat();
        var delivery = relay.CurrentDelivery!;

        coordinator.ExchangeProcessAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));

        var status = await service.GetStatusAsync(CancellationToken.None);
        Assert.True(status.EnforcementConnected);
        Assert.Equal("Active", status.SessionState);
    }

    [Fact]
    public async Task AggregateConnectedOnlyAfterBothExactAcknowledgements()
    {
        var target = new RecordingTarget();
        var coordinator = new PolicyDistributionCoordinator(target);
        var service = new InMemoryFocusService(policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);

        var pending = await service.GetStatusAsync(CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        var connected = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(delivery.PolicyId, target.Acknowledgement!.PolicyId);
        Assert.Equal(delivery.PolicyHash, target.Acknowledgement.PolicyHash);
        Assert.Equal("Active", pending.SessionState);
        Assert.False(pending.EnforcementConnected);
        Assert.Equal("Active", connected.SessionState);
        Assert.True(connected.EnforcementConnected);
    }

    [Fact]
    public async Task StaleBrowserPolicyCannotConfirmCurrentDelivery()
    {
        var coordinator = new PolicyDistributionCoordinator(new RecordingTarget());
        var service = new InMemoryFocusService(policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;

        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(Guid.NewGuid(), delivery.PolicyHash));
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Active", status.SessionState);
        Assert.False(status.EnforcementConnected);
    }

    [Fact]
    public async Task FallenProcessComponentLeavesSessionDegraded()
    {
        var coordinator = new PolicyDistributionCoordinator(new ThrowingTarget());
        var service = new InMemoryFocusService(policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Active", status.SessionState);
        Assert.False(status.EnforcementConnected);
    }

    [Fact]
    public async Task StaleProcessAcknowledgementLeavesSessionDegraded()
    {
        var coordinator = new PolicyDistributionCoordinator(new StaleTarget());
        var service = new InMemoryFocusService(policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));

        Assert.False((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);
    }

    [Fact]
    public async Task FallenBrowserComponentDegradesAggregateButKeepsProcessChannel()
    {
        var browserReady = true;
        var processReceiver = new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver();
        var coordinator = new PolicyDistributionCoordinator(processReceiver);
        var service = new InMemoryFocusService(
            extensionReadyProvider: () => browserReady,
            policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        Assert.True((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);

        browserReady = false;
        var degraded = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(degraded.ExtensionReady);
        Assert.False(degraded.EnforcementConnected);
        Assert.NotNull(processReceiver.GetActiveDelivery());
        Assert.True(coordinator.IsProcessConnected);
        Assert.False(coordinator.IsBrowserConnected);
    }

    [Fact]
    public async Task BrowserReconnectRequiresFreshExactAckWithoutInterruptingProcessChannel()
    {
        var browserReady = true;
        var processReceiver = new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver();
        var coordinator = new PolicyDistributionCoordinator(processReceiver);
        var service = new InMemoryFocusService(
            extensionReadyProvider: () => browserReady,
            policyDistribution: coordinator);
        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        var exactAck = new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash);
        coordinator.ExchangeBrowserAcknowledgement(exactAck);
        Assert.NotNull(processReceiver.GetActiveDelivery());

        browserReady = false;
        await service.GetStatusAsync(CancellationToken.None);
        browserReady = true;

        Assert.False((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);
        Assert.NotNull(processReceiver.GetActiveDelivery());

        coordinator.ExchangeBrowserAcknowledgement(exactAck);
        Assert.True((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);
        Assert.NotNull(processReceiver.GetActiveDelivery());
    }

    [Fact]
    public async Task ApplicationOnlyPolicyConfirmsWithoutBrowserExtension()
    {
        var relay = new ProcessAgentPolicyRelay();
        relay.RecordHeartbeat();
        var coordinator = new PolicyDistributionCoordinator(relay);
        var service = new InMemoryFocusService(
            extensionReadyProvider: () => false,
            processReadyProvider: relay.IsReady,
            policyDistribution: coordinator);

        await service.ActivateAsync(ApplicationOnlyRequest(), CancellationToken.None);
        var delivery = relay.CurrentDelivery!;
        coordinator.ExchangeProcessAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Active", status.SessionState);
        Assert.False(status.ExtensionReady);
        Assert.True(status.EnforcementConnected);
        Assert.True(coordinator.IsProcessConnected);
        Assert.True(relay.EnforcementConfirmed);
    }

    [Fact]
    public async Task MissingBrowserLeaseNeverRevokesConfirmedProcessLease()
    {
        var relay = new ProcessAgentPolicyRelay();
        relay.RecordHeartbeat();
        var coordinator = new PolicyDistributionCoordinator(relay);
        var service = new InMemoryFocusService(
            extensionReadyProvider: () => false,
            processReadyProvider: relay.IsReady,
            policyDistribution: coordinator);

        await service.ActivateAsync(Request(), CancellationToken.None);
        var delivery = relay.CurrentDelivery!;
        coordinator.ExchangeProcessAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));

        var first = await service.GetStatusAsync(CancellationToken.None);
        var second = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Active", first.SessionState);
        Assert.False(first.EnforcementConnected);
        Assert.False(second.EnforcementConnected);
        Assert.True(relay.EnforcementConfirmed);
        Assert.True(coordinator.IsProcessConnected);
        Assert.False(coordinator.IsBrowserConnected);
    }

    [Fact]
    public async Task ExpirationWithdrawsDeliveryAndNeverReconnectsStalePolicy()
    {
        var clock = new ManualClock(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var coordinator = new PolicyDistributionCoordinator(new RecordingTarget(), clock);
        var service = new InMemoryFocusService(clock, policyDistribution: coordinator);
        await service.ActivateAsync(Request(60), CancellationToken.None);
        var delivery = coordinator.ExchangeBrowserAcknowledgement(null)!;
        coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        Assert.True((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);

        clock.Advance(TimeSpan.FromMinutes(1));
        var expired = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Expired", expired.SessionState);
        Assert.False(expired.EnforcementConnected);
        Assert.Null(coordinator.ExchangeBrowserAcknowledgement(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash)));
    }

    private static ActivateSessionRequest Request(int duration = 3600) =>
        new(Guid.NewGuid(), duration, false, 0,
            ApplicationRules:
            [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "chat.exe")],
            WebsiteRules:
            [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
            EnforcementEnabled: true);

    private static ActivateSessionRequest ApplicationOnlyRequest() =>
        new(Guid.NewGuid(), 3600, false, 0,
            ApplicationRules:
            [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "chat.exe")],
            WebsiteRules: [],
            EnforcementEnabled: true);

    private sealed class RecordingTarget : IPolicyDistributionTarget
    {
        public PolicyAcknowledgement? Acknowledgement { get; private set; }

        public ValueTask<PolicyAcknowledgement> ReceiveAsync(
            PolicyDelivery delivery,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Acknowledgement = new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash);
            return ValueTask.FromResult(Acknowledgement);
        }

        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowingTarget : IPolicyDistributionTarget
    {
        public ValueTask<PolicyAcknowledgement> ReceiveAsync(
            PolicyDelivery delivery,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<PolicyAcknowledgement>(new IOException("offline"));

        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class StaleTarget : IPolicyDistributionTarget
    {
        public ValueTask<PolicyAcknowledgement> ReceiveAsync(
            PolicyDelivery delivery,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PolicyAcknowledgement(Guid.NewGuid(), delivery.PolicyHash));

        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
