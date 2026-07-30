using System.ComponentModel;
using System.Windows.Input;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

public sealed class StrictConfirmationViewModel : INotifyPropertyChanged
{
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _endUtc;
    private bool _acknowledgeRecoverySaved;
    private bool _acknowledgeUnsavedWork;
    private string _typedConfirmation = string.Empty;
    private string _statusMessage = UiStrings.Get("StrictReviewInitial");

    public StrictConfirmationViewModel(
        TimeProvider? timeProvider = null,
        StrictConfirmationPhraseGenerator? strictPhraseGenerator = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _endUtc = _timeProvider.GetUtcNow().ToUniversalTime().AddHours(2);
        RequiredConfirmationText =
            (strictPhraseGenerator ?? new StrictConfirmationPhraseGenerator()).Generate();
        StartStrictFocusCommand = new DelegateCommand(StartStrictFocus, () => CanActivate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProfileName { get; } = UiStrings.Get("DefaultProfileName");

    public IReadOnlyList<string> ImpactedApplications { get; } = ["Focus.exe", "GameClient.exe", "VideoEditor.exe", "Chat.exe"];

    public IReadOnlyList<string> ImpactedWebsites { get; } = ["example.com", "*.social.example", "*://video.example/*"];

    public string BrowserHealth { get; } = UiStrings.Get("SimulatorBrowserReady");

    public string RecoveryInstructions { get; } = UiStrings.Get("SimulatorRecoveryInstructions");

    public string UnsavedWorkWarning { get; } = UiStrings.Get("UnsavedWorkWarning");

    public string RequiredConfirmationText { get; }

    public string StrictConfirmationPromptText =>
        string.Format(UiStrings.StrictConfirmationPrompt, RequiredConfirmationText);

    public string EndTimeText => _endUtc.ToLocalTime().ToString("g");

    public string ActivationButtonText => string.Format(UiStrings.Get("StartStrictUntil"), EndTimeText);

    public bool AcknowledgeRecoverySaved
    {
        get => _acknowledgeRecoverySaved;
        set
        {
            if (_acknowledgeRecoverySaved == value)
            {
                return;
            }

            _acknowledgeRecoverySaved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanActivate));
        }
    }

    public bool AcknowledgeUnsavedWork
    {
        get => _acknowledgeUnsavedWork;
        set
        {
            if (_acknowledgeUnsavedWork == value)
            {
                return;
            }

            _acknowledgeUnsavedWork = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanActivate));
        }
    }

    public string TypedConfirmation
    {
        get => _typedConfirmation;
        set
        {
            if (_typedConfirmation == value)
            {
                return;
            }

            _typedConfirmation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConfirmationValid));
            OnPropertyChanged(nameof(CanActivate));
        }
    }

    public bool IsConfirmationValid => string.Equals(TypedConfirmation.Trim(), RequiredConfirmationText, StringComparison.Ordinal);

    public bool CanActivate => AcknowledgeRecoverySaved && AcknowledgeUnsavedWork && IsConfirmationValid;

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

    public ICommand StartStrictFocusCommand { get; }

    private void StartStrictFocus()
    {
        StatusMessage = UiStrings.Get("SimulatorStrictReviewComplete");
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
