using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using FocusGuard.Contracts;

namespace FocusGuard.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _closing = new();
    private readonly List<BlockScreenWindow> _blockScreens = [];
    private CancellationTokenSource? _strictTransition;
    private IInputElement? _focusBeforeStrictReview;
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new FocusViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        viewModel.SessionEnded += SessionEnded;
        viewModel.ApplicationBlocked += ApplicationBlocked;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += RefreshTimerTick;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await RefreshAsync();
        _refreshTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModelPropertyChanged;
            viewModel.SessionEnded -= SessionEnded;
            viewModel.ApplicationBlocked -= ApplicationBlocked;
        }

        _strictTransition?.Cancel();
        _strictTransition?.Dispose();
        _closing.Cancel();
        _closing.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (NotificationAreaClosePolicy.ShouldHide(e.CloseReason))
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    internal void RestoreFromNotificationArea()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void RefreshTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    /// <summary>
    /// A blocked application gets a full-screen block screen; a blocked site does not,
    /// because the extension already answered it inside the tab the user was in.
    /// </summary>
    private void ApplicationBlocked(object? sender, ApplicationBlockedEventArgs e)
    {
        if (_blockScreens.Count > 0 || DataContext is not FocusViewModel viewModel
            || viewModel.SessionStartUtc is not { } start
            || viewModel.SessionEndUtc is not { } end)
        {
            return;
        }

        var screens = BlockScreenWindow.ShowForAllScreens(
            new BlockScreenViewModel(e.Target, start, end, viewModel.IsStrictSessionActive),
            this);
        _blockScreens.AddRange(screens);
        foreach (var screen in screens)
        {
            screen.Closed += (_, _) => _blockScreens.Clear();
        }
    }

    private async void SessionEnded(object? sender, SessionEndedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel
            || viewModel.SessionEndPresentation == SessionEndPresentation.None)
        {
            return;
        }

        // Any block screen still standing belongs to a session that no longer exists.
        foreach (var screen in _blockScreens.ToArray())
        {
            screen.Close();
        }

        SessionSummaryResponse summary;
        try
        {
            summary = await viewModel.GetSessionSummaryAsync(e.SessionId, _closing.Token);
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            return;
        }

        if (!summary.Available)
        {
            return;
        }

        if (viewModel.SessionEndPresentation == SessionEndPresentation.Notification)
        {
            var toast = new SessionToastWindow(summary.FocusedMinutes);
            toast.SummaryRequested += (_, _) => ShowSummaryWindow(summary, dimmed: false);
            toast.Show();
            return;
        }

        ShowSummaryWindow(
            summary,
            viewModel.SessionEndPresentation == SessionEndPresentation.DimmedWindow);
    }

    private void ShowSummaryWindow(SessionSummaryResponse summary, bool dimmed)
    {
        var window = new SessionSummaryWindow(new SessionSummaryViewModel(summary));
        window.AnotherSessionRequested += async (_, minutes) =>
        {
            RestoreFromNotificationArea();
            if (DataContext is FocusViewModel viewModel)
            {
                await viewModel.StartAnotherSessionAsync(minutes);
            }
        };

        if (!dimmed)
        {
            window.Show();
            return;
        }

        // The dim is a plain full-screen scrim behind the card, closed with it.
        var scrim = new BlockScrimWindow();
        window.Closed += (_, _) => scrim.Close();
        scrim.Show();
        window.Show();
        window.Activate();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.IsStrictReviewVisible)
            && sender is FocusViewModel viewModel)
        {
            _ = SetStrictReviewVisibilityAsync(viewModel.IsStrictReviewVisible);
        }
    }

    private async Task SetStrictReviewVisibilityAsync(bool isVisible)
    {
        _strictTransition?.Cancel();
        _strictTransition?.Dispose();
        _strictTransition = new CancellationTokenSource();
        var cancellationToken = _strictTransition.Token;

        var overlay = this.FindControl<Border>("StrictReviewOverlay");
        var dialog = this.FindControl<Border>("StrictReviewDialog");
        var shell = this.FindControl<Grid>("ApplicationShell");
        var blur = shell?.Effect as BlurEffect;
        if (overlay is null || dialog is null || shell is null || blur is null)
        {
            return;
        }

        try
        {
            if (isVisible)
            {
                _focusBeforeStrictReview = FocusManager?.GetFocusedElement();
                overlay.IsVisible = true;
                overlay.IsHitTestVisible = true;
                shell.IsHitTestVisible = false;
                overlay.Opacity = 0;
                dialog.Opacity = 0;
                dialog.RenderTransform = TransformOperations.Parse("scale(0.97)");
                blur.Radius = 0;

                // Let the zero state render once so Avalonia can interpolate the
                // entrance instead of materializing the modal at its final state.
                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Opacity = 1;
                    dialog.Opacity = 1;
                    dialog.RenderTransform = TransformOperations.Parse("scale(1)");
                    blur.Radius = 7;
                    this.FindControl<TextBox>("StrictConfirmationInput")?.Focus();
                });
                return;
            }

            overlay.IsHitTestVisible = false;
            overlay.Opacity = 0;
            dialog.Opacity = 0;
            dialog.RenderTransform = TransformOperations.Parse("scale(0.97)");
            blur.Radius = 0;

            await Task.Delay(TimeSpan.FromMilliseconds(280), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                overlay.IsVisible = false;
                shell.IsHitTestVisible = true;
                _focusBeforeStrictReview?.Focus();
                _focusBeforeStrictReview = null;
            });
        }
        catch (OperationCanceledException)
        {
            // A rapid open/close reverses the same transition; the latest request
            // owns the final visual and focus state.
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || DataContext is not FocusViewModel viewModel)
        {
            return;
        }

        _refreshing = true;
        try
        {
            await viewModel.RefreshAsync(_closing.Token);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ProfilesNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel)
        {
            return;
        }

        viewModel.ShowSection(NavigationSection.Profiles);
        if (viewModel.IsProfilesVisible)
        {
            this.FindControl<ComboBox>("ProfileSelector")?.Focus();
        }
    }

    private async void NewProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel)
        {
            return;
        }

        viewModel.ShowSection(NavigationSection.Profiles);
        await viewModel.BeginCreateProfileAsync();
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NewProfileNameInput")?.Focus());
    }

    private void FocusNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowFocus();
        }
    }

    private void SettingsNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowSection(NavigationSection.Settings);
        }
    }

    private void ProfileSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel
            && sender is Button { DataContext: ProfileSwatchViewModel swatch })
        {
            viewModel.SelectProfile(swatch.Profile);
        }
    }

    private void ExpandWebsiteRulesClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ExpandWebsiteRules();

    private void ExpandApplicationRulesClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ExpandApplicationRules();

    // The window is drawn without Windows chrome, so dragging and the caption
    // buttons are wired up by hand.
    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void StrictReviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is FocusViewModel { CanCancelStrict: true } viewModel)
        {
            viewModel.CancelStrictCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void CopyRecoveryCodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.RecoveryCodeDisplay)
            || Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(viewModel.RecoveryCodeDisplay);
    }

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private async void ActivityNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.ShowActivityAsync(_closing.Token);
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
        {
            // Existing service health copy remains authoritative; Activity simply
            // stays on the current screen if its read-only query is unavailable.
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

internal static class NotificationAreaClosePolicy
{
    internal static bool ShouldHide(WindowCloseReason reason) =>
        reason is WindowCloseReason.Undefined or WindowCloseReason.WindowClosing;
}
