using System.Text.Json;
using FocusGuard.Domain;

namespace FocusGuard.Client.Preferences;

/// <summary>
/// How the end of a session is announced. The window is the default because the summary
/// is the moment the loop closes, but forcing a window on someone who has already moved
/// on is exactly the kind of thing that gets an app uninstalled, so it is a choice.
/// </summary>
public enum SessionEndPresentation
{
    Window,
    Notification,
    DimmedWindow,
    None
}

/// <summary>
/// Which of the two Zen palettes paints the shell. Both are first-class: Dark Zen stays
/// the default because focus work skews toward dim rooms, not because White Zen is a
/// fallback.
/// </summary>
public enum AppTheme
{
    Dark,
    Light
}

public sealed record DesktopSettings(
    SessionEndPresentation SessionEnd = SessionEndPresentation.Window,
    AppTheme Theme = AppTheme.Dark,
    bool ReduceMotion = false,
    Dictionary<Guid, AppTheme>? ProfileThemes = null,
    bool EnforcementEnabled = false,
    bool CyclePhaseNotifications = true,
    Guid? LastProfileId = null,
    int LastDurationMinutes = 50,
    bool CompactTopmost = false,
    SessionMode LastSessionMode = SessionMode.SimpleTimer,
    Guid? LastCyclePresetId = null,
    string UiLanguage = "es")
{
    public static DesktopSettings Default { get; } = new();
}

/// <summary>
/// Resolves visual ownership without putting presentation state in the domain profile.
/// A missing or invalid persisted preference falls through predictably.
/// </summary>
public static class ThemePreferenceResolver
{
    public static AppTheme Resolve(
        AppTheme? profileTheme,
        AppTheme? globalTheme,
        AppTheme defaultTheme = AppTheme.Dark) =>
        profileTheme ?? globalTheme ?? defaultTheme;
}

