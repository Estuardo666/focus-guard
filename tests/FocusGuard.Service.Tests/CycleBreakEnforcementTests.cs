using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service.Tests;

public sealed class CycleBreakEnforcementTests
{
    [Fact]
    public async Task Break_ReleasesWebsiteRules()
    {
        var clock = new PhaseClock(DateTimeOffset.UtcNow);
        var recorder = new RecordingTarget();
        var coordinator = new PolicyDistributionCoordinator(recorder, clock);
        var service = new InMemoryFocusService(clock, policyDistribution: coordinator);

        var prepared = await service.PrepareSessionAsync(Request(), CancellationToken.None);
        Assert.True(prepared.IsValid, string.Join("; ", prepared.Errors));
        var activated = await service.ActivatePreparedSessionAsync(
            new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
            CancellationToken.None);
        Assert.True(activated.Value!.Accepted, activated.Value.Reason);

        await service.GetStatusAsync(CancellationToken.None);
        var duringFocus = recorder.Last!;
        Assert.False(duringFocus.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
        Assert.Single(duringFocus.Policy.EffectiveWebsiteRules(clock.GetUtcNow()));

        // Phase 0 is a 1-minute focus block; phase 1 is the 1-minute short break.
        clock.Advance(TimeSpan.FromSeconds(70));
        var status = await service.GetStatusAsync(CancellationToken.None);
        Assert.Equal(SessionPhaseKind.ShortBreak, status.CurrentPhase!.Kind);

        var duringBreak = recorder.Last!;
        Assert.True(
            duringBreak.Policy.IsAuthorizedBreak(clock.GetUtcNow()),
            $"authorizedPhaseIndex={duringBreak.Policy.AuthorizedPhaseIndex}");
        Assert.Empty(duringBreak.Policy.EffectiveWebsiteRules(clock.GetUtcNow()));
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

    private sealed class RecordingTarget : IPolicyDistributionTarget
    {
        public PolicyDelivery? Last { get; private set; }

        public ValueTask<PolicyAcknowledgement> ReceiveAsync(
            PolicyDelivery delivery,
            CancellationToken cancellationToken)
        {
            Last = delivery;
            return ValueTask.FromResult(
                new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        }

        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
