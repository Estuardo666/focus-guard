using FocusGuard.Domain;
using FocusGuard.Monitoring;

namespace FocusGuard.Monitoring.Tests;

public sealed class SimulatedProcessMonitorTests
{
    [Fact]
    public void MatchingProcess_RequestsGracefulCloseThenTerminationAfterGrace()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var monitor = new SimulatedProcessMonitor(TimeSpan.FromSeconds(10), clock);
        var policy = CreatePolicy(clock.GetUtcNow(), TimeSpan.FromMinutes(5), new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "blocked.exe"));
        var snapshot = new ProcessSnapshot("instance-1", @"C:\Apps\blocked.exe");

        var first = monitor.Evaluate(policy, [snapshot]);
        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(9));
        var beforeGrace = monitor.Evaluate(policy, [snapshot]);
        clock.SetUtcNow(clock.GetUtcNow().AddSeconds(1));
        var afterGrace = monitor.Evaluate(policy, [snapshot]);

        Assert.Equal(ProcessActionKind.RequestGracefulClose, Assert.Single(first).Kind);
        Assert.Empty(beforeGrace);
        Assert.Equal(ProcessActionKind.RequestTermination, Assert.Single(afterGrace).Kind);
    }

    [Fact]
    public void AllowRuleTakesPrecedenceOverBlockRule()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new SimulatedProcessMonitor(TimeSpan.Zero, clock);
        var policy = CreatePolicy(
            clock.GetUtcNow(),
            TimeSpan.FromMinutes(5),
            new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "blocked.exe"),
            new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.ExactPath, @"C:\Apps\blocked.exe"));

        Assert.Empty(monitor.Evaluate(policy, [new ProcessSnapshot("instance-1", @"C:\Apps\blocked.exe")]));
    }

    [Fact]
    public void ProtectedProcessNeverProducesAction()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new SimulatedProcessMonitor(TimeSpan.Zero, clock);
        var policy = CreatePolicy(clock.GetUtcNow(), TimeSpan.FromMinutes(5), new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "security.exe"));

        Assert.Empty(monitor.Evaluate(policy, [new ProcessSnapshot("security-1", "security.exe", IsProtected: true)]));
    }

    [Fact]
    public void TerminationIsDeduplicatedByProcessInstance()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var monitor = new SimulatedProcessMonitor(TimeSpan.Zero, clock);
        var policy = CreatePolicy(clock.GetUtcNow(), TimeSpan.FromMinutes(5), new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "blocked.exe"));
        var snapshot = new ProcessSnapshot("instance-1", "blocked.exe");

        monitor.Evaluate(policy, [snapshot]);
        Assert.Single(monitor.Evaluate(policy, [snapshot]));
        Assert.Empty(monitor.Evaluate(policy, [snapshot]));
    }

    [Fact]
    public void ExpiredPolicyProducesNoAction()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 2, 0, TimeSpan.Zero));
        var monitor = new SimulatedProcessMonitor(TimeSpan.Zero, clock);
        var policy = CreatePolicy(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(1), new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "blocked.exe"));

        Assert.Empty(monitor.Evaluate(policy, [new ProcessSnapshot("instance-1", "blocked.exe")]));
    }

    private static EnforcementPolicy CreatePolicy(DateTimeOffset startUtc, TimeSpan duration, params ApplicationRule[] rules)
    {
        var session = Session.Create(ProfileId.New(), startUtc, duration, StrictModeOptions.Normal).Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);
        return EnforcementPolicy.Create(session, rules, Array.Empty<WebsiteRule>(), enforcementEnabled: true);
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}
