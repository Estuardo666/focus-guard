using FocusGuard.Client;
using FocusGuard.Client.Platform;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using Microsoft.Web.WebView2.Core;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>
/// The whole desktop presence in one place: the main window, the notification-area icon and its panel, the notices,
/// the session summary and the block screen. It replaces the Avalonia shell. It only reads the service and asks the
/// page to show things; closing or quitting it never stops a session.
/// </summary>
internal sealed class HostShell : ApplicationContext, IHostShellController
{
    private readonly MainForm _main;
    private readonly IFocusControlClient _client;
    private readonly IHostControlAuthorization _authorization;
    private readonly DesktopSettingsStore? _settings;
    private readonly Func<DocumentLifetime, IHostShellController, NativeBridgeDispatcher> _surfaceBridge;
    private readonly SessionEventMonitor _monitor;
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;
    private readonly List<SurfaceForm> _surfaces = [];
    private readonly List<Form> _scrims = [];
    private Task<CoreWebView2Environment>? _environment;
    private bool _trayPanelOpening;
    private SurfaceForm? _trayPanel;
    private SurfaceForm? _notice;
    private DateTime _trayPanelClosedAt;
    private bool _quitting;

    internal HostShell(MainForm main, IFocusControlClient client, IHostControlAuthorization authorization, DesktopSettingsStore? settings,
        Func<DocumentLifetime, IHostShellController, NativeBridgeDispatcher> surfaceBridge, bool startMinimized, IDesktopInstance? instance)
    {
        _main = main;
        _client = client;
        _authorization = authorization;
        _settings = settings;
        _surfaceBridge = surfaceBridge;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _main.CloseToTray = true;
        _main.ShellController = this;
        MainForm = null;

        _tray = new NotifyIcon
        {
            Text = HostWindowStrings.Get("WindowTitle"),
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleTrayPanel(); };

        _monitor = new SessionEventMonitor(client);
        _monitor.SessionEnded += e => Post(() => OnSessionEnded(e));
        _monitor.PhaseChanged += e => Post(() => OnPhaseChanged(e));
        _monitor.ApplicationBlocked += e => Post(() => OnApplicationBlocked(e));
        _monitor.StatusChanged += status => Post(() => UpdateTrayText(status));
        _monitor.Start();

        instance?.StartListening(() => Post(ShowMain));
        if (!startMinimized) _main.Show();
        else _ = _main.Handle; // Create the window (and its page) hidden, so opening it later is instant.
    }

    private ContextMenuStrip BuildMenu()
    {
        // The keyboard and screen-reader path to the same actions as the panel.
        var menu = new ContextMenuStrip();
        menu.Items.Add(HostWindowStrings.Get("TrayOpen"), null, (_, _) => ShowMain());
        menu.Items.Add(HostWindowStrings.Get("TrayCompact"), null, (_, _) => OpenCompact());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(HostWindowStrings.Get("TrayQuit"), null, (_, _) => Quit());
        return menu;
    }

    private void Post(Action action) => _ui.Post(_ => { if (!_quitting) Guard("event", action); }, null);

    /// <summary>
    /// A popup that fails must never leave a modal error dialog blocking the tray and every later launch (installed .141
    /// did). The failure is recorded for diagnosis and the shell keeps running; the session lives in the service anyway.
    /// </summary>
    private static void Guard(string where, Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { RecordFailure(where, exception); }
    }

