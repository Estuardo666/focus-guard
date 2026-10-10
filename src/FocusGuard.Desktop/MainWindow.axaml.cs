using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan ForegroundRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _closing = new();
    private readonly List<BlockScreenWindow> _blockScreens = [];
    private SessionToastWindow? _cyclePhaseToast;
    private bool _startHidden;
    private CancellationTokenSource? _strictTransition;
    private CancellationTokenSource? _settingsTransition;
    private CancellationTokenSource? _profileCreationTransition;
    private CancellationTokenSource? _cyclePresetTransition;
    private IInputElement? _focusBeforeStrictReview;
    private IInputElement? _focusBeforeSettings;
    private IInputElement? _focusBeforeProfileCreation;
    private IInputElement? _focusBeforeCyclePreset;
    private bool _refreshing;
    private CompactFocusWindow? _compactWindow;
    private readonly bool _closeToTray;

    public MainWindow()
        : this(startHidden: false)
    {
    }

    internal MainWindow(bool startHidden)
        : this(startHidden, null)
    {
    }

    public MainWindow(FocusViewModel viewModel) : this(false, viewModel) { }

    private MainWindow(bool startHidden, FocusViewModel? suppliedViewModel)
    {
        InitializeComponent();
        _closeToTray = suppliedViewModel is null;
        ScalingChanged += (_, _) => { if (_closeToTray) FitCurrentMonitor(); };
        PositionChanged += (_, _) => { if (_closeToTray) FitCurrentMonitor(); };
        _startHidden = startHidden;
        var executablePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "FocusGuard.Desktop.exe");
        var viewModel = suppliedViewModel ?? new FocusViewModel(
            startupRegistration: new WindowsStartupRegistration(executablePath));
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        viewModel.SessionEnded += SessionEnded;
        viewModel.ApplicationBlocked += ApplicationBlocked;
        viewModel.CyclePhaseChanged += CyclePhaseChanged;
        _refreshTimer = new DispatcherTimer { Interval = ForegroundRefreshInterval };
        _refreshTimer.Tick += RefreshTimerTick;
    }

    protected override async void OnOpened(EventArgs e)
    {
        if (_closeToTray) FitCurrentMonitor();
        base.OnOpened(e);
        if (_startHidden)
        {
            _startHidden = false;
            Hide();
            SetTimerMotionEnabled(false);
            UpdateRefreshInterval();
        }
        else
        {
            SetTimerMotionEnabled(true);
        }

        await RefreshAsync();
        _refreshTimer.Start();
        if (DataContext is FocusViewModel overview)
        {
            await overview.LoadSchedulesAsync();
            await overview.LoadTodayAsync();
        }
    }

    private Size? _lastMonitorSize;
    private void FitCurrentMonitor()
    {
        if (Screens.ScreenFromWindow(this) is { } screen)
            ApplyWorkspaceArea(screen.WorkingArea, screen.Scaling);
    }

    public void ApplyWorkspaceArea(PixelRect area, double scaling)
    {
        var size = DesktopWindowLayout.ClientSize(area, scaling);
        if (_lastMonitorSize == size) return;
        _lastMonitorSize = size;
        MinWidth = 0; MinHeight = 0; MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
        Width = size.Width; Height = size.Height;
        MinWidth = MaxWidth = size.Width;
        MinHeight = MaxHeight = size.Height;
        Position = new PixelPoint(
            area.X + Math.Max(0, (int)((area.Width - (size.Width + 16) * scaling) / 2)),
            area.Y + Math.Max(0, (int)((area.Height - (size.Height + 32) * scaling) / 2)));
    }

    private void NewSessionClick(object? sender, RoutedEventArgs e) => (DataContext as FocusViewModel)?.OpenSessionSetup();

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModelPropertyChanged;
            viewModel.SessionEnded -= SessionEnded;
            viewModel.ApplicationBlocked -= ApplicationBlocked;
            viewModel.CyclePhaseChanged -= CyclePhaseChanged;
        }

        _cyclePhaseToast?.Close();
        _compactWindow?.Close();
        _compactWindow = null;
        _cyclePhaseToast = null;

        _strictTransition?.Cancel();
        _strictTransition?.Dispose();
        _settingsTransition?.Cancel();
        _settingsTransition?.Dispose();
        _profileCreationTransition?.Cancel();
        _profileCreationTransition?.Dispose();
        _cyclePresetTransition?.Cancel();
        _cyclePresetTransition?.Dispose();
        _closing.Cancel();
        _closing.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeToTray && NotificationAreaClosePolicy.ShouldHide(e.CloseReason))
        {
            e.Cancel = true;
            Hide();
            SetTimerMotionEnabled(false);
            UpdateRefreshInterval();
        }

        base.OnClosing(e);
    }

    internal void RestoreFromNotificationArea()
    {
        Show();
        SetTimerMotionEnabled(true);
        _refreshTimer.Interval = ForegroundRefreshInterval;
        _ = RefreshAsync();
        WindowState = WindowState.Normal;
        Activate();
    }

    internal void OpenSettings()
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowSection(NavigationSection.Settings);
        }
    }

    internal void OpenCompactWindow()
    {
        if (_compactWindow is null)
        {
            var window = new CompactFocusWindow { DataContext = DataContext };
            window.OpenRequested += (_, _) => RestoreFromNotificationArea();
            window.Closed += (_, _) => _compactWindow = null;
            _compactWindow = window;
            window.Show();
        }
        else { _compactWindow.Show(); _compactWindow.Activate(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not FocusViewModel vm || vm.IsSettingsVisible || vm.IsStrictReviewVisible || vm.IsCreatingProfile || vm.IsCyclePresetEditorVisible) return;
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.M)
        { OpenCompactWindow(); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter && vm.IsFocusVisible && vm.StartFocusCommand.CanExecute(null))
        { vm.StartFocusCommand.Execute(null); e.Handled = true; }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F && !vm.IsNavigationLocked && !vm.IsSessionSetupOpen)
        { vm.ShowSection(NavigationSection.Profiles); this.FindControl<RuleEditorView>("ProfileRules")?.FocusSearch(); e.Handled = true; }
    }

    private async void ScheduleNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel vm && !vm.IsNavigationLocked)
        { vm.ShowSection(NavigationSection.Schedule); await vm.LoadSchedulesAsync(_closing.Token); }
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

    private void CyclePhaseChanged(object? sender, CyclePhaseTransitionEventArgs e)
    {
        if (DataContext is not FocusViewModel { AreCyclePhaseNotificationsEnabled: true })
        {
            return;
        }

        var presentation = CyclePhaseNotificationCopy.Create(e.Transition);
        _cyclePhaseToast?.Close();
        var toast = new SessionToastWindow(
            presentation.Title,
            presentation.Body,
            presentation.IconGlyph);
        _cyclePhaseToast = toast;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_cyclePhaseToast, toast))
            {
                _cyclePhaseToast = null;
            }
        };
        toast.Show();
    }

    private async void SessionEnded(object? sender, SessionEndedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel)
        {
            return;
        }
        await viewModel.LoadTodayAsync(_closing.Token);
        await viewModel.LoadSchedulesAsync(_closing.Token);
        if (viewModel.SessionEndPresentation == SessionEndPresentation.None) return;

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
        var focus = DataContext as FocusViewModel;
        var intention = focus?.GetIntention(summary.SessionId);
        var summaryModel = new SessionSummaryViewModel(summary, objective: intention?.Objective ?? "", reflection: intention?.Reflection ?? "", saveReflection: value => focus?.SaveReflection(summary.SessionId, value), reduceMotion: focus?.IsReduceMotionEnabled ?? false);
        var window = new SessionSummaryWindow(summaryModel);
        window.AnotherSessionRequested += async (_, minutes) =>
        {
            RestoreFromNotificationArea();
            if (DataContext is FocusViewModel viewModel)
            {
                await viewModel.RepeatSessionAsync(summary.SessionId, minutes);
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

        if (e.PropertyName == nameof(FocusViewModel.IsSettingsVisible)
            && sender is FocusViewModel settingsViewModel)
        {
            _ = SetSettingsVisibilityAsync(settingsViewModel.IsSettingsVisible);
        }

        if (e.PropertyName == nameof(FocusViewModel.IsReduceMotionEnabled)
            && sender is FocusViewModel motionViewModel)
        {
            SetTimerMotionEnabled(IsVisible && !motionViewModel.IsReduceMotionEnabled);
        }

        if (e.PropertyName == nameof(FocusViewModel.IsCreatingProfile)
            && sender is FocusViewModel profileViewModel)
        {
            _ = SetProfileCreationVisibilityAsync(profileViewModel.IsCreatingProfile);
        }

        if (e.PropertyName == nameof(FocusViewModel.IsCyclePresetEditorVisible)
            && sender is FocusViewModel cycleViewModel)
        {
            _ = SetCyclePresetEditorVisibilityAsync(cycleViewModel.IsCyclePresetEditorVisible);
        }
    }

    private async Task SetSettingsVisibilityAsync(bool isVisible)
    {
        _settingsTransition?.Cancel();
        _settingsTransition?.Dispose();
        _settingsTransition = new CancellationTokenSource();
        var cancellationToken = _settingsTransition.Token;

        var overlay = this.FindControl<Border>("SettingsOverlay");
        var dialog = this.FindControl<Border>("SettingsDialog");
        var shell = this.FindControl<Grid>("ApplicationShell");
        var blur = shell?.Effect as BlurEffect;
        if (overlay is null || dialog is null || shell is null || blur is null)
        {
            return;
        }

        try
        {
            var reduceMotion = DataContext is FocusViewModel { IsReduceMotionEnabled: true };
            var exitDelay = reduceMotion
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromMilliseconds(220);
            if (isVisible)
            {
                _focusBeforeSettings = FocusManager?.GetFocusedElement();
                overlay.IsVisible = true;
                overlay.IsHitTestVisible = true;
                shell.IsHitTestVisible = false;
                overlay.Opacity = 0;
                dialog.Opacity = 0;
                dialog.RenderTransform = TransformOperations.Parse(
                    reduceMotion ? "translateY(0px) scale(1)" : "translateY(8px) scale(0.985)");
                blur.Radius = 0;

                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Opacity = 1;
                    dialog.Opacity = 1;
                    dialog.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
                    blur.Radius = 6;
                    this.FindControl<TabControl>("SettingsTabs")?.Focus();
                });
                return;
            }

            overlay.IsHitTestVisible = false;
            overlay.Opacity = 0;
            dialog.Opacity = 0;
            dialog.RenderTransform = TransformOperations.Parse(
                reduceMotion ? "translateY(0px) scale(1)" : "translateY(4px) scale(0.99)");
            blur.Radius = 0;

            await Task.Delay(exitDelay, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                overlay.IsVisible = false;
                shell.IsHitTestVisible = true;
                _focusBeforeSettings?.Focus();
                _focusBeforeSettings = null;
            });
        }
        catch (OperationCanceledException)
        {
            // The newest visibility request owns focus and the final visual state.
        }
    }

    // Profile creation enters and leaves exactly like the strict review modal:
    // scrim fades, the dialog scales in, and the shell blurs behind it.
    private async Task SetProfileCreationVisibilityAsync(bool isVisible)
    {
        _profileCreationTransition?.Cancel();
        _profileCreationTransition?.Dispose();
        _profileCreationTransition = new CancellationTokenSource();
        var cancellationToken = _profileCreationTransition.Token;

        var overlay = this.FindControl<Border>("NewProfileOverlay");
        var dialog = this.FindControl<Border>("NewProfileDialog");
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
                _focusBeforeProfileCreation = FocusManager?.GetFocusedElement();
                overlay.IsVisible = true;
                overlay.IsHitTestVisible = true;
                shell.IsHitTestVisible = false;
                overlay.Opacity = 0;
                dialog.Opacity = 0;
                dialog.RenderTransform = TransformOperations.Parse("scale(0.97)");
                blur.Radius = 0;

                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Opacity = 1;
                    dialog.Opacity = 1;
                    dialog.RenderTransform = TransformOperations.Parse("scale(1)");
                    blur.Radius = 7;
                    this.FindControl<TextBox>("NewProfileNameInput")?.Focus();
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
                _focusBeforeProfileCreation?.Focus();
                _focusBeforeProfileCreation = null;
            });
        }
        catch (OperationCanceledException)
        {
            // A rapid open/close reverses the same transition; the latest request
            // owns the final visual and focus state.
        }
    }

    // The cycle editor is the same modal gesture as profile creation, down to the blur
    // radius, so adding a program and adding a profile feel like one kind of action.
    private async Task SetCyclePresetEditorVisibilityAsync(bool isVisible)
    {
        _cyclePresetTransition?.Cancel();
        _cyclePresetTransition?.Dispose();
        _cyclePresetTransition = new CancellationTokenSource();
        var cancellationToken = _cyclePresetTransition.Token;

        var overlay = this.FindControl<Border>("CyclePresetOverlay");
        var dialog = this.FindControl<Border>("CyclePresetDialog");
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
                _focusBeforeCyclePreset = FocusManager?.GetFocusedElement();
                overlay.IsVisible = true;
                overlay.IsHitTestVisible = true;
                shell.IsHitTestVisible = false;
                overlay.Opacity = 0;
                dialog.Opacity = 0;
                dialog.RenderTransform = TransformOperations.Parse("scale(0.97)");
                blur.Radius = 0;

                await Task.Delay(TimeSpan.FromMilliseconds(16), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Opacity = 1;
                    dialog.Opacity = 1;
                    dialog.RenderTransform = TransformOperations.Parse("scale(1)");
                    blur.Radius = 7;
                    this.FindControl<TextBox>("CyclePresetNameField")?.Focus();
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
                _focusBeforeCyclePreset?.Focus();
                _focusBeforeCyclePreset = null;
            });
        }
        catch (OperationCanceledException)
        {
            // The newest visibility request owns focus and the final visual state.
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
            UpdateRefreshInterval();
        }
    }

    private void UpdateRefreshInterval()
    {
        var isActive = DataContext is FocusViewModel { IsActive: true };
        _refreshTimer.Interval = DesktopRefreshPolicy.GetInterval(IsVisible, isActive);
    }

    private void SetTimerMotionEnabled(bool enabled)
    {
        if (DataContext is FocusViewModel { IsReduceMotionEnabled: true })
        {
            enabled = false;
        }

        SetPulsingClass(this.FindControl<Border>("TimerGlow"), enabled);
        SetPulsingClass(this.FindControl<Border>("TimerGlowOuter"), enabled);
    }

    private static void SetPulsingClass(Border? glow, bool enabled)
    {
        if (glow is null)
        {
            return;
        }

        if (enabled)
        {
            if (!glow.Classes.Contains("pulsing"))
            {
                glow.Classes.Add("pulsing");
            }
        }
        else
        {
            glow.Classes.Remove("pulsing");
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

    private async void FocusNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowFocus();
            await viewModel.LoadSchedulesAsync(_closing.Token);
            await viewModel.LoadTodayAsync(_closing.Token);
        }
    }

    private void SettingsNavigationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowSection(NavigationSection.Settings);
        }
    }

    private void SettingsCloseClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.CloseSettings();

    private void SettingsOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source))
        {
            (DataContext as FocusViewModel)?.CloseSettings();
            e.Handled = true;
        }
    }

    private void SettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is FocusViewModel viewModel)
        {
            viewModel.CloseSettings();
            e.Handled = true;
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

    private void ProfileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not FocusViewModel vm || vm.IsNavigationLocked || sender is not Button { DataContext: ProfileSwatchViewModel swatch } button) return;
        vm.SelectProfile(swatch.Profile);
        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem { Header = UiStrings.ProfilesNavigation, Command = new NotifyingDelegateCommand(() => vm.ShowSection(NavigationSection.Profiles), () => !vm.IsNavigationLocked) },
                new MenuItem { Header = UiStrings.DuplicateProfile, Command = vm.DuplicateProfileCommand },
                new MenuItem { Header = UiStrings.DeleteProfile, Command = vm.DeleteProfileCommand }
            }
        };
        menu.Open(button);
        e.Handled = true;
    }

    private void ExpandWebsiteRulesClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ExpandWebsiteRules();

    private void ExpandApplicationRulesClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ExpandApplicationRules();

    private void StrictReviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is FocusViewModel { CanCancelStrict: true } viewModel)
        {
            viewModel.CancelStrictCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewProfileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is FocusViewModel { IsCreatingProfile: true } viewModel
            && viewModel.CancelCreateProfileCommand.CanExecute(null))
        {
            viewModel.CancelCreateProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CyclePresetKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is FocusViewModel { IsCyclePresetEditorVisible: true } viewModel
            && viewModel.CancelCyclePresetEditCommand.CanExecute(null))
        {
            viewModel.CancelCyclePresetEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AdvanceStrictStepClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel)
        {
            return;
        }

        viewModel.AdvanceStrictStep();
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("StrictConfirmationInput")?.Focus());
    }

    private void ReturnToStrictStepOneClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ReturnToStrictStepOne();

    private void ShowStrictLimitsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.IsStrictLimitsVisible = true;
        }
    }

    private void HideStrictLimitsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.IsStrictLimitsVisible = false;
        }
    }

    /// <summary>
    /// Writes the code to a file the user picks. A recovery code that only exists in
    /// the clipboard is one copy away from being lost, which is the whole failure this
    /// step is trying to prevent.
    /// </summary>
    private async void DownloadRecoveryCodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.RecoveryCodeDisplay))
        {
            return;
        }

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = UiStrings.StrictDownloadCode,
                SuggestedFileName = "focusguard-recovery-code.txt",
                DefaultExtension = "txt",
            });

            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.RecoveryCodeDisplay);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saving is a convenience: the code is still on screen, and the step cannot
            // be passed by a failed write either way.
        }
    }

    /// <summary>
    /// Windows has no print dialog reachable from Avalonia, so this writes the code to
    /// a temporary file and hands it to the shell, which opens the user's own printable
    /// viewer. Claiming a Print button that silently did nothing would be worse.
    /// </summary>
    private void PrintRecoveryCodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel viewModel
            || string.IsNullOrWhiteSpace(viewModel.RecoveryCodeDisplay))
        {
            return;
        }

        try
        {
            var path = Path.Combine(Path.GetTempPath(), "focusguard-recovery-code.txt");
            File.WriteAllText(path, viewModel.RecoveryCodeDisplay);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            // No printer, or no association for the verb. The code stays on screen.
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

internal static class DesktopRefreshPolicy
{
    private static readonly TimeSpan ForegroundOrActiveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HiddenInactiveInterval = TimeSpan.FromSeconds(5);

    internal static TimeSpan GetInterval(bool isVisible, bool isActive) =>
        isVisible || isActive ? ForegroundOrActiveInterval : HiddenInactiveInterval;
}
