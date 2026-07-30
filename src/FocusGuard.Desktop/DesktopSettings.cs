using System.Text.Json;
using System.Windows.Input;

namespace FocusGuard.Desktop;

/// <summary>
/// The repo's other view models each carry a private DelegateCommand that never raises
/// CanExecuteChanged. The block and summary screens do need to re-evaluate — a capture
/// button stops being available once the capture is saved — so they share this one.
/// </summary>
public sealed class NotifyingDelegateCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class SessionEndedEventArgs(Guid sessionId) : EventArgs
{
    public Guid SessionId { get; } = sessionId;
}

public sealed class ApplicationBlockedEventArgs(string target) : EventArgs
{
    /// <summary>Sanitized by the service to a file name, never a full path.</summary>
    public string Target { get; } = target;
}

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

/// <summary>One row in the Settings picker for how a session should announce its end.</summary>
public sealed record SessionEndOptionViewModel(SessionEndPresentation Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record DesktopSettings(SessionEndPresentation SessionEnd = SessionEndPresentation.Window)
{
    public static DesktopSettings Default { get; } = new();
}

/// <summary>
/// Small JSON file next to the profile database. Deliberately not in the service: this is
/// presentation preference, and losing it must never affect enforcement.
/// </summary>
public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public DesktopSettingsStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusGuard",
            "desktop-settings.json"));
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return DesktopSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<DesktopSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return settings is not null && Enum.IsDefined(settings.SessionEnd)
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
