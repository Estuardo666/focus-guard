using System.Text.Json;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service;

/// <summary>
/// One small file holding the history entry of the running normal session, refreshed while it
/// runs and removed when it closes. A file left behind means the previous service process died
/// without closing the session. It lives beside history.db, writable only by SYSTEM,
/// LocalService and Administrators.
/// </summary>
public sealed class NormalSessionJournal(string path)
{
    public const int SchemaVersion = 1;
    public const int MaximumBytes = 64 * 1024;
    private static readonly TimeSpan MaximumSpan = TimeSpan.FromDays(31);

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FocusGuard",
        "normal-session-journal.json");

    public bool Exists => File.Exists(Path);

    public async Task WriteAsync(SessionHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var record = new JournalRecord(
            SchemaVersion,
            entry.SessionId,
            entry.ProfileId.Value,
            entry.ProfileName,
            entry.StartUtc,
            entry.EndedAtUtc,
            entry.PlannedMinutes,
            entry.FocusedMinutes,
            [.. entry.Phases ?? []],
            entry.CycleMetrics,
            entry.ScheduledIntent);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > MaximumBytes || !IsValid(record))
        {
            throw new InvalidDataException("Normal session journal entry is invalid.");
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>
    /// Reads the journal. A corrupt or out-of-bounds file is moved aside and reported as absent,
    /// so it can be inspected but never turns into history.
    /// </summary>
    public async Task<SessionHistoryEntry?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        JournalRecord? record = null;
        try
        {
            var info = new FileInfo(Path);
            if (info.Length is > 0 and <= MaximumBytes)
            {
                var bytes = await File.ReadAllBytesAsync(Path, cancellationToken).ConfigureAwait(false);
                record = JsonSerializer.Deserialize<JournalRecord>(bytes);
            }
        }
        catch (JsonException)
        {
            record = null;
        }

        if (record is null || !IsValid(record))
        {
            Quarantine();
            return null;
        }

        return new SessionHistoryEntry(
            record.SessionId,
            new ProfileId(record.ProfileId),
            record.ProfileName,
            record.StartUtc,
            record.LastSeenUtc,
            Completed: false,
            record.PlannedMinutes,
            record.FocusedMinutes,
            BlockedAttempts: 0,
            record.Phases,
            record.CycleMetrics,
            record.ScheduledIntent);
    }

    public void Delete()
    {
        File.Delete(Path);
        File.Delete(Path + ".tmp");
    }

    private void Quarantine()
    {
        var target = $"{Path}.invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(Path, target, overwrite: true);
    }

    private static bool IsValid(JournalRecord record) =>
        record.SchemaVersion == SchemaVersion
        && record.SessionId != Guid.Empty
        && record.ProfileId != Guid.Empty
        && !string.IsNullOrWhiteSpace(record.ProfileName)
        && record.ProfileName.Length <= 80
        && record.StartUtc.Offset == TimeSpan.Zero
        && record.LastSeenUtc.Offset == TimeSpan.Zero
        && record.StartUtc <= record.LastSeenUtc
        && record.LastSeenUtc - record.StartUtc <= MaximumSpan
        && record.PlannedMinutes is >= 1 and <= 44_640
        && record.FocusedMinutes >= 0
        && record.FocusedMinutes <= record.PlannedMinutes
        && record.Phases is { Length: <= 256 }
        && record.Phases.All(phase => phase is not null
            && Enum.IsDefined(phase.Kind)
            && phase.StartUtc >= record.StartUtc
            && phase.StartUtc <= phase.EndUtc
            && phase.EndUtc <= record.LastSeenUtc)
        && (record.CycleMetrics is null || record.CycleMetrics.IsValid())
        && (record.ScheduledIntent is null
            || record.ScheduledIntent.IsValid()
                && string.Equals(record.ScheduledIntent.ProfileName, record.ProfileName, StringComparison.Ordinal));

    private sealed record JournalRecord(
        int SchemaVersion,
        Guid SessionId,
        Guid ProfileId,
        string ProfileName,
        DateTimeOffset StartUtc,
        DateTimeOffset LastSeenUtc,
        int PlannedMinutes,
        int FocusedMinutes,
        SessionHistoryPhase[] Phases,
        CycleHistoryMetrics? CycleMetrics,
        ScheduledSessionIntent? ScheduledIntent);
}
