using FocusGuard.Domain;
using FocusGuard.Storage;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage.Tests;

public sealed class SqliteSessionHistoryStoreTests
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAndList_RoundTripsCapturesInOrderOfCapture()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var sessionId = Guid.NewGuid();
        await store.InitializeAsync();

        await store.AddAsync(Capture(sessionId, "Revisar respuesta de Carlos", "https://x.com/", Noon));
        await store.AddAsync(Capture(
            sessionId,
            "Precio del monitor 27\"",
            "https://amazon.com/",
            Noon.AddMinutes(9)));

        var captures = await store.ListBySessionAsync(sessionId);

        Assert.Equal(2, captures.Count);
        Assert.Equal("Revisar respuesta de Carlos", captures[0].Text);
        Assert.Equal("amazon.com", captures[1].SourceLabel);
        Assert.All(captures, capture => Assert.Equal(CaptureResolution.Pending, capture.Resolution));
    }

    [Fact]
    public async Task Resolve_MarksOnlyPendingCapturesAndKeepsTheRowForLaterAnalysis()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var sessionId = Guid.NewGuid();
        var capture = Capture(sessionId, "Preguntarle sobre el deploy", "https://x.com/", Noon);
        await store.AddAsync(capture);

        var first = await store.ResolveAsync(
            [capture.Id],
            CaptureResolution.Dismissed,
            Noon.AddMinutes(50));
        var second = await store.ResolveAsync(
            [capture.Id],
            CaptureResolution.Opened,
            Noon.AddMinutes(51));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var stored = Assert.Single(await store.ListBySessionAsync(sessionId));
        Assert.Equal(CaptureResolution.Dismissed, stored.Resolution);
        Assert.Equal(Noon.AddMinutes(50), stored.ResolvedAtUtc);
    }

    [Fact]
    public async Task Add_StopsAtTheSessionCapLeavingEarlierCapturesIntact()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var sessionId = Guid.NewGuid();
        for (var index = 0; index <= SqliteSessionHistoryStore.MaximumCapturesPerSession; index++)
        {
            await store.AddAsync(Capture(
                sessionId,
                $"Idea {index}",
                "https://x.com/",
                Noon.AddSeconds(index)));
        }

        Assert.Equal(
            SqliteSessionHistoryStore.MaximumCapturesPerSession,
            await store.CountBySessionAsync(sessionId));
    }

    [Fact]
    public async Task Streak_CountsConsecutiveCompletedDaysAndIgnoresStoppedSessions()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var today = new DateOnly(2026, 7, 29);
        await store.RecordAsync(Entry(Noon, completed: true));
        await store.RecordAsync(Entry(Noon.AddDays(-1), completed: true));
        await store.RecordAsync(Entry(Noon.AddDays(-2), completed: true));
        // A session the user ended early does not extend a streak.
        await store.RecordAsync(Entry(Noon.AddDays(-3), completed: false));
        await store.RecordAsync(Entry(Noon.AddDays(-4), completed: true));

        Assert.Equal(3, await store.GetCompletedStreakDaysAsync(today, TimeSpan.Zero));
    }

    [Fact]
    public async Task Streak_SurvivesADayThatHasNotProducedASessionYet()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        await store.RecordAsync(Entry(Noon.AddDays(-1), completed: true));
        await store.RecordAsync(Entry(Noon.AddDays(-2), completed: true));

        // Today is still in progress, so yesterday anchors the count.
        Assert.Equal(
            2,
            await store.GetCompletedStreakDaysAsync(new DateOnly(2026, 7, 29), TimeSpan.Zero));
    }

    [Fact]
    public async Task GetMostRecent_ReturnsTheSessionThatEndedLast()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var latest = Entry(Noon, completed: true);
        await store.RecordAsync(Entry(Noon.AddHours(-3), completed: false));
        await store.RecordAsync(latest);

        var loaded = await store.GetMostRecentAsync();

        Assert.NotNull(loaded);
        Assert.Equal(latest.SessionId, loaded!.SessionId);
        Assert.True(loaded.Completed);
    }

    [Fact]
    public async Task CycleMetrics_RoundTripWithTheHistoryEntry()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteSessionHistoryStore(temporary.DatabasePath);
        var entry = Entry(Noon, completed: true) with
        {
            Phases = [new(SessionPhaseKind.Focus, Noon.AddMinutes(-25), Noon)],
            CycleMetrics = new CycleHistoryMetrics(1, 0),
            ScheduledIntent = new ScheduledSessionIntent("Deep Work", 1, EnforcementEnabled: false)
        };

        await store.RecordAsync(entry);
        var loaded = await store.GetAsync(entry.SessionId);

        Assert.NotNull(loaded);
        Assert.Equal(entry.CycleMetrics, loaded!.CycleMetrics);
        Assert.Equal(entry.Phases, loaded.Phases);
        Assert.Equal(entry.ScheduledIntent, loaded.ScheduledIntent);
    }

    [Fact]
    public async Task LegacyPhaseTable_MigratesWithoutInventingCycleMetrics()
    {
        using var temporary = TemporaryDatabase.Create();
        var sessionId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SessionHistory (
                    SessionId TEXT NOT NULL PRIMARY KEY,
                    ProfileId TEXT NOT NULL,
                    ProfileName TEXT NOT NULL,
                    StartUtc TEXT NOT NULL,
                    EndedAtUtc TEXT NOT NULL,
                    Completed INTEGER NOT NULL,
                    PlannedMinutes INTEGER NOT NULL,
                    FocusedMinutes INTEGER NOT NULL,
                    BlockedAttempts INTEGER NOT NULL
                );
                CREATE TABLE SessionHistoryPhases (
                    SessionId TEXT NOT NULL PRIMARY KEY,
                    PhasesJson TEXT NOT NULL
                );
                INSERT INTO SessionHistory VALUES
                    ($sessionId, $profileId, 'Legacy cycle', $startUtc, $endUtc, 1, 25, 20, 0);
                INSERT INTO SessionHistoryPhases VALUES ($sessionId, '[]');
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
            command.Parameters.AddWithValue("$startUtc", Noon.AddMinutes(-25).ToString("O"));
            command.Parameters.AddWithValue("$endUtc", Noon.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        var migrated = await new SqliteSessionHistoryStore(temporary.DatabasePath).GetAsync(sessionId);

        Assert.NotNull(migrated);
        Assert.Null(migrated!.CycleMetrics);
        Assert.Empty(migrated.Phases!);
        Assert.Null(migrated.ScheduledIntent);
    }

    private static CapturedItem Capture(
        Guid sessionId,
        string text,
        string source,
        DateTimeOffset capturedAtUtc)
    {
        var result = CapturedItem.Create(
            sessionId,
            text,
            CaptureSourceKind.Website,
            source,
            capturedAtUtc);
        Assert.True(result.IsValid);
        return result.Value!;
    }

    private static SessionHistoryEntry Entry(DateTimeOffset endedAtUtc, bool completed) =>
        new(
            Guid.NewGuid(),
            ProfileId.New(),
            "Deep Work",
            endedAtUtc.AddMinutes(-50),
            endedAtUtc,
            completed,
            PlannedMinutes: 50,
            FocusedMinutes: completed ? 50 : 32,
            BlockedAttempts: 14);

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly DirectoryInfo _directory;

        private TemporaryDatabase(DirectoryInfo directory)
        {
            _directory = directory;
            DatabasePath = Path.Combine(directory.FullName, "history.db");
        }

        public string DatabasePath { get; }

        public static TemporaryDatabase Create() =>
            new(Directory.CreateTempSubdirectory("FocusGuard-history-"));

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
