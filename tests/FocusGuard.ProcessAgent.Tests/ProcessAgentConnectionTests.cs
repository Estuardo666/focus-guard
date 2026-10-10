using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Monitoring;

namespace FocusGuard.ProcessAgent.Tests;

public sealed class ProcessAgentConnectionTests
{
    [Theory]
    [InlineData("io")]
    [InlineData("timeout")]
    [InlineData("eof")]
    [InlineData("permission")]
    [InlineData("invalid")]
    public async Task FailedExchangeReleasesConfirmationAndRequiresFreshHandshake(string kind)
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        fixture.Client.Failure = kind switch
        {
            "io" => new IOException(), "timeout" => new TimeoutException(),
            "permission" => new UnauthorizedAccessException(), _ => new InvalidDataException()
        };
        Assert.False(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        fixture.Client.Failure = null;
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Client.LastRequest!.AcknowledgedPolicyId);
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.NotNull(fixture.Receiver.GetActiveDelivery());
    }

    [Theory]
    [InlineData("correlation")]
    [InlineData("not-ready")]
    [InlineData("hash")]
    [InlineData("inactive-policy")]
    [InlineData("unrelated-confirmation")]
    public async Task UntrustedReplyCannotRetainConfirmation(string kind)
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        fixture.Client.Transform = response => kind switch
        {
            "correlation" => response with { CorrelationId = Guid.NewGuid() },
            "not-ready" => response with { ServiceReady = false },
            "hash" => response with { Policy = response.Policy! with { PolicyHash = new string('0', 64) } },
            "inactive-policy" => response with { SessionState = "Completed" },
            _ => response with { Policy = PolicyDelivery.Create(response.Policy!.Policy), EnforcementConfirmed = true }
        };
        Assert.False(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task ChangedServiceInstanceRequiresAnotherAcknowledgementEvenForSamePolicy()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        fixture.Client.ServiceInstance = Guid.NewGuid();
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.NotNull(fixture.Receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task MonotonicLeaseExpiresEvenWhenWallClockMovesBackward()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        fixture.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(3));
        fixture.Clock.UtcNow -= TimeSpan.FromHours(1);
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Client.LastRequest!.AcknowledgedPolicyId);
        Assert.Null(fixture.Receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task PendingResponseCannotExtendConfirmationPastItsLease()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        var pending = new TaskCompletionSource<ProcessAgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Pending = pending;
        var exchange = fixture.Connection.TickAsync(default);
        fixture.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(3));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        pending.SetException(new TimeoutException());
        Assert.False(await exchange);
    }

    [Fact]
    public async Task CallerCancellationAndNormalLoopShutdownReleaseLocalPolicy()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Connection.TickAsync(cancellation.Token));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
        await fixture.ConfirmAsync();
        await fixture.Connection.RunAsync(cancellation.Token);
        Assert.Null(fixture.Receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task LostAuditReplyIsNotReplayedOrAttributedToAnotherSession()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        await fixture.Audits.WriteAsync(new(Guid.NewGuid(), fixture.Clock.UtcNow,
            FocusAuditEventKind.ApplicationBlocked, "target.exe", "Terminated", fixture.Client.Policy.Policy.SessionId), default);
        fixture.Client.Failure = new IOException();
        Assert.False(await fixture.Connection.TickAsync(default));
        Assert.Equal(ProcessAgentProtocol.AuditType, fixture.Client.LastRequest!.Type);
        fixture.Client.Failure = null;
        fixture.Client.Policy = PolicyDelivery.Create(Policy(fixture.Clock.UtcNow));
        await fixture.ConfirmAsync();
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.Equal(ProcessAgentProtocol.HeartbeatType, fixture.Client.LastRequest!.Type);
    }

    [Fact]
    public async Task PolicyExpirationAndAuthoritativeNoPolicyReleaseLocalConfirmation()
    {
        var fixture = new Fixture();
        await fixture.ConfirmAsync();
        fixture.Client.Transform = response => response with { Policy = null, EnforcementConfirmed = false, SessionState = "Completed" };
        Assert.True(await fixture.Connection.TickAsync(default));
        Assert.Null(fixture.Receiver.GetActiveDelivery());
    }

    [Fact]
    public async Task AuditQueueIsBoundedAndOnlyDrainsTheMatchingSession()
    {
        var queue = new AgentAuditStore();
        var session = Guid.NewGuid();
        for (var i = 0; i < AgentAuditStore.MaximumPending + 20; i++)
            await queue.WriteAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, FocusAuditEventKind.ApplicationBlocked,
                $"target{i}.exe", "AlreadyExited", session), default);
        var count = 0;
        while (queue.TakeForSession(session) is { } audit) { Assert.Equal("Closed", audit.Outcome); count++; }
        Assert.Equal(AgentAuditStore.MaximumPending, count);
        await queue.WriteAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, FocusAuditEventKind.ApplicationBlocked,
            "target.exe", "Terminated", session), default);
        Assert.Null(queue.TakeForSession(Guid.NewGuid()));
    }

    private static EnforcementPolicy Policy(DateTimeOffset now) => EnforcementPolicy.Create(
        Session.Create(new ProfileId(Guid.NewGuid()), now, TimeSpan.FromMinutes(5), StrictModeOptions.Normal).Value!, [], []);

    private sealed class Fixture
    {
        public ManualClock Clock { get; } = new();
        public ReplyClient Client { get; }
        public ProcessMonitorPolicyReceiver Receiver { get; }
        public AgentAuditStore Audits { get; } = new();
        public ProcessAgentConnection Connection { get; }
        public Fixture()
        {
            Receiver = new(Clock, ProcessAgentConnection.ConfirmationLifetime);
            Client = new(Clock);
            Connection = new(Client, Receiver, Audits, Clock);
        }
        public async Task ConfirmAsync()
        {
            Assert.True(await Connection.TickAsync(default));
            Assert.True(await Connection.TickAsync(default));
            Assert.NotNull(Receiver.GetActiveDelivery());
        }
    }

    private sealed class ReplyClient(ManualClock clock) : IProcessAgentClient
    {
        public Guid ServiceInstance { get; set; } = Guid.NewGuid();
        public PolicyDelivery Policy { get; set; } = PolicyDelivery.Create(ProcessAgentConnectionTests.Policy(clock.UtcNow));
        public Exception? Failure { get; set; }
        public Func<ProcessAgentResponse, ProcessAgentResponse>? Transform { get; set; }
        public ProcessAgentRequest? LastRequest { get; private set; }
        public TaskCompletionSource<ProcessAgentResponse>? Pending { get; set; }
        public ValueTask<ProcessAgentResponse> ExchangeAsync(ProcessAgentRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            if (Failure is not null) return ValueTask.FromException<ProcessAgentResponse>(Failure);
            if (Pending is not null) return new(Pending.Task);
            var response = new ProcessAgentResponse(1, ProcessAgentProtocol.StatusResponseType,
                request.CorrelationId, ServiceInstance, true,
                request.AcknowledgedPolicyId == Policy.PolicyId && request.AcknowledgedPolicyHash == Policy.PolicyHash,
                "Active", 300, clock.UtcNow, Policy);
            return ValueTask.FromResult(Transform?.Invoke(response) ?? response);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void AdvanceMonotonic(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
