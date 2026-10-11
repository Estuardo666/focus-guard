using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class StrictSessionEndTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("FocusGuard-strict-end-");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 10, 7, 26, 10, TimeSpan.Zero));

    public void Dispose() => _directory.Delete(recursive: true);

    [Theory]
    [InlineData(StrictPolicyLoadStatus.Absent)]
    [InlineData(StrictPolicyLoadStatus.Recovered)]
    [InlineData(StrictPolicyLoadStatus.FailedOpen)]
    public async Task SnapshotGoneAtTheEndTimeStillCompletesTheSession(StrictPolicyLoadStatus released)
    {
        // The persistence worker can delete the expired snapshot a moment before the service
        // refreshes the session; that must not turn a full strict session into an interrupted one.
        var store = new ReleasableStrictSessionStore();
        var (service, history) = Create(store);
        var sessionId = await ActivateStrictAsync(service, 60);

        store.Release(released);
        _clock.Advance(TimeSpan.FromSeconds(61));
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Expired", status.SessionState);
        var recorded = await history.GetAsync(sessionId);
        Assert.NotNull(recorded);
        Assert.True(recorded.Completed);
    }

    [Fact]
    public async Task ReleaseBeforeTheEndTimeIsStillReportedAsRecovered()
    {
        var store = new ReleasableStrictSessionStore();
        var (service, history) = Create(store);
        var sessionId = await ActivateStrictAsync(service, 600);

        store.Release(StrictPolicyLoadStatus.Recovered);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Recovered", status.SessionState);
        Assert.False((await history.GetAsync(sessionId))!.Completed);
    }

    private (InMemoryFocusService Service, SqliteSessionHistoryStore History) Create(IStrictSessionStore store)
    {
        var history = new SqliteSessionHistoryStore(Path.Combine(_directory.FullName, "history.db"));
        return (new InMemoryFocusService(_clock, store, historyStore: history), history);
    }

    private static async Task<Guid> ActivateStrictAsync(InMemoryFocusService service, int seconds)
    {
        var recovery = RecoveryCodeService.Generate();
        var activation = await service.ActivateAsync(
            new ActivateSessionRequest(Guid.NewGuid(), seconds, true, 900, recovery.Credential), CancellationToken.None);
        Assert.Equal("Active", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
        return activation.Value!.SessionId!.Value;
    }

    private sealed class ReleasableStrictSessionStore : IStrictSessionStore
    {
        private StrictPolicySnapshot? _snapshot;
        private StrictPolicyLoadStatus? _released;

        public void Release(StrictPolicyLoadStatus status) => _released = status;

        public ValueTask ActivateAsync(Session session, EnforcementPolicy policy, RecoveryCredential recoveryCredential, CancellationToken cancellationToken)
        {
            _snapshot = new StrictPolicySnapshot(StrictPolicySnapshotStore.CurrentSchemaVersion, policy, session.StartUtc,
                "test-boot", recoveryCredential, session.StrictMode.RecoveryDelay);
            return ValueTask.CompletedTask;
        }

        public ValueTask<StrictPolicyLoadResult> LoadAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_released is { } released
                ? new StrictPolicyLoadResult(released, null, released.ToString())
                : _snapshot is null
                    ? new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent")
                    : new StrictPolicyLoadResult(StrictPolicyLoadStatus.Active, _snapshot, "Active"));
    }

    private sealed class TestClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
