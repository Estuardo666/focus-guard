using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

/// <summary>
/// One captured item as the end-of-session list shows it: the line, where it came from,
/// and the two things that can be done with it.
/// </summary>
public sealed class CapturedItemViewModel
{
    private readonly CapturedItemView _item;
    private readonly Action<CapturedItemViewModel, bool> _resolve;

    public CapturedItemViewModel(
        CapturedItemView item,
        Action<CapturedItemViewModel, bool> resolve)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        OpenCommand = new NotifyingDelegateCommand(Open, () => true);
        DismissCommand = new NotifyingDelegateCommand(() => _resolve(this, false), () => true);
    }

    public Guid Id => _item.Id;

    public string Text => _item.Text;

    /// <summary>"x.com · 12:32" — enough context to decide in two seconds.</summary>
    public string Origin => string.Create(
        CultureInfo.CurrentCulture,
        $"{_item.SourceLabel} · {_item.CapturedAtUtc.ToLocalTime():t}");

    /// <summary>Segoe Fluent glyph: a globe for a site, a window for an application.</summary>
    public string Glyph => _item.SourceKind == CaptureProtocol.WebsiteKind ? "" : "";

    public string OpenLabel => UiStrings.SummaryOpen;

    public string DismissLabel => UiStrings.SummaryDismiss;

    public ICommand OpenCommand { get; }

    public ICommand DismissCommand { get; }

    private void Open()
    {
        try
        {
            // UseShellExecute is what lets one call handle both an https URL and the
            // full executable path recorded when the application was captured.
            Process.Start(new ProcessStartInfo(_item.SourceValue) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or FileNotFoundException)
        {
            // The target may be gone by now. Resolving it anyway is the honest outcome:
            // the user asked for it, and the item should not linger in the inbox.
        }

        _resolve(this, true);
    }
}

/// <summary>
/// The end-of-session screen. Metrics come before the list on purpose: opening with
/// "you have 3 things to review" feels like an inbox, opening with "50 minutes, 6 day
/// streak" feels like a reward, and the streak is what brings people back tomorrow.
/// </summary>
public sealed class SessionSummaryViewModel : INotifyPropertyChanged
{
    private readonly IFocusControlClient _client;
    private readonly SessionSummaryResponse _summary;

    public SessionSummaryViewModel(
        SessionSummaryResponse summary,
        IFocusControlClient? client = null)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _client = client ?? new NamedPipeFocusControlClient();
        foreach (var capture in summary.Captures)
        {
            Captures.Add(new CapturedItemViewModel(capture, ResolveCapture));
        }

        DismissAllCommand = new NotifyingDelegateCommand(DismissAll, () => Captures.Count > 0);
        AnotherSessionCommand = new NotifyingDelegateCommand(
            () => RequestedAnotherSession?.Invoke(this, EventArgs.Empty),
            () => true);
        TakeBreakCommand = new NotifyingDelegateCommand(
            () => RequestedClose?.Invoke(this, EventArgs.Empty),
            () => true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Chain straight into another session of the same length.</summary>
    public event EventHandler? RequestedAnotherSession;

    public event EventHandler? RequestedClose;

    public ObservableCollection<CapturedItemViewModel> Captures { get; } = [];

    public bool IsCompleted => _summary.Outcome == CaptureProtocol.CompletedOutcome;

    /// <summary>
    /// A session ended early does not get the green check. The header says what happened
    /// without punishing it.
    /// </summary>
    public string Title => IsCompleted
        ? UiStrings.SummaryCompleteTitle
        : UiStrings.SummaryStoppedTitle;

    public string Subtitle => IsCompleted
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SummarySubtitle,
            _summary.ProfileName,
            _summary.FocusedMinutes)
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SummaryStoppedSubtitle,
            _summary.ProfileName,
            _summary.FocusedMinutes,
            _summary.PlannedMinutes);

    public string FocusedValue => string.Create(
        CultureInfo.CurrentCulture,
        $"{_summary.FocusedMinutes} {UiStrings.SummaryUnit}");

    public string FocusedLabel => UiStrings.SummaryFocusedLabel;

    public string BlocksValue => _summary.BlockedAttempts.ToString(CultureInfo.CurrentCulture);

    public string BlocksLabel => UiStrings.SummaryBlocksLabel;

    public string StreakValue => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.SummaryStreakUnit,
        _summary.StreakDays);

    public string StreakLabel => UiStrings.SummaryStreakLabel;

    /// <summary>A streak is only shown for a session that actually ran to its end.</summary>
    public bool HasStreak => IsCompleted && _summary.StreakDays > 0;

    public bool HasCaptures => Captures.Count > 0;

    public string CapturedTitle => Captures.Count == 1
        ? UiStrings.SummaryCapturedOne
        : string.Format(CultureInfo.CurrentCulture, UiStrings.SummaryCapturedTitle, Captures.Count);

    public string CapturedHint => UiStrings.SummaryCapturedHint;

    public string DismissAllLabel => UiStrings.SummaryDismissAll;

    public string AnotherSessionLabel => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.SummaryAnotherSession,
        _summary.PlannedMinutes);

    public string TakeBreakLabel => UiStrings.SummaryTakeBreak;

    public int PlannedMinutes => _summary.PlannedMinutes;

    public ICommand DismissAllCommand { get; }

    public ICommand AnotherSessionCommand { get; }

    public ICommand TakeBreakCommand { get; }

    private void ResolveCapture(CapturedItemViewModel capture, bool opened)
    {
        Captures.Remove(capture);
        RaiseCaptureListChanged();
        _ = SendResolutionAsync(
            [capture.Id],
            opened ? CaptureProtocol.OpenedResolution : CaptureProtocol.DismissedResolution);
    }

    private void DismissAll()
    {
        // Very often the honest answer is that none of them mattered, and that itself is
        // useful information: dismissals are stored, not deleted.
        var ids = Captures.Select(capture => capture.Id).ToArray();
        Captures.Clear();
        RaiseCaptureListChanged();
        _ = SendResolutionAsync(ids, CaptureProtocol.DismissedResolution);
    }

    private async Task SendResolutionAsync(IReadOnlyList<Guid> ids, string resolution)
    {
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            await _client.ResolveCapturesAsync(
                new ResolveCapturesRequest(ids, resolution),
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            // The row is already gone from the list. Failing to persist that is not worth
            // putting it back and confusing the user.
        }
    }

    private void RaiseCaptureListChanged()
    {
        OnPropertyChanged(nameof(HasCaptures));
        OnPropertyChanged(nameof(CapturedTitle));
        ((NotifyingDelegateCommand)DismissAllCommand).RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
