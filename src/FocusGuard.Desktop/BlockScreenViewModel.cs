using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

/// <summary>
/// The block screen for a blocked application.
///
/// The guiding idea is that a block screen must not feel like a punishment: the user
/// already decided to block this, and the screen only reminds them. So there is no red,
/// no padlock and no "you can't" — just the accent blue, how long is left, and how much
/// progress walking away would cost.
/// </summary>
public sealed class BlockScreenViewModel : INotifyPropertyChanged
{
    private readonly IFocusControlClient _client;
    private readonly string _executablePath;
    private readonly DateTimeOffset _startUtc;
    private readonly DateTimeOffset _endUtc;
    private readonly TimeProvider _timeProvider;
    private readonly NotifyingDelegateCommand _showCaptureCommand;
    private readonly NotifyingDelegateCommand _cancelCaptureCommand;
    private readonly NotifyingDelegateCommand _saveCaptureCommand;
    private string _captureText;
    private string _captureFeedback = string.Empty;
    private bool _isCaptureVisible;
    private bool _isCaptureSaved;
    private bool _isSaving;

    public BlockScreenViewModel(
        string executablePath,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        bool isStrict,
        IFocusControlClient? client = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _client = client ?? new NamedPipeFocusControlClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _executablePath = executablePath;
        _startUtc = startUtc;
        _endUtc = endUtc;
        IsStrict = isStrict;

        // Pre-filled with what was blocked, so most captures need nothing but Enter.
        _captureText = DisplayName;

        _showCaptureCommand = new NotifyingDelegateCommand(
            () =>
            {
                IsCaptureVisible = true;
                CaptureFeedback = string.Empty;
            },
            () => !_isCaptureSaved && !_isCaptureVisible);
        _cancelCaptureCommand = new NotifyingDelegateCommand(
            () => IsCaptureVisible = false,
            () => _isCaptureVisible);
        _saveCaptureCommand = new NotifyingDelegateCommand(
            () => _ = SaveCaptureAsync(),
            () => _isCaptureVisible && !_isSaving && !_isCaptureSaved);

        Tick();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the screen has nothing left to say and should close itself.</summary>
    public event EventHandler? Dismissed;

    public bool IsStrict { get; }

    public string DisplayName => Path.GetFileName(_executablePath) is { Length: > 0 } name
        ? name
        : _executablePath;

    public string Headline => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.BlockAppHeadline,
        DisplayName);

    public string Context { get; private set; } = string.Empty;

    public string RingValue { get; private set; } = "--";

    public double RingSweepAngle { get; private set; }

    public string FooterMode => IsStrict
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.BlockFooterStrict,
            _endUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
        : UiStrings.BlockFooterNormal;

    public string AutoCloseNotice => UiStrings.BlockAutoClose;

    public string BackLabel => UiStrings.BlockBack;

    public string CaptureLabel => UiStrings.BlockCapture;

    public string CapturePlaceholder => UiStrings.BlockCapturePlaceholder;

    public string CaptureSaveLabel => UiStrings.BlockCaptureSave;

    public string CaptureCancelLabel => UiStrings.BlockCaptureCancel;

    public ICommand ShowCaptureCommand => _showCaptureCommand;

    public ICommand CancelCaptureCommand => _cancelCaptureCommand;

    public ICommand SaveCaptureCommand => _saveCaptureCommand;

    public string CaptureText
    {
        get => _captureText;
        set => SetField(ref _captureText, value);
    }

    public string CaptureFeedback
    {
        get => _captureFeedback;
        private set
        {
            if (SetField(ref _captureFeedback, value))
            {
                OnPropertyChanged(nameof(HasCaptureFeedback));
            }
        }
    }

    public bool HasCaptureFeedback => !string.IsNullOrEmpty(_captureFeedback);

    public bool IsCaptureVisible
    {
        get => _isCaptureVisible;
        private set
        {
            if (SetField(ref _isCaptureVisible, value))
            {
                OnPropertyChanged(nameof(IsCaptureButtonVisible));
                NotifyCommands();
            }
        }
    }

    public bool IsCaptureButtonVisible => !_isCaptureVisible && !_isCaptureSaved;

    /// <summary>
    /// Recomputes the two facts that actually send someone back to work: how little is
    /// left, and how much has already been earned.
    /// </summary>
    public void Tick()
    {
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var remaining = _endUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var total = _endUtc - _startUtc;
        var elapsed = nowUtc - _startUtc;
        var remainingMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
        var elapsedMinutes = (int)Math.Max(0, Math.Floor(elapsed.TotalMinutes));

        RingValue = remainingMinutes.ToString(CultureInfo.CurrentCulture);
        RingSweepAngle = total > TimeSpan.Zero
            ? Math.Clamp(elapsed / total, 0, 1) * 360
            : 0;
        Context = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.BlockContext,
            _endUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
            FormatMinutes(remainingMinutes),
            elapsedMinutes.ToString(CultureInfo.CurrentCulture));

        OnPropertyChanged(nameof(RingValue));
        OnPropertyChanged(nameof(RingSweepAngle));
        OnPropertyChanged(nameof(Context));
    }

    private static string FormatMinutes(int minutes) => minutes switch
    {
        < 1 => UiStrings.BlockLessThanMinute,
        1 => UiStrings.BlockOneMinute,
        _ => string.Format(CultureInfo.CurrentCulture, UiStrings.BlockMinutes, minutes)
    };

    private async Task SaveCaptureAsync()
    {
        _isSaving = true;
        NotifyCommands();
        try
        {
            var response = await _client.CaptureAsync(
                new CaptureRequest(
                    CaptureText,
                    CaptureProtocol.ApplicationKind,
                    _executablePath),
                CancellationToken.None);
            if (response.Accepted)
            {
                _isCaptureSaved = true;
                IsCaptureVisible = false;
                CaptureFeedback = UiStrings.BlockCaptureSaved;
                return;
            }

            CaptureFeedback = UiStrings.BlockCaptureFailed;
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            CaptureFeedback = UiStrings.BlockCaptureFailed;
        }
        finally
        {
            _isSaving = false;
            OnPropertyChanged(nameof(IsCaptureButtonVisible));
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        _showCaptureCommand.RaiseCanExecuteChanged();
        _cancelCaptureCommand.RaiseCanExecuteChanged();
        _saveCaptureCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
