using System.Runtime.InteropServices;
using System.Text.Json;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FocusGuard.Host.Windows;

internal enum SurfaceKind { Tray, Toast, Summary, Block }

/// <summary>What a popup shows. Serialized to the page right after host.initialize; never read back from the page.</summary>
internal sealed record SurfaceSpec(SurfaceKind Kind, IReadOnlyDictionary<string, object?> Payload);

internal sealed record HostSurfaceMessage(string Type, int Version, string DocumentToken, string Surface, IReadOnlyDictionary<string, object?> Payload);

/// <summary>
/// A borderless WebView2 popup for the tray panel, notices, the session summary and the block screen. It loads the same
/// packaged UI with the same hardening as the main window and talks through its own bridge and document lease.
/// </summary>
internal sealed class SurfaceForm : Form
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SurfaceSpec _spec;
    private readonly CoreWebView2Environment _environment;
    private readonly Func<DocumentLifetime, NativeBridgeDispatcher> _bridgeFactory;
    private readonly DocumentLifetime _documentLifetime = new();
    private readonly bool _dark;
    private WebView2? _webView;
    private NativeBridgeDispatcher? _bridge;
    private ulong _navigationId;
    private bool _closing;
    private bool _revealed;
    private readonly Rectangle _target;
    private readonly System.Windows.Forms.Timer _revealFallback = new() { Interval = 1500 };

    internal SurfaceForm(SurfaceSpec spec, CoreWebView2Environment environment, Func<DocumentLifetime, NativeBridgeDispatcher> bridgeFactory,
        Rectangle bounds, bool dark)
    {
        _spec = spec;
        _environment = environment;
        _bridgeFactory = bridgeFactory;
        _dark = dark;
        Text = HostWindowStrings.Get("WindowTitle");
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        // Created off-screen: the page paints there with the person's theme already applied and says "surface.ready",
        // then the popup moves into place. Nothing half-painted (empty, then dark, then light) is ever on screen.
        _target = bounds;
        var virtualScreen = SystemInformation.VirtualScreen;
        Bounds = new Rectangle(virtualScreen.Right + 64, virtualScreen.Top, bounds.Width, bounds.Height);
        _revealFallback.Tick += (_, _) => Reveal();
        // Paint the page colour before WebView2 is ready so a popup never flashes white.
        BackColor = dark ? Color.FromArgb(16, 18, 26) : Color.FromArgb(244, 245, 250);
        KeyPreview = true;
    }

    internal SurfaceKind Kind => _spec.Kind;

    /// <summary>Moves the finished popup into place (on the page's "surface.ready", or after a short fallback).</summary>
    internal void Reveal()
    {
        if (_revealed || _closing || IsDisposed) return;
        _revealed = true;
        _revealFallback.Stop();
        Bounds = _target;
        if (!IsToast) Activate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    /// <summary>Windows 10 has no rounded-corner preference, so popups other than the block screen clip to a rounded shape.</summary>
    private void ApplyRoundedRegion()
    {
        if (_spec is null || _spec.Kind == SurfaceKind.Block || Environment.OSVersion.Version.Build >= 22000 || Width <= 0 || Height <= 0) return;
        var radius = (int)Math.Round(16 * DeviceDpi / 96d) * 2;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(Width - radius, 0, radius, radius, 270, 90);
        path.AddArc(Width - radius, Height - radius, radius, radius, 0, 90);
        path.AddArc(0, Height - radius, radius, radius, 90, 90);
        path.CloseFigure();
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    // WinForms reads CreateParams and ShowWithoutActivation from the base Form constructor, before _spec is assigned;
    // the handle is created later, when the real values apply.
    private bool IsToast => _spec?.Kind == SurfaceKind.Toast;

    /// <summary>Notices appear without stealing focus from whatever the person is doing.</summary>
    protected override bool ShowWithoutActivation => IsToast;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            const int ToolWindow = 0x00000080;
            const int NoActivate = 0x08000000;
            parameters.ExStyle |= ToolWindow;
            if (IsToast) parameters.ExStyle |= NoActivate;
            return parameters;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedCorners();
        try { await InitializeAsync(); }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ObjectDisposedException
                                           or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_closing) BeginInvoke(new Action(Close));
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // The tray panel behaves like a flyout: clicking elsewhere dismisses it.
        if (_spec.Kind == SurfaceKind.Tray && !_closing) BeginInvoke(new Action(Close));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc closes the flyout, notices and the summary. The block screen ignores it (Alt+F4 too), but never the Windows key.
        if (e.KeyCode == Keys.Escape && _spec.Kind != SurfaceKind.Block) { e.Handled = true; Close(); return; }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_spec.Kind == SurfaceKind.Block && e.CloseReason == CloseReason.UserClosing && !AllowUserClose)
        {
            e.Cancel = true;
            return;
        }

        _closing = true;
        _revealFallback.Stop();
        _revealFallback.Dispose();
        _documentLifetime.Dispose();
        if (_webView?.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
        }

        _webView?.Dispose();
        _webView = null;
        base.OnFormClosing(e);
    }

    /// <summary>Set by the shell when the page itself asks to close (the block screen's "Volver" button).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AllowUserClose { get; set; }

    private async Task InitializeAsync()
    {
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        if (!File.Exists(Path.Combine(uiFolder, "index.html"))) { Close(); return; }
        var webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        _webView = webView;
        Controls.Add(webView);
        await webView.EnsureCoreWebView2Async(_environment);
        if (_closing) return;
        var core = webView.CoreWebView2;
        WebViewHardening.Apply(core, uiFolder, _environment);
        _bridge = _bridgeFactory(_documentLifetime);
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate(WebViewSecurityPolicy.EntryUri.AbsoluteUri);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!WebViewSecurityPolicy.IsAllowedTopLevelNavigation(e.Uri)) { e.Cancel = true; return; }
        _documentLifetime.Invalidate();
        _navigationId = e.NavigationId;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.NavigationId != _navigationId || _webView?.CoreWebView2 is not { } core) return;
        if (!e.IsSuccess) { Close(); return; }
        if (!_revealed) _revealFallback.Start();
        var lease = _documentLifetime.BeginDocument();
        // The surface goes first: the page decides what to render the moment host.initialize arrives, without a flash
        // of the main window. The main window never receives a surface message.
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new HostSurfaceMessage("host.surface", BridgeLimits.ProtocolVersion, lease.Token,
            _spec.Kind.ToString().ToLowerInvariant(), _spec.Payload), Json));
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new HostInitializationMessage("host.initialize", BridgeLimits.ProtocolVersion, lease.Token,
            HostWindowStrings.Get("ShellTitle"), HostWindowStrings.Get("ShellDescription"), HostWindowStrings.Get("ShellReady"),
            HostWindowStrings.Get("BridgeUnavailable")), Json));
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (_bridge is null || _closing || !WebViewSecurityPolicy.IsTrustedBridgeSource(e.Source)) return;
            var response = await _bridge.HandleAsync(e.Source, e.WebMessageAsJson, CancellationToken.None);
            if (response is null || _closing || _webView?.CoreWebView2 is not { } core || !WebViewSecurityPolicy.IsTrustedBridgeSource(core.Source)) return;
            core.PostWebMessageAsJson(response);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            _ = exception;
        }
    }

    private void ApplyRoundedCorners()
    {
        if (_spec.Kind == SurfaceKind.Block) return;
        try
        {
            const int CornerPreference = 33;
            var round = 2;
            _ = DwmSetWindowAttribute(Handle, CornerPreference, ref round, sizeof(int));
            const int ImmersiveDarkMode = 20;
            var dark = _dark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, ImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows 10 keeps square corners.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>The WebView2 lockdown shared by every popup: local packaged UI only, no external navigation, no browser features.</summary>
internal static class WebViewHardening
{
    internal static void Apply(CoreWebView2 core, string uiFolder, CoreWebView2Environment environment)
    {
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsScriptEnabled = true;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.SetVirtualHostNameToFolderMapping(WebViewSecurityPolicy.VirtualHostName, uiFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;
        core.ClientCertificateRequested += (_, e) => e.Cancel = true;
        core.WebResourceRequested += (_, e) =>
        {
            if (WebViewSecurityPolicy.IsAllowedLocalResource(e.Request.Uri)) return;
            e.Response = environment.CreateWebResourceResponse(new MemoryStream([]), 403, "Blocked", "Content-Type: text/plain; charset=utf-8");
        };
    }
}
