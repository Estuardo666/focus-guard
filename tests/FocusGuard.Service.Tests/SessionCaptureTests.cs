using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class SessionCaptureTests
{
    [Fact]
    public async Task Capture_IsRejectedWhenNoSessionIsRunning()
    {
        using var store = new TemporaryHistoryStore();
        var service = CreateService(store);

        var response = await service.CaptureAsync(
            new CaptureRequest("Revisar X", CaptureProtocol.WebsiteKind, "https://x.com/"),
            CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Null(response.CaptureId);
    }

    [Fact]
    public async Task Capture_WithoutTextFallsBackToTheBlockedDomain()
    {
        using var store = new TemporaryHistoryStore();
        var service = CreateService(store);
        await service.ActivateAsync(Request(3600), CancellationToken.None);

        var response = await service.CaptureAsync(
            new CaptureRequest(null, CaptureProtocol.WebsiteKind, "https://instagram.com/"),
            CancellationToken.None);

        Assert.True(response.Accepted);
        var summary = await FinishAndSummarizeAsync(service, store);
        var capture = Assert.Single(summary.Captures);
        Assert.Equal("instagram.com", capture.Text);
        Assert.Equal("instagram.com", capture.SourceLabel);
    }

    [Fact]
    public async Task Capture_RejectsAWebsiteSourceCarryingAQueryString()
    {
        using var store = new TemporaryHistoryStore();
        var service = CreateService(store);
        await service.ActivateAsync(Request(3600), CancellationToken.None);

        var response = await service.CaptureAsync(
            new CaptureRequest(
                "Buscar precio",
                CaptureProtocol.WebsiteKind,
                "https://amazon.com/s?k=monitor"),
            CancellationToken.None);

        Assert.False(response.Accepted);
    }

    [Fact]
    public async Task Summary_ReportsACompletedSessionWithItsStreakAndPendingCaptures()
    {
        using var store = new TemporaryHistoryStore();
        var clock = new TestClock(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, clock);
        await service.ActivateAsync(
            Request(3000) with { ProfileName = "Deep Work" },
            CancellationToken.None);
        await service.CaptureAsync(
            new CaptureRequest("Revisar respuesta de Carlos", CaptureProtocol.WebsiteKind, "https://x.com/"),
            CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(50));
        await service.GetStatusAsync(CancellationToken.None);
        var summary = await service.GetSessionSummaryAsync(
            new SessionSummaryRequest(),
            CancellationToken.None);

        Assert.True(summary.Available);
        Assert.Equal(CaptureProtocol.CompletedOutcome, summary.Outcome);
        Assert.Equal("Deep Work", summary.ProfileName);
        Assert.Equal(50, summary.PlannedMinutes);
        Assert.Equal(50, summary.FocusedMinutes);
        Assert.Equal(1, summary.StreakDays);
        Assert.Equal("Revisar respuesta de Carlos", Assert.Single(summary.Captures).Text);
    }

    [Fact]
    public async Task Summary_DoesNotCreditAStreakToASessionEndedEarly()
    {
        using var store = new TemporaryHistoryStore();
        var clock = new TestClock(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, clock);
        await service.ActivateAsync(Request(3000), CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(32));
        await service.StopNormalSessionAsync(CancellationToken.None);
        var summary = await service.GetSessionSummaryAsync(
            new SessionSummaryRequest(),
            CancellationToken.None);

        Assert.True(summary.Available);
        Assert.Equal(CaptureProtocol.StoppedOutcome, summary.Outcome);
        Assert.Equal(32, summary.FocusedMinutes);
        Assert.Equal(50, summary.PlannedMinutes);
        Assert.Equal(0, summary.StreakDays);
    }

    [Fact]
    public async Task ResolvedCapturesDropOutOfTheSummaryButStayOnDisk()
    {
        using var store = new TemporaryHistoryStore();
        var service = CreateService(store);
        await service.ActivateAsync(Request(3600), CancellationToken.None);
        await service.CaptureAsync(
            new CaptureRequest("Precio del monitor", CaptureProtocol.WebsiteKind, "https://amazon.com/"),
            CancellationToken.None);
        var summary = await FinishAndSummarizeAsync(service, store);
        var captureId = Assert.Single(summary.Captures).Id;

        var resolved = await service.ResolveCapturesAsync(
            new ResolveCapturesRequest([captureId], CaptureProtocol.DismissedResolution),
            CancellationToken.None);
        var after = await service.GetSessionSummaryAsync(
            new SessionSummaryRequest(summary.SessionId),
            CancellationToken.None);

        Assert.Equal(1, resolved.ResolvedCount);
        Assert.Empty(after.Captures);
        // Dismissed items are kept: months of them are the only honest record of what
        // actually pulls attention away.
        var stored = Assert.Single(await store.Value.ListBySessionAsync(summary.SessionId!.Value));
        Assert.Equal(CaptureResolution.Dismissed, stored.Resolution);
    }

    private static InMemoryFocusService CreateService(
        TemporaryHistoryStore store,
        TimeProvider? clock = null) =>
        new(
            clock,
            captureStore: store.Value,
            historyStore: store.Value);

    private static async Task<SessionSummaryResponse> FinishAndSummarizeAsync(
        InMemoryFocusService service,
        TemporaryHistoryStore store)
    {
        await service.StopNormalSessionAsync(CancellationToken.None);
        return await service.GetSessionSummaryAsync(
            new SessionSummaryRequest(),
            CancellationToken.None);
    }

    private static ActivateSessionRequest Request(int durationSeconds) =>
        new(Guid.NewGuid(), durationSeconds, false, 0);

    private sealed class TemporaryHistoryStore : IDisposable
    {
        private readonly DirectoryInfo _directory;

        public TemporaryHistoryStore()
        {
            _directory = Directory.CreateTempSubdirectory("FocusGuard-capture-");
            Value = new SqliteSessionHistoryStore(Path.Combine(_directory.FullName, "history.db"));
        }

        public SqliteSessionHistoryStore Value { get; }

        public void Dispose() => _directory.Delete(recursive: true);
    }

    private sealed class TestClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
