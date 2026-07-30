using System.Globalization;
using FocusGuard.Domain;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage;

/// <summary>
/// One finished session, written when the session leaves the active state. This is what
/// the end-of-session window reads, and what the streak is counted from.
/// </summary>
public sealed record SessionHistoryEntry(
    Guid SessionId,
    ProfileId ProfileId,
    string ProfileName,
    DateTimeOffset StartUtc,
    DateTimeOffset EndedAtUtc,
    bool Completed,
    int PlannedMinutes,
    int FocusedMinutes,
    int BlockedAttempts);

public interface ISessionHistoryStore
{
    Task RecordAsync(SessionHistoryEntry entry, CancellationToken cancellationToken = default);

    Task<SessionHistoryEntry?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<SessionHistoryEntry?> GetMostRecentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consecutive local days, counting back from <paramref name="localToday"/>, on which
    /// at least one session ran to completion. Today counts only if it already has one,
    /// but yesterday still anchors the streak so the number does not reset at midnight.
    /// </summary>
    Task<int> GetCompletedStreakDaysAsync(
        DateOnly localToday,
        TimeSpan localOffset,
        CancellationToken cancellationToken = default);
}

public interface ICaptureStore
{
    Task AddAsync(CapturedItem item, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapturedItem>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<int> ResolveAsync(
        IReadOnlyList<Guid> captureIds,
        CaptureResolution resolution,
        DateTimeOffset resolvedAtUtc,
        CancellationToken cancellationToken = default);

    Task<int> CountBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite-backed session history and capture inbox. Dismissed captures are retained, not
/// deleted: over months they are the only honest record of what actually pulls attention.
/// </summary>
public sealed class SqliteSessionHistoryStore : ISessionHistoryStore, ICaptureStore
{
    public const int MaximumCapturesPerSession = 200;
    private readonly string _connectionString;

    public SqliteSessionHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task RecordAsync(SessionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            """
            INSERT INTO SessionHistory
                (SessionId, ProfileId, ProfileName, StartUtc, EndedAtUtc, Completed,
                 PlannedMinutes, FocusedMinutes, BlockedAttempts)
            VALUES
                ($sessionId, $profileId, $profileName, $startUtc, $endedAtUtc, $completed,
                 $plannedMinutes, $focusedMinutes, $blockedAttempts)
            ON CONFLICT(SessionId) DO UPDATE SET
                ProfileName = excluded.ProfileName,
                EndedAtUtc = excluded.EndedAtUtc,
                Completed = excluded.Completed,
                FocusedMinutes = excluded.FocusedMinutes,
                BlockedAttempts = excluded.BlockedAttempts;
            """,
            cancellationToken,
            ("$sessionId", entry.SessionId.ToString("D")),
            ("$profileId", entry.ProfileId.Value.ToString("D")),
            ("$profileName", entry.ProfileName.Trim()),
            ("$startUtc", entry.StartUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$endedAtUtc", entry.EndedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$completed", entry.Completed ? 1 : 0),
            ("$plannedMinutes", entry.PlannedMinutes),
            ("$focusedMinutes", entry.FocusedMinutes),
            ("$blockedAttempts", entry.BlockedAttempts));
    }

    public async Task<SessionHistoryEntry?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{HistorySelect} WHERE SessionId = $sessionId LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<SessionHistoryEntry?> GetMostRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{HistorySelect} ORDER BY EndedAtUtc DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEntry(reader) : null;
    }

