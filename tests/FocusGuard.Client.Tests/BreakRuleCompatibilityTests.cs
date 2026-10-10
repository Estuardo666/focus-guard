using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

/// <summary>Ensures cycle preparation preserves the persisted Block-only break exceptions.</summary>
public sealed class BreakRuleCompatibilityTests
{
    [Fact]
    public async Task StoredBlockExceptionsRemainBlockedDuringAnAuthorizedBreak()
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Break exceptions",
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "focus.exe")],
            [new(Guid.NewGuid(), RuleAction.Block, "focus.example")]);
        var applicationException = new BreakApplicationRule(Guid.NewGuid(), RuleAction.Block,
            ApplicationMatchKind.FileName, "game.exe");
        var websiteException = new BreakWebsiteRule(Guid.NewGuid(), RuleAction.Block, "games.example");
        await data.Store.SaveProfileAsync(profile);
        await data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, null, [applicationException], [websiteException]));

        var now = DateTimeOffset.UtcNow;
        var clock = new PhaseClock(now);
        var target = new RecordingTarget();
        var coordinator = new PolicyDistributionCoordinator(target, clock);
        var service = new InMemoryFocusService(clock, policyDistribution: coordinator);
        var pipe = "FocusGuard.Tests.Fg216.BreakRules." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await server.StartAsync(cancellation.Token);
        try
        {
            var client = new NamedPipeFocusControlClient(pipe);
            var workflow = new SessionWorkflow(client, data.Store);
            var configuration = FocusCycleConfiguration.Create(
                TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), 2,
                CycleTerminationMode.CycleCount, 2).Value!;
            var review = await workflow.PrepareSavedNormalCycleAsync(
                new(profile.Id.Value, null, configuration, EnforcementEnabled: true), cancellation.Token);

            Assert.True(review.Preparation.Accepted, review.Preparation.Reason);
            Assert.Equal(1, review.BreakApplicationRuleCount);
            Assert.Equal(1, review.BreakWebsiteRuleCount);
            Assert.Equal("game.exe", Assert.Single(review.BreakApplications));
            Assert.Equal("games.example", Assert.Single(review.BreakWebsites));
            var started = await workflow.ActivatePreparedAsync(
                new(review.Preparation.PreparationId!.Value), cancellation.Token);
            Assert.True(started.Accepted, started.Reason);

            await client.GetStatusAsync(cancellation.Token);
            Assert.Equal("focus.exe", Assert.Single(target.Last!.Policy.EffectiveApplicationRules(now)).Value);
            Assert.Equal("focus.example", Assert.Single(target.Last.Policy.EffectiveWebsiteRules(now)).Pattern);

            clock.Advance(TimeSpan.FromSeconds(70));
            var status = await client.GetStatusAsync(cancellation.Token);
            Assert.Equal(SessionPhaseKind.ShortBreak, status.CurrentPhase!.Kind);
            var duringBreak = target.Last!.Policy;
            Assert.True(duringBreak.IsAuthorizedBreak(clock.GetUtcNow()));
            Assert.Equal("Block", Assert.Single(duringBreak.EffectiveApplicationRules(clock.GetUtcNow())).Action.ToString());
            Assert.Equal("game.exe", Assert.Single(duringBreak.EffectiveApplicationRules(clock.GetUtcNow())).Value);
            Assert.Equal("games.example", Assert.Single(duringBreak.EffectiveWebsiteRules(clock.GetUtcNow())).Pattern);

            // Stored data remains Block-only and unchanged after prepare/activate.
            var persisted = await data.Store.LoadProfileCycleSettingsAsync(profile.Id);
            Assert.Equal(applicationException, Assert.Single(persisted!.ApplicationRules));
            Assert.Equal(websiteException, Assert.Single(persisted.WebsiteRules));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AllowBreakRuleCannotBeEncodedOrPersisted()
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Break rules", [], []);
        await data.Store.SaveProfileAsync(profile);

        Assert.False(CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), Request(profile.Id.Value, "Allow")).IsValid);
        await Assert.ThrowsAsync<ArgumentException>(() => data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, null,
            [new(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "audit-target.exe")], [])));
        Assert.Empty((await data.Store.LoadProfileCycleSettingsAsync(profile.Id))!.ApplicationRules);
    }

    private static PrepareSessionRequest Request(Guid profile, string action) => new(profile, "Audit", SessionMode.FocusCycle, 0,
        new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0,
        ApplicationRules: [], WebsiteRules: [],
        BreakApplicationRules: [new(Guid.NewGuid(), action, "FileName", "audit-target.exe")], BreakWebsiteRules: []);

    private sealed class PhaseClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class RecordingTarget : IPolicyDistributionTarget
    {
        public PolicyDelivery? Last { get; private set; }
        public ValueTask<PolicyAcknowledgement> ReceiveAsync(PolicyDelivery delivery, CancellationToken cancellationToken)
        {
            Last = delivery;
            return ValueTask.FromResult(new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
        }
        public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg216-" + Guid.NewGuid());
        public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path.Combine(_directory, "profiles.db"));
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
