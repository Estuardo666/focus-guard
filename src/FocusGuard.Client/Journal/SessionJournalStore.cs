using System.Text.Json;
using FocusGuard.Domain;

namespace FocusGuard.Client.Journal;

public sealed record SessionIntention(Guid SessionId, Guid ProfileId, string ProfileName, int DurationMinutes, SessionMode Mode,
    Guid? CyclePresetId, FocusCycleConfiguration? CycleConfiguration, bool Strict, string Objective, string Reflection = "",
    bool? EnforcementEnabled = null);

/// <summary>Optional local writing. No URL data, credentials or service authority.</summary>
public sealed class SessionJournalStore
{
    private Dictionary<Guid, SessionIntention>? _entries;
    private readonly string? _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public SessionJournalStore(string? path) => _path = path is null ? null : Path.GetFullPath(path);

    public SessionIntention? Get(Guid sessionId)
    {
        if (sessionId == Guid.Empty) return null;
        lock (_gate)
        {
            _entries ??= ReadEntries();
            return _entries.GetValueOrDefault(sessionId);
        }
    }

    public SessionIntention? GetLatest(Guid sessionId)
    {
        if (sessionId == Guid.Empty) return null;
        lock (_gate)
        {
            if (_path is null)
            {
                _entries = LoadMemory();
                return _entries.GetValueOrDefault(sessionId);
            }

            FileStream? fileLock = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                fileLock = AcquireWriteLock(_path + ".lock");
                if (fileLock is null) return null;
                _entries = ReadEntries();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            finally { fileLock?.Dispose(); }
            return _entries.GetValueOrDefault(sessionId);
        }
    }

    public void Save(SessionIntention entry) => SaveCore(entry, onlyIfAbsent: false);

    /// <summary>Observation cannot replace an intention accepted by another UI process.</summary>
    public void SaveIfAbsent(SessionIntention entry) => SaveCore(entry, onlyIfAbsent: true);

    /// <summary>Known schedule intent fills an absent or consent-unknown observation without replacing accepted intent.</summary>
    public void SaveObserved(SessionIntention entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.EnforcementEnabled is not null) SaveCore(entry, onlyIfAbsent: true, replaceUnknownConsent: true);
    }

    private void SaveCore(SessionIntention entry, bool onlyIfAbsent, bool replaceUnknownConsent = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SessionId == Guid.Empty) return;
        var normalized = entry with { Objective = Limit(entry.Objective, 240), Reflection = Limit(entry.Reflection, 1000) };
        lock (_gate)
        {
            if (_path is null)
            {
                _entries = LoadMemory();
                if (onlyIfAbsent && _entries.TryGetValue(entry.SessionId, out var current))
                {
                    if (!replaceUnknownConsent || !CanReplaceUnknownConsent(current, normalized)) return;
                    normalized = PreserveLocalNotes(normalized, current);
                    if (current == normalized) return;
                }
                AddAndTrim(_entries, normalized);
                return;
            }

            var directory = Path.GetDirectoryName(_path)!;
            string? temporary = null;
            FileStream? fileLock = null;
            try
            {
                Directory.CreateDirectory(directory);
                fileLock = AcquireWriteLock(_path + ".lock");
                if (fileLock is null) return;

                var latest = ReadEntries();
                if (onlyIfAbsent && latest.TryGetValue(entry.SessionId, out var current))
                {
                    if (!replaceUnknownConsent || !CanReplaceUnknownConsent(current, normalized))
                    {
                        _entries = latest;
                        return;
                    }
                    normalized = PreserveLocalNotes(normalized, current);
                    if (current == normalized)
                    {
                        _entries = latest;
                        return;
                    }
                }
                AddAndTrim(latest, normalized);
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, latest.Values.ToArray(), Options);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
                temporary = null;
                _entries = latest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
            {
                // The journal is optional and cannot change whether an accepted session runs.
            }
            finally
            {
                fileLock?.Dispose();
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    /// <summary>Atomically updates the local reflection without replacing newer session metadata.</summary>
    public bool SaveReflection(Guid sessionId, string reflection, SessionIntention? fallback = null)
    {
        if (sessionId == Guid.Empty || fallback is not null && fallback.SessionId != sessionId) return false;
        var boundedReflection = Limit(reflection, 1000);
        lock (_gate)
        {
            if (_path is null)
            {
                _entries ??= [];
                var current = _entries.GetValueOrDefault(sessionId) ?? fallback;
                if (current is null) return false;
                _entries[sessionId] = current with
                {
                    Objective = Limit(current.Objective, 240),
                    Reflection = boundedReflection
                };
                return true;
            }

            string? temporary = null;
            FileStream? fileLock = null;
            try
            {
                var directory = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(directory);
                fileLock = AcquireWriteLock(_path + ".lock");
                if (fileLock is null) return false;

                var latest = ReadEntries();
                var current = latest.GetValueOrDefault(sessionId) ?? fallback;
                if (current is null) return false;
                latest[sessionId] = current with
                {
                    Objective = Limit(current.Objective, 240),
                    Reflection = boundedReflection
                };
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, latest.Values.ToArray(), Options);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
                temporary = null;
                _entries = latest;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
            {
                return false;
            }
            finally
            {
                fileLock?.Dispose();
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    private Dictionary<Guid, SessionIntention> LoadMemory() => _entries ?? [];

    private Dictionary<Guid, SessionIntention> ReadEntries()
    {
        var entries = new Dictionary<Guid, SessionIntention>();
        try
        {
            if (_path is null || !File.Exists(_path) || new FileInfo(_path).Length > 8 * 1024 * 1024) return entries;
            var loaded = JsonSerializer.Deserialize<SessionIntention[]>(File.ReadAllText(_path), Options) ?? [];
            foreach (var e in loaded.TakeLast(1000).Where(e => e is not null && e.SessionId != Guid.Empty && e.ProfileName is { Length: <= 80 } && Enum.IsDefined(e.Mode) && e.DurationMinutes is >= 1 and <= 720))
                entries[e.SessionId] = e with { Objective = Limit(e.Objective, 240), Reflection = Limit(e.Reflection, 1000) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException) { entries.Clear(); }
        return entries;
    }

    private static bool CanReplaceUnknownConsent(SessionIntention current, SessionIntention observed)
        => current.EnforcementEnabled is null && current.ProfileId == observed.ProfileId
            && current.Mode == observed.Mode && current.Strict == observed.Strict;

    private static SessionIntention PreserveLocalNotes(SessionIntention observed, SessionIntention current)
        => observed with { Objective = current.Objective, Reflection = current.Reflection };

    private static void AddAndTrim(Dictionary<Guid, SessionIntention> entries, SessionIntention entry)
    {
        entries[entry.SessionId] = entry;
        while (entries.Count > 1000) entries.Remove(entries.Keys.First());
    }

    private static FileStream? AcquireWriteLock(string path)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < 2000)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(25); }
            catch (UnauthorizedAccessException) { return null; }
        }
        return null;
    }

    private static string Limit(string? text, int maximum) => (text ?? "")[..Math.Min((text ?? "").Length, maximum)].Trim();
}
