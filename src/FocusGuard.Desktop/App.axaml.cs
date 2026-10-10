using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using FocusGuard.Client.Preferences;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

public sealed partial class App : Application
{
    private readonly ReactPilotLauncher _reactPilotLauncher = new(AppContext.BaseDirectory, new WindowsReactPilotProcessOperations());
    private TrayFlyoutWindow? _trayFlyout;
    private CancellationTokenSource? _processAgentSupervisorCancellation;
    private Task? _processAgentSupervisorTask;

    internal static IDesktopInstance? SingleInstance { get; set; }

    internal static bool StartMinimized { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Swaps the palette. Every colour token is keyed by theme variant, so flipping this
    /// one property repaints the shell and any open window without a restart.
    /// </summary>
    internal static void ApplyTheme(AppTheme theme)
    {
        if (Current is not null)
        {
            Current.RequestedThemeVariant =
                theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Read before the first window exists so the app never flashes the wrong palette.
        ApplyTheme(new DesktopSettingsStore().Load().Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow(StartMinimized);
            SingleInstance?.StartListening(() =>
                Dispatcher.UIThread.Post(ShowMainWindow));
            StartProcessAgentSupervisor(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        if (_trayFlyout is { IsVisible: true })
        {
            _trayFlyout.Close();
            return;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow { DataContext: FocusViewModel viewModel })
        {
            return;
        }

        _trayFlyout = new TrayFlyoutWindow(viewModel);
        _trayFlyout.OpenRequested += (_, _) =>
        {
            _trayFlyout?.Close();
            ShowMainWindow();
        };
        _trayFlyout.SettingsRequested += (_, _) =>
        {
            _trayFlyout?.Close();
            ShowSettings();
        };
        _trayFlyout.ExitRequested += ExitFocusGuardClick;
        _trayFlyout.CompactRequested += (_, _) =>
        {
            _trayFlyout?.Close();
            if (desktop.MainWindow is MainWindow main) main.OpenCompactWindow();
        };
        _trayFlyout.ReactPilotRequested += ReactPilotRequested;
        _trayFlyout.Closed += (_, _) => _trayFlyout = null;
        _trayFlyout.Show();
    }

    private void OpenFocusGuardClick(object? sender, EventArgs e) => ShowMainWindow();

    private void ReactPilotClick(object? sender, EventArgs e)
    {
        _trayFlyout?.Close();
        OpenReactPilot();
    }

    private void ReactPilotRequested(object? sender, EventArgs e)
    {
        _trayFlyout?.Close();
        OpenReactPilot();
    }

    private void OpenReactPilot()
    {
        switch (_reactPilotLauncher.Open())
        {
            case ReactPilotLaunchResult.NotInstalled:
                new SessionToastWindow(UiStrings.ReactPilotUnavailableTitle,
                    UiStrings.ReactPilotUnavailableBody, "\uE8A7").Show();
                break;
            case ReactPilotLaunchResult.Failed:
                new SessionToastWindow(UiStrings.ReactPilotStartFailedTitle,
                    UiStrings.ReactPilotStartFailedBody, "\uE7BA").Show();
                break;
        }
    }

    private void ExitFocusGuardClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            SingleInstance?.StopListening();
            desktop.Shutdown();
        }
    }

    private void StartProcessAgentSupervisor(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _processAgentSupervisorCancellation = new CancellationTokenSource();
        var cancellation = _processAgentSupervisorCancellation;
        _processAgentSupervisorTask = new ProcessAgentSupervisor(AppContext.BaseDirectory)
            .RunAsync(cancellation.Token);
        desktop.Exit += (_, _) => StopProcessAgentSupervisor();
    }

    private void StopProcessAgentSupervisor()
    {
        var cancellation = Interlocked.Exchange(
            ref _processAgentSupervisorCancellation,
            null);
        cancellation?.Cancel();
        if (cancellation is not null)
        {
            _ = (_processAgentSupervisorTask ?? Task.CompletedTask)
                .ContinueWith(
                    _ => cancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        _processAgentSupervisorTask = null;
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not MainWindow window)
        {
            return;
        }

        window.RestoreFromNotificationArea();
    }

    private void ShowSettings()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: MainWindow window
            })
        {
            window.RestoreFromNotificationArea();
            window.OpenSettings();
        }
    }
}
