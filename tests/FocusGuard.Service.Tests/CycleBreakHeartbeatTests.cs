using System.IO.Pipes;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service.Tests;

public sealed class CycleBreakHeartbeatTests
{
    [Fact]
    public async Task StatusAtCycleBoundaryReturnsBreakPolicyAndDoesNotClearItsAcknowledgement()
    {
        var pipeName = $"FocusGuard.Tests.CycleStatus.{Guid.NewGuid():N}";
        var clock = new PhaseClock(DateTimeOffset.UtcNow);
        var health = new ExtensionHealthState();
        var coordinator = new PolicyDistributionCoordinator(new NullTarget(), clock);
        var service = new InMemoryFocusService(
            clock,
            extensionReadyProvider: () => health.IsExtensionReady(clock.GetUtcNow()),
            policyDistribution: coordinator);
        using var pipeService = new ExtensionHealthPipeService(
            health,
            service,
            coordinator,
            clock,
            pipeName);
        await pipeService.StartAsync(CancellationToken.None);
        try
        {
            var prepared = await service.PrepareSessionAsync(Request(), CancellationToken.None);
            await service.ActivatePreparedSessionAsync(
                new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
                CancellationToken.None);

            PolicyDelivery? focusPolicy = null;
            for (var beat = 0; beat < 5; beat++)
            {
                var response = await BeatAsync(pipeName, clock, focusPolicy);
                focusPolicy = response.Policy;
                if (response.EnforcementConfirmed) break;
            }

            Assert.NotNull(focusPolicy);
            Assert.False(focusPolicy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
            Assert.True((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);

            clock.Advance(TimeSpan.FromSeconds(70));
            var breakStatus = await SendAsync(
                pipeName,
                clock,
                ExtensionHealthProtocol.StatusType,
                held: null);

            Assert.NotNull(breakStatus.Policy);
            Assert.True(breakStatus.Policy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
            Assert.False(breakStatus.EnforcementConfirmed);

            var breakAcknowledgement = await BeatAsync(pipeName, clock, breakStatus.Policy);
            Assert.True(breakAcknowledgement.EnforcementConfirmed);
            var statusAfterAcknowledgement = await SendAsync(
                pipeName,
                clock,
                ExtensionHealthProtocol.StatusType,
                held: null);

            Assert.True(statusAfterAcknowledgement.EnforcementConfirmed);
            Assert.True(statusAfterAcknowledgement.Policy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
            Assert.True((await service.GetStatusAsync(CancellationToken.None)).EnforcementConnected);
        }
        finally
        {
            await pipeService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Heartbeat_DeliversBreakPolicyOnTheFirstBeatAfterTheBoundary()
    {
        var pipeName = $"FocusGuard.Tests.CycleBreak.{Guid.NewGuid():N}";
        var clock = new PhaseClock(DateTimeOffset.UtcNow);
        var coordinator = new PolicyDistributionCoordinator(new NullTarget(), clock);
        var service = new InMemoryFocusService(clock, policyDistribution: coordinator);
        var pipeService = new ExtensionHealthPipeService(
            new ExtensionHealthState(),
            service,
            coordinator,
            clock,
            pipeName);
        await pipeService.StartAsync(CancellationToken.None);
        try
        {
            var prepared = await service.PrepareSessionAsync(Request(), CancellationToken.None);
            await service.ActivatePreparedSessionAsync(
                new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
                CancellationToken.None);

            // Settle: heartbeat until the extension is confirmed under the focus policy.
            PolicyDelivery? held = null;
            for (var beat = 0; beat < 5; beat++)
            {
                var focusBeat = await BeatAsync(pipeName, clock, held);
                held = focusBeat.Policy;
                if (focusBeat.EnforcementConfirmed)
                {
                    break;
                }
            }

            Assert.NotNull(held);
            Assert.False(held!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));

            // Cross into the short break (phase 1 of a 1-minute focus / 1-minute break plan).
            clock.Advance(TimeSpan.FromSeconds(70));
            var breakBeat = await BeatAsync(pipeName, clock, held);

            Assert.True(
                breakBeat.Policy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()),
                "first beat after the boundary still carried the focus phase policy");

            // The new delivery still has to be acknowledged before it counts as enforced;
            // the extension acknowledges immediately rather than on the next interval.
            var acknowledged = await BeatAsync(pipeName, clock, breakBeat.Policy);
            Assert.True(acknowledged.EnforcementConfirmed);
            Assert.True(acknowledged.Policy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
        }
        finally
        {
            await pipeService.StopAsync(CancellationToken.None);
            pipeService.Dispose();
        }
    }

    private static async Task<ExtensionHealthResponse> BeatAsync(
        string pipeName,
        TimeProvider clock,
        PolicyDelivery? held) =>
        await SendAsync(pipeName, clock, ExtensionHealthProtocol.HeartbeatType, held);

    private static async Task<ExtensionHealthResponse> SendAsync(
        string pipeName,
        TimeProvider clock,
        string type,
        PolicyDelivery? held)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var request = new ExtensionHealthRequest(
            1,
            type,
            Guid.NewGuid(),
            clock.GetUtcNow().ToUniversalTime(),
            held?.PolicyId,
            held?.PolicyHash);
        await BoundedPipeFraming.WriteAsync(
            pipe,
            ExtensionHealthProtocol.WriteRequest(request),
            CancellationToken.None);
        var payload = await BoundedPipeFraming.ReadAsync(pipe, CancellationToken.None);
        var response = ExtensionHealthProtocol.ReadResponse(payload!);
        Assert.True(response.IsValid, string.Join("; ", response.Errors));
        return response.Value!;
    }

    private static PrepareSessionRequest Request() =>
        new(
            Guid.NewGuid(),
            "Cycles",
            SessionMode.FocusCycle,
            0,
            new FocusCycleConfigurationContract(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null),
            false,
            0,
            ApplicationRules: [],
            WebsiteRules: [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
            EnforcementEnabled: true,
            BreakApplicationRules: [],
            BreakWebsiteRules: []);

    private sealed class PhaseClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class NullTarget : IPolicyDistributionTarget
    {
        public ValueTask<PolicyAcknowledgement> ReceiveAsync(
            PolicyDelivery delivery,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));

        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
