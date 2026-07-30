using System.Diagnostics;
using FocusGuard.Domain;

namespace FocusGuard.Monitoring.Tests;

public sealed class ConfirmedProcessPolicyEvaluatorTests
{
    [Fact]
    public async Task EvaluatesOnlyConfirmedPolicyAndDeduplicatesThroughGracePeriod()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var receiver = new ProcessMonitorPolicyReceiver(clock);
        var evaluator = new ConfirmedProcessPolicyEvaluator(receiver, TimeSpan.FromSeconds(2), clock);
        var delivery = PolicyDelivery.Create(CreatePolicy(now));
        var snapshot = new ProcessSnapshot("42:100", @"C:\Apps\chat.exe");
        await receiver.ReceiveAsync(delivery, CancellationToken.None);

        Assert.Empty(evaluator.Evaluate([snapshot]));
        await receiver.SetConfirmedAsync(delivery.PolicyId, true, CancellationToken.None);
        Assert.Equal(ProcessActionKind.RequestGracefulClose, Assert.Single(evaluator.Evaluate([snapshot])).Kind);
        Assert.Empty(evaluator.Evaluate([snapshot]));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(ProcessActionKind.RequestTermination, Assert.Single(evaluator.Evaluate([snapshot])).Kind);
        Assert.Empty(evaluator.Evaluate([snapshot]));
    }

    [Fact]
    public async Task AllowAndProtectedProcessAlwaysFailOpen()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var receiver = new ProcessMonitorPolicyReceiver(new ManualClock(now));
        var delivery = PolicyDelivery.Create(CreatePolicy(now, includeAllow: true));
        await receiver.ReceiveAsync(delivery, CancellationToken.None);
        await receiver.SetConfirmedAsync(delivery.PolicyId, true, CancellationToken.None);
        var evaluator = new ConfirmedProcessPolicyEvaluator(receiver, TimeSpan.Zero, new ManualClock(now));

        Assert.Empty(evaluator.Evaluate([new ProcessSnapshot("1", @"C:\Apps\chat.exe")]));
        Assert.Empty(evaluator.Evaluate([new ProcessSnapshot("2", @"C:\Apps\other.exe", IsProtected: true)]));
    }

    [Fact]
    public async Task TenThousandRulesEvaluateBelowProcessDetectionBudget()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var receiver = new ProcessMonitorPolicyReceiver(clock);
        var session = Session.Create(ProfileId.New(), now, TimeSpan.FromMinutes(1), StrictModeOptions.Normal)
            .Value!.TransitionTo(SessionState.Activating);
        var rules = Enumerable.Range(0, 10_000)
            .Select(index => new ApplicationRule(
                Guid.NewGuid(),
                RuleAction.Block,
                ApplicationMatchKind.FileName,
                $"process-{index}.exe"))
            .ToArray();
        var delivery = PolicyDelivery.Create(
            EnforcementPolicy.Create(session, rules, [], enforcementEnabled: true));
        await receiver.ReceiveAsync(delivery, CancellationToken.None);
        await receiver.SetConfirmedAsync(delivery.PolicyId, true, CancellationToken.None);
        var evaluator = new ConfirmedProcessPolicyEvaluator(receiver, TimeSpan.Zero, clock);
        var samples = new List<double>();

        for (var index = 0; index < 21; index++)
        {
            var timer = Stopwatch.StartNew();
            evaluator.Evaluate([new ProcessSnapshot($"instance-{index}", @"C:\Apps\process-9999.exe")]);
            timer.Stop();
            if (index > 0)
            {
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        Assert.True(median < 1000, $"10,000-rule process evaluation median was {median:F3} ms.");
    }

    private static EnforcementPolicy CreatePolicy(DateTimeOffset now, bool includeAllow = false)
    {
        var session = Session.Create(ProfileId.New(), now, TimeSpan.FromMinutes(1), StrictModeOptions.Normal)
            .Value!.TransitionTo(SessionState.Activating);
        var rules = new List<ApplicationRule>
        {
            new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.Folder, @"C:\Apps")
        };
        if (includeAllow)
        {
            rules.Add(new(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "chat.exe"));
        }
        return EnforcementPolicy.Create(session, rules, [], enforcementEnabled: true);
    }

    private sealed class ManualClock(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan duration) => _value += duration;
    }
}
