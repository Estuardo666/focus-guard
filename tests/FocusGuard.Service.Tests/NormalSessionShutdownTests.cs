using FocusGuard.Contracts;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class NormalSessionShutdownTests
{
    [Fact]
    public async Task Shutdown_RecordsActiveNormalSessionAsInterrupted()
    {
        using var store = new TemporaryHistoryStore();
        var audit = new RecordingAuditStore();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var service = new InMemoryFocusService(clock, auditStore: audit, historyStore: store.Value);
        var activation = await service.ActivateAsync(
            new ActivateSessionRequest(Guid.NewGuid(), 180, false, 0) with { ProfileName = "gpt test" },
            CancellationToken.None);
        var sessionId = activation.Value!.SessionId!.Value;
        clock.Advance(TimeSpan.FromSeconds(70));

        await service.InterruptNormalSessionForShutdownAsync(CancellationToken.None);

        var summary = await service.GetSessionSummaryAsync(new SessionSummaryRequest(sessionId), CancellationToken.None);
        Assert.True(summary.Available);
        Assert.Equal(CaptureProtocol.StoppedOutcome, summary.Outcome);
        Assert.Equal(3, summary.PlannedMinutes);
        Assert.Equal(1, summary.FocusedMinutes);
        var interrupted = Assert.Single(audit.Events);
        Assert.Equal(FocusAuditEventKind.SessionInterrupted, interrupted.Kind);
        Assert.Equal(sessionId, interrupted.SessionId);
        Assert.Equal(clock.GetUtcNow(), interrupted.TimestampUtc);
        Assert.Equal("Stopped", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
    }

    [Fact]
    public async Task Shutdown_WithoutRunningSessionWritesNothing()
    {
        using var store = new TemporaryHistoryStore();
        var audit = new RecordingAuditStore();
        var service = new InMemoryFocusService(auditStore: audit, historyStore: store.Value);
        await service.ActivateAsync(new ActivateSessionRequest(Guid.NewGuid(), 60, false, 0), CancellationToken.None);
        await service.StopNormalSessionAsync(CancellationToken.None);
        var before = await store.Value.GetMostRecentAsync(CancellationToken.None);

        await service.InterruptNormalSessionForShutdownAsync(CancellationToken.None);

        Assert.Empty(audit.Events);
        var after = await store.Value.GetMostRecentAsync(CancellationToken.None);
        Assert.Equal(before!.SessionId, after!.SessionId);
        Assert.Equal(before.EndedAtUtc, after.EndedAtUtc);
        Assert.Equal("Stopped", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
    }

    [Fact]
    public async Task Recorder_InterruptsTheSessionWhenTheHostStops()
    {
        using var store = new TemporaryHistoryStore();
        var audit = new RecordingAuditStore();
        var service = new InMemoryFocusService(auditStore: audit, historyStore: store.Value);
        await service.ActivateAsync(new ActivateSessionRequest(Guid.NewGuid(), 600, false, 0), CancellationToken.None);
        var recorder = new NormalSessionShutdownRecorder(service);

        await recorder.StartAsync(CancellationToken.None);
        Assert.Equal("Active", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
        await recorder.StopAsync(CancellationToken.None);

        Assert.Equal("Stopped", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
        Assert.Equal(FocusAuditEventKind.SessionInterrupted, Assert.Single(audit.Events).Kind);
        Assert.NotNull(await store.Value.GetMostRecentAsync(CancellationToken.None));
    }

    private sealed class TemporaryHistoryStore : IDisposable
    {
        private readonly DirectoryInfo _directory;

        public TemporaryHistoryStore()
        {
            _directory = Directory.CreateTempSubdirectory("FocusGuard-shutdown-");
            Value = new SqliteSessionHistoryStore(Path.Combine(_directory.FullName, "history.db"));
        }

        public SqliteSessionHistoryStore Value { get; }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class RecordingAuditStore : IFocusAuditStore
    {
        public List<FocusAuditEvent> Events { get; } = [];

        public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusAuditEvent>>(Events.Take(maximumCount).ToArray());
    }

    private sealed class TestClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