    private static void RecordFailure(string where, Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "diagnostics");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "host-shell-errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {where} {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}");
        }
        catch (Exception logging) when (logging is IOException or UnauthorizedAccessException)
        {
            _ = logging;
        }
    }

    private bool MainIsOnScreen => _main.Visible && _main.WindowState != FormWindowState.Minimized;

    private DesktopSettings Settings => _settings?.Load() ?? DesktopSettings.Default;

    private bool Dark => Settings.Theme != AppTheme.Light;

    // ── Main window ───────────────────────────────────────────────────────────────────────────────
    private void ShowMain()
    {
        CloseTrayPanel();
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
    }

    private void OpenCompact()
    {
        ShowMain();
        _ = _main.SetCompactModeAsync(true, CancellationToken.None);
    }

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _monitor.Dispose();
        foreach (var surface in _surfaces.ToArray()) { surface.AllowUserClose = true; surface.Close(); }
        foreach (var scrim in _scrims.ToArray()) scrim.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _main.CloseToTray = false;
        _main.Close();
        ExitThread();
    }

    public ValueTask<bool> ExecuteAsync(HostAppCommand command, CancellationToken cancellationToken) =>
        ExecuteFor(null, command);

    private ValueTask<bool> ExecuteFor(SurfaceForm? sender, HostAppCommand command)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try
            {
                switch (command.Action)
                {
                    case HostAppAction.Open:
                        if (sender is not null) CloseSurface(sender);
                        ShowMain();
                        _main.Navigate(command.Section ?? "summary");
                        break;
                    case HostAppAction.Compact:
                        if (sender is not null) CloseSurface(sender);
                        OpenCompact();
                        break;
                    case HostAppAction.Quit:
                        Quit();
                        break;
                    case HostAppAction.CloseSurface:
                        if (sender is null) { done.SetResult(false); return; }
                        CloseSurface(sender);
                        break;
                    case HostAppAction.SurfaceReady:
                        if (sender is null) { done.SetResult(false); return; }
                        sender.Reveal();
                        break;
                }
                done.SetResult(true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                done.SetResult(false);
            }
        }, null);
        return new ValueTask<bool>(done.Task);
    }

    // ── Tray panel ───────────────────────────────────────────────────────────────────────────────
    private async void ToggleTrayPanel()
    {
        try { await ToggleTrayPanelAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _trayPanelOpening = false; RecordFailure("tray", exception); }
    }

    private async Task ToggleTrayPanelAsync()
    {
        if (_trayPanel is not null) { CloseTrayPanel(); return; }
        if (_trayPanelOpening) return;
        // A click on the icon that also dismissed the panel (deactivation) should not reopen it straight away.
        if ((DateTime.UtcNow - _trayPanelClosedAt).TotalMilliseconds < 250) return;
        var screen = Screen.FromPoint(Cursor.Position);
        var area = screen.WorkingArea;
        var size = Scale(new Size(360, 480));
        // Open next to the taskbar, wherever it is docked (top, left or the usual bottom/right).
        var x = area.Left > screen.Bounds.Left ? area.Left + Scale(12) : area.Right - size.Width - Scale(12);
        var y = area.Top > screen.Bounds.Top ? area.Top + Scale(12) : area.Bottom - size.Height - Scale(12);
        var bounds = new Rectangle(x, y, size.Width, size.Height);
        _trayPanelOpening = true;
        try
        {
            var panel = await OpenSurfaceAsync(new SurfaceSpec(SurfaceKind.Tray, new Dictionary<string, object?>()), bounds, activate: true);
            if (panel is null) return;
            _trayPanel = panel;
            panel.FormClosed += (_, _) => { _trayPanel = null; _trayPanelClosedAt = DateTime.UtcNow; };
        }
        finally { _trayPanelOpening = false; }
    }

    private void CloseTrayPanel() => _trayPanel?.Close();

    // ── Events from the service ──────────────────────────────────────────────────────────────────
    private void OnSessionEnded(SessionEndedEvent e)
    {
        CloseBlockScreens();
        var presentation = Settings.SessionEnd;
        // With the window on screen, the page shows its own summary; never show it twice.
        if (presentation == SessionEndPresentation.None || MainIsOnScreen) return;
        var payload = new Dictionary<string, object?> { ["kind"] = "sessionEnd", ["sessionId"] = e.SessionId };
        if (presentation == SessionEndPresentation.Notification) { ShowNotice(payload); return; }
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = Scale(new Size(480, 680));
        if (presentation == SessionEndPresentation.DimmedWindow) ShowScrims(includePrimary: true);
        _ = OpenSurfaceAsync(new SurfaceSpec(SurfaceKind.Summary, new Dictionary<string, object?> { ["sessionId"] = e.SessionId, ["dimmed"] = presentation == SessionEndPresentation.DimmedWindow }),
            new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height), activate: true);
    }

    private void OnPhaseChanged(CyclePhaseTransition transition)
    {
        if (!Settings.CyclePhaseNotifications || MainIsOnScreen) return;
        ShowNotice(new Dictionary<string, object?>
        {
            ["kind"] = "phase",
            ["endedKind"] = transition.EndedKind?.ToString(),
            ["endedCycle"] = transition.EndedFocusCycleNumber,
            ["startedKind"] = transition.StartedKind?.ToString(),
            ["startedCycle"] = transition.StartedFocusCycleNumber,
            ["startedEndUtc"] = UtcText(transition.StartedEndUtc),
        });
    }

    private void OnApplicationBlocked(ApplicationBlockedEvent e)
    {
        CloseBlockScreens();
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        // One interactive block screen on the primary monitor; the others only darken.
        foreach (var screen in Screen.AllScreens.Where(screen => !ReferenceEquals(screen, primary) && screen.DeviceName != primary.DeviceName))
            _scrims.Add(ShowScrim(screen.Bounds, opacity: 0.86));
        _ = OpenSurfaceAsync(new SurfaceSpec(SurfaceKind.Block, new Dictionary<string, object?>
        {
            ["target"] = e.Target, ["startUtc"] = UtcText(e.StartUtc), ["endUtc"] = UtcText(e.EndUtc), ["strict"] = e.Strict,
        }), primary.Bounds, activate: true);
    }

    /// <summary>The page accepts only "Z" timestamps; never let a local offset reach it.</summary>
    private static string? UtcText(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private async void ShowNotice(Dictionary<string, object?> payload)
    {
        try { await ShowNoticeAsync(payload); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { RecordFailure("notice", exception); }
    }

    private async Task ShowNoticeAsync(Dictionary<string, object?> payload)
    {
        _notice?.Close();
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        var size = Scale(new Size(380, 112));
        var notice = await OpenSurfaceAsync(new SurfaceSpec(SurfaceKind.Toast, payload),
            new Rectangle(area.Right - size.Width - Scale(16), area.Bottom - size.Height - Scale(16), size.Width, size.Height), activate: false);
        if (notice is null) return;
        _notice?.Close();
        _notice = notice;
        notice.FormClosed += (_, _) => { if (ReferenceEquals(_notice, notice)) _notice = null; };
    }

    private void UpdateTrayText(StatusResponse? status)
    {
        var text = HostWindowStrings.Get("WindowTitle");
        if (status is { SessionState: "Active" } && status.RemainingSeconds > 0)
            text = $"{text} · {HostWindowStrings.Get("TrayRemaining").Replace("{minutes}", Math.Ceiling(status.RemainingSeconds / 60d).ToString(System.Globalization.CultureInfo.CurrentCulture))}";
        if (!_quitting && _tray.Text != text) _tray.Text = text.Length > 120 ? text[..120] : text;
    }

    // ── Surfaces ─────────────────────────────────────────────────────────────────────────────────
    private async Task<SurfaceForm?> OpenSurfaceAsync(SurfaceSpec spec, Rectangle bounds, bool activate)
    {
        CoreWebView2Environment environment;
        try { environment = await (_environment ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder())); }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or IOException
                                           or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                           or WebView2RuntimeNotFoundException)
        {
            // Without WebView2 the main window already explains how to fix it; popups stay silent.
            _environment = null;
            return null;
        }

        if (_quitting) return null;
        try { return ShowSurface(spec, environment, bounds, activate); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordFailure("surface:" + spec.Kind, exception);
            return null;
        }
    }

    private SurfaceForm ShowSurface(SurfaceSpec spec, CoreWebView2Environment environment, Rectangle bounds, bool activate)
    {
        SurfaceForm? owner = null;
        var form = new SurfaceForm(spec, environment,
            lifetime => _surfaceBridge(lifetime, new SurfaceShellController(this, () => owner)), bounds, Dark);
        owner = form;
        _surfaces.Add(form);
        form.FormClosed += (_, _) =>
        {
            _surfaces.Remove(form);
            if (form.Kind == SurfaceKind.Block || form.Kind == SurfaceKind.Summary) CloseScrims();
        };
        if (activate) { form.Show(); form.Activate(); }
        else form.Show();
        return form;
    }

    private void CloseSurface(SurfaceForm form)
    {
        form.AllowUserClose = true;
        form.Close();
    }

    private void CloseBlockScreens()
    {
        foreach (var surface in _surfaces.Where(surface => surface.Kind == SurfaceKind.Block).ToArray()) CloseSurface(surface);
    }

    private void ShowScrims(bool includePrimary)
    {
        foreach (var screen in Screen.AllScreens.Where(screen => includePrimary || !screen.Primary)) _scrims.Add(ShowScrim(screen.Bounds, opacity: 0.55));
    }

    private static Form ShowScrim(Rectangle bounds, double opacity)
    {
        var scrim = new Form
        {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Bounds = bounds, BackColor = Color.Black, Opacity = opacity, TopMost = true,
        };
        scrim.Show();
        return scrim;
    }

    private void CloseScrims()
    {
        foreach (var scrim in _scrims.ToArray()) scrim.Close();
        _scrims.Clear();
    }

    private static int Scale(int value)
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Round(value * graphics.DpiX / 96d);
    }

    private static Size Scale(Size size) => new(Scale(size.Width), Scale(size.Height));

    private static string UserDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "WebView2");

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_quitting)
        {
            _quitting = true;
            _monitor.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Binds shell commands from one popup to that popup, so "close" closes the right window.</summary>
    private sealed class SurfaceShellController(HostShell shell, Func<SurfaceForm?> owner) : IHostShellController
    {
        public ValueTask<bool> ExecuteAsync(HostAppCommand command, CancellationToken cancellationToken) =>
            shell.ExecuteFor(owner(), command);
    }
}
