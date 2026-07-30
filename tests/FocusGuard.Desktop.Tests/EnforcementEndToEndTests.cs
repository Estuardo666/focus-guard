using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Monitoring;
using FocusGuard.NativeMessaging;
using FocusGuard.Service;

namespace FocusGuard.Desktop.Tests;

public sealed class EnforcementEndToEndTests
{
    [Fact]
    public async Task DesktopPipe_DualAck_Enforcement_Expiration_ReleasesBothChannels()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var controlPipe = $"FocusGuard.Tests.E2E.Control.{Guid.NewGuid():N}";
        var healthPipe = $"FocusGuard.Tests.E2E.Health.{Guid.NewGuid():N}";
        var receiver = new ProcessMonitorPolicyReceiver(clock);
        var coordinator = new PolicyDistributionCoordinator(receiver, clock);
        var health = new ExtensionHealthState();
        var auditPath = Path.Combine(
            Path.GetTempPath(), $"FocusGuard.E2E.Audit.{Guid.NewGuid():N}.jsonl");
        using var auditStore = new JsonLinesFocusAuditStore(auditPath);
        var service = new InMemoryFocusService(
            clock,
            extensionReadyProvider: () => health.IsExtensionReady(clock.GetUtcNow()),
            policyDistribution: coordinator,
            auditStore: auditStore);
        using var controlServer = new FocusControlPipeService(service, controlPipe);
        using var healthServer = new ExtensionHealthPipeService(
            health, service, coordinator, clock, healthPipe, auditStore);
        await controlServer.StartAsync(CancellationToken.None);
        await healthServer.StartAsync(CancellationToken.None);
        try
        {
            var desktop = new NamedPipeFocusControlClient(controlPipe, TimeSpan.FromSeconds(2));
            var nativeHost = new NamedPipeExtensionHealthClient(
                healthPipe, TimeSpan.FromSeconds(2));
            var activation = await desktop.ActivateAsync(
                new ActivateSessionRequest(
                    Guid.NewGuid(),
                    60,
                    false,
                    0,
                    ApplicationRules:
                    [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "FocusGuard.TestTarget.exe")],
                    WebsiteRules:
                    [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
                    EnforcementEnabled: true),
                CancellationToken.None);
            Assert.True(activation.Accepted);
            var processOnly = await desktop.GetStatusAsync(CancellationToken.None);
            Assert.Equal("Active", processOnly.SessionState);
            Assert.False(processOnly.EnforcementConnected);
            Assert.NotNull(receiver.GetActiveDelivery());

            var firstHeartbeat = await nativeHost.QueryAsync(
                Heartbeat(clock.GetUtcNow()), CancellationToken.None);
            var delivery = Assert.IsType<PolicyDelivery>(firstHeartbeat.Policy);
            Assert.NotNull(receiver.GetActiveDelivery());

            var acknowledged = await nativeHost.QueryAsync(
                Heartbeat(clock.GetUtcNow(), delivery), CancellationToken.None);
            Assert.Equal(delivery.PolicyId, acknowledged.Policy!.PolicyId);
            var active = await desktop.GetStatusAsync(CancellationToken.None);
            Assert.Equal("Active", active.SessionState);
            Assert.True(active.EnforcementConnected);
            Assert.Equal(delivery.PolicyId, receiver.GetActiveDelivery()!.PolicyId);

            await nativeHost.QueryAsync(
                Heartbeat(
                    clock.GetUtcNow(),
                    delivery,
                    ExtensionHealthProtocol.AuditType,
                    "https://example.com/private/path"),
                CancellationToken.None);
            var activity = await desktop.GetActivityAsync(20, CancellationToken.None);
            var browserEvent = Assert.Single(activity.Events);
            Assert.Equal(FocusAuditEventKind.WebsiteBlocked, browserEvent.Kind);
            Assert.Equal("https://example.com/private/path", browserEvent.Target);

            clock.Advance(TimeSpan.FromSeconds(6));
            var componentLost = await desktop.GetStatusAsync(CancellationToken.None);
            Assert.False(componentLost.ExtensionReady);
            Assert.False(componentLost.EnforcementConnected);
            Assert.NotNull(receiver.GetActiveDelivery());

            await nativeHost.QueryAsync(
                Heartbeat(clock.GetUtcNow(), delivery), CancellationToken.None);
            var reconnected = await desktop.GetStatusAsync(CancellationToken.None);
            Assert.True(reconnected.EnforcementConnected);
            Assert.NotNull(receiver.GetActiveDelivery());

            clock.Advance(TimeSpan.FromSeconds(54));
            var expired = await desktop.GetStatusAsync(CancellationToken.None);
            Assert.Equal("Expired", expired.SessionState);
            Assert.False(expired.EnforcementConnected);
            Assert.Null(receiver.GetActiveDelivery());
            var afterExpiration = await nativeHost.QueryAsync(
                Heartbeat(clock.GetUtcNow(), delivery), CancellationToken.None);
            Assert.Null(afterExpiration.Policy);
        }
        finally
        {
            await healthServer.StopAsync(CancellationToken.None);
            await controlServer.StopAsync(CancellationToken.None);
            File.Delete(auditPath);
        }
    }

    private static ExtensionHealthRequest Heartbeat(
        DateTimeOffset now,
        PolicyDelivery? delivery = null,
        string type = ExtensionHealthProtocol.HeartbeatType,
        string? blockedUrl = null) =>
        new(
            ExtensionHealthProtocol.CurrentVersion,
            type,
            Guid.NewGuid(),
            now,
            delivery?.PolicyId,
            delivery?.PolicyHash,
            blockedUrl);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
