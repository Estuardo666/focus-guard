using FocusGuard.Domain;

namespace FocusGuard.Monitoring.Tests;

public sealed class ProcessMonitorPolicyReceiverTests
{
    [Fact]
    public async Task AcknowledgesExactValidSnapshotAndClearsAtExpiration()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(now);
        var receiver = new ProcessMonitorPolicyReceiver(clock);
        var delivery = PolicyDelivery.Create(CreatePolicy(now));

        var acknowledgement = await receiver.ReceiveAsync(delivery, CancellationToken.None);
        Assert.Equal(delivery.PolicyId, acknowledgement.PolicyId);
        Assert.Equal(delivery.PolicyHash, acknowledgement.PolicyHash);
        Assert.Null(receiver.GetActiveDelivery());
        await receiver.SetConfirmedAsync(delivery.PolicyId, true, CancellationToken.None);
        Assert.NotNull(receiver.GetActiveDelivery());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task RejectsTamperedHash()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var receiver = new ProcessMonitorPolicyReceiver(new ManualClock(now));
        var delivery = PolicyDelivery.Create(CreatePolicy(now)) with
        {
            PolicyHash = new string('0', 64)
        };

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await receiver.ReceiveAsync(delivery, CancellationToken.None));
    }

    private static EnforcementPolicy CreatePolicy(DateTimeOffset now)
    {
        var session = Session.Create(
            new ProfileId(Guid.NewGuid()),
            now,
            TimeSpan.FromMinutes(1),
            StrictModeOptions.Normal).Value!.TransitionTo(SessionState.Activating);
        return EnforcementPolicy.Create(session, [], []);
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
