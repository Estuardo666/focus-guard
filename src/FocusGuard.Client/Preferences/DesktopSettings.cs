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
            if (!File.Exists(_path))
            {
                return DesktopSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<DesktopSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return settings is not null
                && Enum.IsDefined(settings.SessionEnd)
                && Enum.IsDefined(settings.Theme)
                && settings.LastDurationMinutes is >= 1 and <= 720
                && Enum.IsDefined(settings.LastSessionMode)
                && settings.UiLanguage is "es" or "en"
                && (settings.ProfileThemes is null
                    || settings.ProfileThemes.Values.All(Enum.IsDefined))
                    ? settings
                    : DesktopSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return DesktopSettings.Default;
        }
    }

    public void Save(DesktopSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!_persist) { _memory = settings; return; }
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (IOException)
        {
            // A preference that fails to persist is worth less than a crash on shutdown.
        }
    }
}
