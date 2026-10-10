using FocusGuard.Client.State;
using FocusGuard.Contracts;

namespace FocusGuard.Client.Tests;

public sealed class SessionConnectionTrackerTests
{
    [Fact]
    public void InitialFailureDoesNotInventIdleOrProtection()
    {
        var tracker = new SessionConnectionTracker();
        Assert.False(tracker.Snapshot.IsFresh);
        Assert.False(tracker.Snapshot.HasConfirmedStatus);
        tracker.MarkDisconnected();
        Assert.Null(tracker.Snapshot.LastStatus);
        Assert.Null(tracker.Snapshot.LastConfirmedUtc);
        Assert.False(tracker.Snapshot.IsStale);
    }

    [Fact]
    public void FailureKeepsLastSnapshotAndReceiptTimeAcrossLocalDeadline()
    {
        var clock = new Clock();
        var tracker = new SessionConnectionTracker(clock);
        var status = Active(clock.GetUtcNow());
        tracker.Observe(status);
        var received = tracker.Snapshot.LastConfirmedUtc;
        clock.Advance(TimeSpan.FromDays(1));
        tracker.MarkDisconnected();
        Assert.Same(status, tracker.Snapshot.LastStatus);
        Assert.Equal(received, tracker.Snapshot.LastConfirmedUtc);
        Assert.True(tracker.Snapshot.IsStale);
        Assert.False(tracker.Snapshot.IsFresh);
        Assert.Equal("Active", tracker.Snapshot.LastStatus!.SessionState);
    }

    [Fact]
    public void NotReadyResponseCannotOverwriteConfirmedSession()
    {
        var tracker = new SessionConnectionTracker();
        var status = Active(DateTimeOffset.UtcNow);
        tracker.Observe(status);
        tracker.Observe(new(false, false, "Inactive", 0, null, null, null, null, false, false));
        Assert.Same(status, tracker.Snapshot.LastStatus);
        Assert.True(tracker.Snapshot.IsStale);
    }

    [Fact]
    public void ReconnectionReplacesStateOnlyWithReadyBackendResponse()
    {
        var tracker = new SessionConnectionTracker();
        var status = Active(DateTimeOffset.UtcNow);
        tracker.Observe(status);
        tracker.MarkDisconnected();
        var terminal = status with { SessionState = "Expired", RemainingSeconds = 0, EnforcementConnected = false };
        tracker.Observe(terminal);
        Assert.True(tracker.Snapshot.IsFresh);
        Assert.False(tracker.Snapshot.IsStale);
        Assert.Same(terminal, tracker.Snapshot.LastStatus);
    }

    private static StatusResponse Active(DateTimeOffset now) =>
        new(true, true, "Active", 60, Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(1), true, true);

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
