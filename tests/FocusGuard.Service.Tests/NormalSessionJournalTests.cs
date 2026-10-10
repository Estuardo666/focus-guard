using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class NormalSessionJournalTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("FocusGuard-journal-");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    private string JournalPath => Path.Combine(_directory.FullName, "normal-session-journal.json");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Journal_RoundTripsTheInterruptionEntry()
    {
        var service = new InMemoryFocusService(_clock);
        await service.ActivateAsync(Request(1800) with { ProfileName = "Deep Work" }, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(7));
        var entry = service.CreateInterruptionJournalEntry()!;
        var journal = new NormalSessionJournal(JournalPath);

        await journal.WriteAsync(entry, CancellationToken.None);
        var read = await journal.ReadAsync(CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(entry.SessionId, read.SessionId);
        Assert.Equal(entry.ProfileId, read.ProfileId);
        Assert.Equal("Deep Work", read.ProfileName);
        Assert.Equal(entry.StartUtc, read.StartUtc);
        Assert.Equal(_clock.GetUtcNow(), read.EndedAtUtc);
        Assert.False(read.Completed);
        Assert.Equal(30, read.PlannedMinutes);
        Assert.Equal(7, read.FocusedMinutes);
        Assert.Equal(entry.Phases, read.Phases);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\":2}")]
    [InlineData("{\"SchemaVersion\":1,\"SessionId\":\"11111111-1111-4111-8111-111111111111\",\"ProfileId\":\"22222222-2222-4222-8222-222222222222\",\"ProfileName\":\"X\",\"StartUtc\":\"2026-10-10T12:00:00+00:00\",\"LastSeenUtc\":\"2026-10-10T11:00:00+00:00\",\"PlannedMinutes\":5,\"FocusedMinutes\":0,\"Phases\":[],\"CycleMetrics\":null,\"ScheduledIntent\":null}")]
    public async Task Journal_QuarantinesCorruptOrOutOfBoundsFiles(string content)
    {
        await File.WriteAllTextAsync(JournalPath, content);
        var journal = new NormalSessionJournal(JournalPath);

        Assert.Null(await journal.ReadAsync(CancellationToken.None));
        Assert.False(File.Exists(JournalPath));
        Assert.Single(Directory.GetFiles(_directory.FullName, "normal-session-journal.json.invalid-*"));
    }

    [Fact]
    public async Task Worker_WritesOnStartRefreshesOnHeartbeatAndRemovesOnStop()
    {
        var service = new InMemoryFocusService(_clock);
        var journal = new NormalSessionJournal(JournalPath);
        var worker = new NormalSessionJournalWorker(service, journal, _clock, heartbeat: TimeSpan.FromSeconds(10));

        await worker.TickAsync(CancellationToken.None);
        Assert.False(journal.Exists);

        await service.ActivateAsync(Request(600), CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);
        var first = await journal.ReadAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(4));
        await worker.TickAsync(CancellationToken.None);
        var notYet = await journal.ReadAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(6));
        await worker.TickAsync(CancellationToken.None);
        var refreshed = await journal.ReadAsync(CancellationToken.None);

        Assert.Equal(first!.EndedAtUtc, notYet!.EndedAtUtc);
        Assert.Equal(first.EndedAtUtc.AddSeconds(10), refreshed!.EndedAtUtc);

        await service.StopNormalSessionAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);
        Assert.False(journal.Exists);
    }

    [Fact]
    public async Task Reconcile_RecordsAnOrphanedSessionOnceAtItsLastHeartbeat()
    {
        using var history = new TemporaryHistoryStore();
        var audit = new RecordingAuditStore();
        var crashed = new InMemoryFocusService(_clock, auditStore: audit, historyStore: history.Value);
        await crashed.ActivateAsync(Request(1800) with { ProfileName = "gpt test" }, CancellationToken.None);
        var journal = new NormalSessionJournal(JournalPath);
        var crashedWorker = new NormalSessionJournalWorker(crashed, journal, _clock);
        _clock.Advance(TimeSpan.FromMinutes(4));
        await crashedWorker.TickAsync(CancellationToken.None);
        var lastHeartbeat = _clock.GetUtcNow();
        var sessionId = crashed.CreateInterruptionJournalEntry()!.SessionId;

        // The process dies here: no stop, no closure. A new process starts later.
        _clock.Advance(TimeSpan.FromMinutes(3));
        var restarted = new InMemoryFocusService(_clock, auditStore: audit, historyStore: history.Value);
        await new NormalSessionJournalWorker(restarted, journal, _clock).ReconcileAsync(CancellationToken.None);

        var recorded = await history.Value.GetAsync(sessionId);
        Assert.NotNull(recorded);
        Assert.False(recorded.Completed);
        Assert.Equal(lastHeartbeat, recorded.EndedAtUtc);
        Assert.Equal(4, recorded.FocusedMinutes);
        Assert.Equal("gpt test", recorded.ProfileName);
        var interrupted = Assert.Single(audit.Events);
        Assert.Equal(FocusAuditEventKind.SessionInterrupted, interrupted.Kind);
        Assert.Equal("ServiceTerminated", interrupted.Outcome);
        Assert.Equal(sessionId, interrupted.SessionId);
        Assert.False(journal.Exists);
        Assert.Equal("Inactive", (await restarted.GetStatusAsync(CancellationToken.None)).SessionState);

        await journal.WriteAsync(recorded, CancellationToken.None);
        await new NormalSessionJournalWorker(restarted, journal, _clock).ReconcileAsync(CancellationToken.None);
        Assert.Single(audit.Events);
        Assert.False(journal.Exists);
    }

    [Fact]
    public async Task GracefulStop_LeavesNoJournalToReplay()
    {
        using var history = new TemporaryHistoryStore();
        var audit = new RecordingAuditStore();
        var service = new InMemoryFocusService(_clock, auditStore: audit, historyStore: history.Value);
        await service.ActivateAsync(Request(600), CancellationToken.None);
        var journal = new NormalSessionJournal(JournalPath);
        var worker = new NormalSessionJournalWorker(service, journal, _clock);
        await worker.TickAsync(CancellationToken.None);
        Assert.True(journal.Exists);

        await new NormalSessionShutdownRecorder(service).StopAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(journal.Exists);
        Assert.Equal("Stopped", Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task StrictSession_IsNeverJournaled()
    {
        var service = new InMemoryFocusService(_clock, strictSessionStore: new AcceptingStrictSessionStore());
        var generated = RecoveryCodeService.Generate();
        await service.ActivateAsync(new ActivateSessionRequest(Guid.NewGuid(), 600, true, 900, generated.Credential), CancellationToken.None);
        var journal = new NormalSessionJournal(JournalPath);

        await new NormalSessionJournalWorker(service, journal, _clock).TickAsync(CancellationToken.None);

        Assert.Equal("Active", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
        Assert.False(journal.Exists);
    }

    [Fact]
    public async Task ClockRollback_NeverRewindsOrInvalidatesTheJournal()
    {
        var service = new InMemoryFocusService(_clock);
        await service.ActivateAsync(Request(1800), CancellationToken.None);
        var start = _clock.GetUtcNow();
        var journal = new NormalSessionJournal(JournalPath);
        var worker = new NormalSessionJournalWorker(service, journal, _clock, heartbeat: TimeSpan.FromSeconds(10));
        _clock.Advance(TimeSpan.FromMinutes(6));
        await worker.TickAsync(CancellationToken.None);

        // Wall clock rolled back past the session start while monotonic time keeps going.
        _clock.SetWall(start.AddHours(-2));
        _clock.AdvanceMonotonic(TimeSpan.FromSeconds(30));
        await worker.TickAsync(CancellationToken.None);
        var afterRollback = await journal.ReadAsync(CancellationToken.None);

        Assert.NotNull(afterRollback);
        Assert.Equal(start.AddMinutes(6), afterRollback.EndedAtUtc);
        Assert.Equal(6, afterRollback.FocusedMinutes);
        Assert.Equal(start, service.CreateInterruptionJournalEntry()!.EndedAtUtc);
    }

    [Fact]
    public async Task ClockJumpForward_IsClampedToThePlannedEnd()
    {
        var service = new InMemoryFocusService(_clock);
        await service.ActivateAsync(Request(600), CancellationToken.None);
        var start = _clock.GetUtcNow();
        _clock.SetWall(start.AddDays(40));

        var entry = service.CreateInterruptionJournalEntry()!;
        var journal = new NormalSessionJournal(JournalPath);
        await journal.WriteAsync(entry, CancellationToken.None);

        Assert.Equal(start.AddMinutes(10), entry.EndedAtUtc);
        Assert.Equal(10, (await journal.ReadAsync(CancellationToken.None))!.FocusedMinutes);
    }
    private static ActivateSessionRequest Request(int durationSeconds) => new(Guid.NewGuid(), durationSeconds, false, 0);

    private sealed class TemporaryHistoryStore : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("FocusGuard-journal-history-");

        public TemporaryHistoryStore() =>
            Value = new SqliteSessionHistoryStore(Path.Combine(_directory.FullName, "history.db"));

        public SqliteSessionHistoryStore Value { get; }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class RecordingAuditStore : IFocusAuditStore
    {
        public List<FocusAuditEvent> Events { get; } = [];

        public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusAuditEvent>>(Events.Take(maximumCount).ToArray());
    }

    private sealed class AcceptingStrictSessionStore : IStrictSessionStore
    {
        public ValueTask ActivateAsync(Session session, EnforcementPolicy policy, RecoveryCredential recoveryCredential, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<StrictPolicyLoadResult> LoadAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent"));
    }

    private sealed class TestClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
            _timestamp += delta.Ticks;
        }

        public void SetWall(DateTimeOffset utcNow) => _utcNow = utcNow;

        public void AdvanceMonotonic(TimeSpan delta) => _timestamp += delta.Ticks;
    }
}