/// <summary>
/// Small JSON file next to the profile database. Deliberately not in the service: this is
/// presentation preference, and losing it must never affect enforcement.
/// </summary>
public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly bool _persist;
    private DesktopSettings? _memory;
    private DesktopSettings? _lastLoaded;
    public string FilePath => _path;
    public string DataDirectory => Path.GetDirectoryName(_path)!;
    public bool Persists => _persist;

    public DesktopSettingsStore(string? path = null, bool persist = true)
    {
        _persist = persist;
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusGuard",
            "desktop-settings.json"));
        var directory = Path.GetDirectoryName(_path);
        if (_persist && !string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public DesktopSettings Load()
    {
        if (!_persist) return _memory ?? DesktopSettings.Default;
        try
        {
            using var lease = DesktopSettingsFileLease.AcquireAsync(_path).GetAwaiter().GetResult();
            var bytes = lease.ReadAsync().GetAwaiter().GetResult();
            var settings = bytes is null ? DesktopSettings.Default : Parse(bytes);
            _lastLoaded = Clone(settings);
            return Clone(settings);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            _lastLoaded = null;
            return DesktopSettings.Default;
        }
    }

    public void Save(DesktopSettings settings)
    {
        try { SaveVerified(settings); }
        catch (IOException) { /* Preserve historical shutdown behavior; callers needing confirmation use SaveVerified. */ }
    }

    /// <summary>Returns only after a shared-lock, atomic durable replacement; propagates failures.</summary>
    public void SaveVerified(DesktopSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        if (!_persist) { _memory = Clone(settings); _lastLoaded = Clone(settings); return; }
        using var lease = DesktopSettingsFileLease.AcquireAsync(_path).GetAwaiter().GetResult();
        var baseline = _lastLoaded;
        byte[]? original;
        try { original = lease.ReadAsync().GetAwaiter().GetResult(); }
        catch (InvalidDataException) when (baseline is null) { original = null; }
        DesktopSettings disk;
        try { disk = original is null ? DesktopSettings.Default : Parse(original); }
        catch (Exception exception) when (baseline is null && exception is (JsonException or InvalidDataException))
        {
            // A user-initiated local save can repair unreadable legacy preferences. Sync
            // projection remains stricter and rejects the same file without replacing it.
            original = null;
            disk = DesktopSettings.Default;
        }
        if (original is not null && baseline is null)
            throw new InvalidOperationException("Load the preferences before saving over an existing file.");
        var merged = baseline is null ? settings : MergeChanges(baseline, settings, disk);
        Validate(merged);
        var node = original is null ? new System.Text.Json.Nodes.JsonObject() :
            System.Text.Json.Nodes.JsonNode.Parse(original) as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("Invalid preferences object.");
        var known = JsonSerializer.SerializeToNode(merged, JsonOptions)!.AsObject();
        foreach (var property in known) node[property.Key] = property.Value?.DeepClone();
        lease.ReplaceAsync(JsonSerializer.SerializeToUtf8Bytes(node, JsonOptions)).GetAwaiter().GetResult();
        _memory = null; _lastLoaded = Clone(merged);
    }

    /// <summary>Compare the three shared preferences and apply selected values under the same durable writer lease.</summary>
    public bool TryApplySharedIfUnchanged(AppTheme expectedTheme, string expectedLanguage, int expectedDuration,
        AppTheme? theme, string? language, int? duration, out DesktopSettings? updated)
    {
        if (language is not (null or "es" or "en") || duration is < 1 or > 720 || theme is { } selectedTheme && !Enum.IsDefined(selectedTheme))
            throw new InvalidDataException("Invalid shared preference update.");
        if (!_persist)
        {
            var current = _memory ?? DesktopSettings.Default;
            if (current.Theme != expectedTheme || current.UiLanguage != expectedLanguage || current.LastDurationMinutes != expectedDuration)
            { updated = null; return false; }
            updated = current with { Theme = theme ?? current.Theme, UiLanguage = language ?? current.UiLanguage,
                LastDurationMinutes = duration ?? current.LastDurationMinutes };
            Validate(updated); _memory = Clone(updated); _lastLoaded = Clone(updated); return true;
        }

        using var lease = DesktopSettingsFileLease.AcquireAsync(_path).GetAwaiter().GetResult();
        var original = lease.ReadAsync().GetAwaiter().GetResult();
        var currentDisk = original is null ? DesktopSettings.Default : Parse(original);
        if (currentDisk.Theme != expectedTheme || currentDisk.UiLanguage != expectedLanguage || currentDisk.LastDurationMinutes != expectedDuration)
        { updated = null; return false; }
        updated = currentDisk with { Theme = theme ?? currentDisk.Theme, UiLanguage = language ?? currentDisk.UiLanguage,
            LastDurationMinutes = duration ?? currentDisk.LastDurationMinutes };
        Validate(updated);
        var node = original is null ? new System.Text.Json.Nodes.JsonObject() :
            System.Text.Json.Nodes.JsonNode.Parse(original) as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("Invalid preferences object.");
        var known = JsonSerializer.SerializeToNode(updated, JsonOptions)!.AsObject();
        foreach (var property in known) node[property.Key] = property.Value?.DeepClone();
        lease.ReplaceAsync(JsonSerializer.SerializeToUtf8Bytes(node, JsonOptions)).GetAwaiter().GetResult();
        _memory = null; _lastLoaded = Clone(updated);
        return true;
    }

    private static DesktopSettings Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes); ValidateUnique(document.RootElement);
        var settings = JsonSerializer.Deserialize<DesktopSettings>(bytes, JsonOptions);
        if (settings is null) throw new InvalidDataException("Invalid preferences.");
        Validate(settings); return settings;
    }

    private static void ValidateUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous preferences field.");
                ValidateUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateUnique(child);
    }

    private static void Validate(DesktopSettings settings)
    {
        if (!Enum.IsDefined(settings.SessionEnd) || !Enum.IsDefined(settings.Theme) ||
            !Enum.IsDefined(settings.LastSessionMode) || settings.LastDurationMinutes is < 1 or > 720 ||
            settings.UiLanguage is not ("es" or "en") || settings.ProfileThemes is not null && settings.ProfileThemes.Values.Any(value => !Enum.IsDefined(value)))
            throw new InvalidDataException("Invalid preferences values.");
    }

    private static DesktopSettings Clone(DesktopSettings settings) =>
        settings with { ProfileThemes = settings.ProfileThemes is null ? null : new(settings.ProfileThemes) };

    private static DesktopSettings MergeChanges(DesktopSettings baseline, DesktopSettings proposed, DesktopSettings disk)
    {
        static T Select<T>(T oldValue, T changedValue, T latestValue) => EqualityComparer<T>.Default.Equals(oldValue, changedValue) ? latestValue : changedValue;
        var profiles = disk.ProfileThemes is null ? new Dictionary<Guid, AppTheme>() : new(disk.ProfileThemes);
        var oldProfiles = baseline.ProfileThemes ?? new Dictionary<Guid, AppTheme>();
        var newProfiles = proposed.ProfileThemes ?? new Dictionary<Guid, AppTheme>();
        foreach (var id in oldProfiles.Keys.Union(newProfiles.Keys))
        {
            var hadOld = oldProfiles.TryGetValue(id, out var oldTheme);
            var hasNew = newProfiles.TryGetValue(id, out var newTheme);
            if (hadOld == hasNew && (!hadOld || oldTheme == newTheme)) continue;
            if (hasNew) profiles[id] = newTheme; else profiles.Remove(id);
        }
        return disk with
        {
            SessionEnd = Select(baseline.SessionEnd, proposed.SessionEnd, disk.SessionEnd),
            Theme = Select(baseline.Theme, proposed.Theme, disk.Theme),
            ReduceMotion = Select(baseline.ReduceMotion, proposed.ReduceMotion, disk.ReduceMotion),
            ProfileThemes = profiles.Count == 0 && disk.ProfileThemes is null && newProfiles.Count == 0 ? null : profiles,
            EnforcementEnabled = Select(baseline.EnforcementEnabled, proposed.EnforcementEnabled, disk.EnforcementEnabled),
            CyclePhaseNotifications = Select(baseline.CyclePhaseNotifications, proposed.CyclePhaseNotifications, disk.CyclePhaseNotifications),
            LastProfileId = Select(baseline.LastProfileId, proposed.LastProfileId, disk.LastProfileId),
            LastDurationMinutes = Select(baseline.LastDurationMinutes, proposed.LastDurationMinutes, disk.LastDurationMinutes),
            CompactTopmost = Select(baseline.CompactTopmost, proposed.CompactTopmost, disk.CompactTopmost),
            LastSessionMode = Select(baseline.LastSessionMode, proposed.LastSessionMode, disk.LastSessionMode),
            LastCyclePresetId = Select(baseline.LastCyclePresetId, proposed.LastCyclePresetId, disk.LastCyclePresetId),
            UiLanguage = Select(baseline.UiLanguage, proposed.UiLanguage, disk.UiLanguage)
        };
    }
}
