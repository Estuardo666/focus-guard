using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace FocusGuard.Desktop;

/// <summary>
/// The window that closes the loop on a session. Small and centred rather than
/// full-screen: there is nothing left to enforce, and the user may already have moved on.
/// </summary>
public sealed partial class SessionSummaryWindow : Window
{
    private SessionSummaryViewModel? _viewModel;

    public SessionSummaryWindow()
        : this(new SessionSummaryViewModel(EmptySummary()))
    {
    }

    public SessionSummaryWindow(SessionSummaryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.RequestedClose += RequestedClose;
        viewModel.RequestedAnotherSession += RequestedAnotherSession;
    }

    /// <summary>
    /// Raised when the user chose to chain straight into another session of the same
    /// length. The shell owns activation, so it decides what that actually starts.
    /// </summary>
    public event EventHandler<int>? AnotherSessionRequested;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_viewModel?.ReduceMotion == true) { Card.Transitions = null; CheckBadge.Transitions = null; }
        Card.Opacity = 1;
        Card.RenderTransform = TransformOperations.Parse("scale(1)");

        // The check settling into place is the one expressive animation in the app, and
        // it is justified here: this is the only screen that exists to say "well done".
        if (CheckBadge.IsVisible)
        {
            Dispatcher.UIThread.Post(
                () => CheckBadge.RenderTransform = TransformOperations.Parse("scale(1)"),
                DispatcherPriority.Background);
        }

        AnotherSessionButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SaveReflection();
            _viewModel.RequestedClose -= RequestedClose;
            _viewModel.RequestedAnotherSession -= RequestedAnotherSession;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    private void RequestedClose(object? sender, EventArgs e) => Close();

    private void RequestedAnotherSession(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            AnotherSessionRequested?.Invoke(this, _viewModel.PlannedMinutes);
        }

        Close();
    }

    private static FocusGuard.Contracts.SessionSummaryResponse EmptySummary() =>
        new(
            Available: false,
            SessionId: null,
            Outcome: FocusGuard.Contracts.CaptureProtocol.CompletedOutcome,
            ProfileName: "Focus",
            PlannedMinutes: 50,
            FocusedMinutes: 50,
            BlockedAttempts: 0,
            StreakDays: 0,
            EndedAtUtc: null,
            Captures: []);
}
