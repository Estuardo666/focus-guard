using System.ComponentModel;
using System.Windows.Input;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

public sealed class ActiveFocusViewModel : INotifyPropertyChanged
{
    private readonly TimeProvider _timeProvider;
    private Session _session;
    private TimeSpan _remaining;
    private string _statusMessage = UiStrings.Get("SimulatorProtectionRunning");

    public ActiveFocusViewModel(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var startUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        _session = Session.Create(
                ProfileId.New(),
                startUtc,
                TimeSpan.FromMinutes(50),
                StrictModeOptions.Normal).Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);
        _remaining = _session.EndUtc - startUtc;
        StopFocusCommand = new DelegateCommand(StopFocus, () => CanStop);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProfileName { get; } = UiStrings.Get("DefaultProfileName");

    public int WebsiteRuleCount { get; } = 12;

    public int ApplicationRuleCount { get; } = 4;

    public bool IsStrictMode => _session.StrictMode.Enabled;

    public string ModeLabel => IsStrictMode ? UiStrings.StrictMode : UiStrings.NormalMode;

    public string HealthLabel { get; } = UiStrings.Get("SimulatorHealthReady");

    public int RecentBlockCount { get; private set; }

    public SessionState State => _session.State;

    public bool IsExpired => State == SessionState.Expired;

    public bool CanStop => State == SessionState.Active && !IsStrictMode;

    public TimeSpan Remaining => _remaining;

    public string RemainingText => FormatRemaining(_remaining);

    public string ExactEndText => _session.EndUtc.ToLocalTime().ToString("g");

    public string ExactEndDisplayText => string.Format(UiStrings.Get("EndsAt"), ExactEndText);

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand StopFocusCommand { get; }

    public void Refresh()
    {
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        _session = _session.ExpireIfDue(nowUtc);
        _remaining = _session.State == SessionState.Expired
            ? TimeSpan.Zero
            : _session.EndUtc - nowUtc;

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsExpired));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ExactEndText));
        OnPropertyChanged(nameof(ExactEndDisplayText));
        if (IsExpired)
        {
            StatusMessage = UiStrings.Get("SimulatorFocusEnded");
        }
    }

    private void StopFocus()
    {
        if (!CanStop)
        {
            return;
        }

        _session = _session.TransitionTo(SessionState.Stopped);
        _remaining = TimeSpan.Zero;
        StatusMessage = UiStrings.Get("SimulatorFocusStopped");
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(RemainingText));
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var safeRemaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        return safeRemaining.TotalHours >= 1
            ? $"{(int)safeRemaining.TotalHours:00}:{safeRemaining.Minutes:00}:{safeRemaining.Seconds:00}"
            : $"{safeRemaining.Minutes:00}:{safeRemaining.Seconds:00}";
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        event EventHandler? ICommand.CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();
    }
}