    public async Task<int> GetCompletedStreakDaysAsync(
        DateOnly localToday,
        TimeSpan localOffset,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EndedAtUtc FROM SessionHistory WHERE Completed = 1 ORDER BY EndedAtUtc DESC LIMIT 2000;";

        var days = new HashSet<DateOnly>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (DateTimeOffset.TryParse(
                        reader.GetString(0),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var endedAt))
                {
                    days.Add(DateOnly.FromDateTime(endedAt.ToOffset(localOffset).DateTime));
                }
            }
        }

        if (days.Count == 0)
        {
            return 0;
        }

        // A day with no session yet does not break a streak until it is over, so the
        // count may start at yesterday.
        var cursor = days.Contains(localToday) ? localToday : localToday.AddDays(-1);
        var streak = 0;
        while (days.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    public async Task AddAsync(CapturedItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateCapture(item);

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            """
            INSERT INTO Captures
                (Id, SessionId, Text, SourceKind, SourceValue, CapturedAtUtc, ResolvedAtUtc, Resolution)
            SELECT $id, $sessionId, $text, $sourceKind, $sourceValue, $capturedAtUtc, NULL, $resolution
            WHERE (SELECT COUNT(*) FROM Captures WHERE SessionId = $sessionId) < $maximum;
            """,
            cancellationToken,
            ("$id", item.Id.ToString("D")),
            ("$sessionId", item.SessionId.ToString("D")),
            ("$text", item.Text),
            ("$sourceKind", (int)item.SourceKind),
            ("$sourceValue", item.SourceValue),
            ("$capturedAtUtc", item.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("$resolution", (int)item.Resolution),
            ("$maximum", MaximumCapturesPerSession));
    }

    public async Task<IReadOnlyList<CapturedItem>> ListBySessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Array.Empty<CapturedItem>();
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SessionId, Text, SourceKind, SourceValue, CapturedAtUtc, ResolvedAtUtc, Resolution
            FROM Captures
            WHERE SessionId = $sessionId
            ORDER BY CapturedAtUtc ASC
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$maximum", MaximumCapturesPerSession);

        var items = new List<CapturedItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CapturedItem(
                ParseGuid(reader.GetString(0), "capture ID"),
                ParseGuid(reader.GetString(1), "capture session ID"),
                reader.GetString(2),
                ParseEnum<CaptureSourceKind>(reader.GetInt32(3), "capture source kind"),
                reader.GetString(4),
                ParseTimestamp(reader.GetString(5)),
                reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
                ParseEnum<CaptureResolution>(reader.GetInt32(7), "capture resolution")));
        }

        return items;
    }

    public async Task<int> ResolveAsync(
        IReadOnlyList<Guid> captureIds,
        CaptureResolution resolution,
        DateTimeOffset resolvedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureIds);
        if (resolution is not (CaptureResolution.Opened or CaptureResolution.Dismissed))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        if (captureIds.Count is 0 or > MaximumCapturesPerSession)
        {
            return 0;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();
        var resolved = 0;
        foreach (var captureId in captureIds)
        {
            if (captureId == Guid.Empty)
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE Captures
                SET Resolution = $resolution, ResolvedAtUtc = $resolvedAtUtc
                WHERE Id = $id AND Resolution = $pending;
                """;
            command.Parameters.AddWithValue("$resolution", (int)resolution);
            command.Parameters.AddWithValue(
                "$resolvedAtUtc",
                resolvedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", captureId.ToString("D"));
            command.Parameters.AddWithValue("$pending", (int)CaptureResolution.Pending);
            resolved += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return resolved;
    }

    public async Task<int> CountBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return 0;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Captures WHERE SessionId = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private const string HistorySelect =
        """
        SELECT SessionId, ProfileId, ProfileName, StartUtc, EndedAtUtc, Completed,
               PlannedMinutes, FocusedMinutes, BlockedAttempts
        FROM SessionHistory
        """;

    private static SessionHistoryEntry ReadEntry(SqliteDataReader reader) =>
        new(
            ParseGuid(reader.GetString(0), "session ID"),
            new ProfileId(ParseGuid(reader.GetString(1), "profile ID")),
            reader.GetString(2),
            ParseTimestamp(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8));

    private static void ValidateEntry(SessionHistoryEntry entry)
    {
        if (entry.SessionId == Guid.Empty
            || entry.ProfileId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(entry.ProfileName)
            || entry.ProfileName.Length > 80
            || entry.PlannedMinutes < 0
            || entry.FocusedMinutes < 0
            || entry.BlockedAttempts < 0)
        {
            throw new ArgumentException("Session history entry is invalid.", nameof(entry));
        }
    }

    private static void ValidateCapture(CapturedItem item)
    {
        if (item.Id == Guid.Empty
            || item.SessionId == Guid.Empty
            || string.IsNullOrWhiteSpace(item.Text)
            || item.Text.Length > CapturedItem.MaximumTextLength
            || !Enum.IsDefined(item.SourceKind)
            || !Enum.IsDefined(item.Resolution)
            || string.IsNullOrWhiteSpace(item.SourceValue)
            || item.SourceValue.Length > CapturedItem.MaximumSourceLength)
        {
            throw new ArgumentException("Captured item is invalid.", nameof(item));
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SessionHistory (
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
            CREATE INDEX IF NOT EXISTS IX_SessionHistory_EndedAtUtc
                ON SessionHistory (EndedAtUtc DESC);
            CREATE TABLE IF NOT EXISTS Captures (
                Id TEXT NOT NULL PRIMARY KEY,
                SessionId TEXT NOT NULL,
                Text TEXT NOT NULL,
                SourceKind INTEGER NOT NULL,
                SourceValue TEXT NOT NULL,
                CapturedAtUtc TEXT NOT NULL,
                ResolvedAtUtc TEXT NULL,
                Resolution INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Captures_SessionId ON Captures (SessionId);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid ParseGuid(string value, string description) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"Stored {description} is invalid.");

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : throw new InvalidDataException("Stored timestamp is invalid.");

    private static TEnum ParseEnum<TEnum>(int value, string description)
        where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)(object)value
            : throw new InvalidDataException($"Stored {description} is invalid.");
}
