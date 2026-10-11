using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FocusGuard.Setup;

/// <summary>
/// The installer window with the React UI. The page is served from resources embedded in this executable (never from
/// disk), cannot navigate anywhere else, and can only ask for the actions <see cref="SetupBridge"/> accepts. All
/// decisions that change the machine stay here and in the packaged scripts, exactly as in the classic window.
/// </summary>
internal sealed class WebSetupForm : Form
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SetupOperations _operations;
    private readonly string _version;
    private readonly bool _smoke;
    private readonly string _userDataFolder = Path.Combine(Path.GetTempPath(), "FocusGuard.Setup." + Guid.NewGuid().ToString("N"));
    private WebView2? _webView;
    private TaskCompletionSource<bool>? _blockerAnswer;
    private bool _busy;
    private bool _closing;

    internal WebSetupForm(string packageDirectory, string version, bool smoke = false)
    {
        _operations = new SetupOperations(packageDirectory, Log);
        _version = version;
        _smoke = smoke;
        Text = "FocusGuard Setup";
        AccessibleName = "Instalador de FocusGuard";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 660);
        MinimumSize = new Size(560, 540);
        BackColor = Color.FromArgb(12, 11, 18);
    }

    /// <summary>Set when WebView2 cannot start; Program then shows the classic installer window instead.</summary>
    internal bool FallbackRequested { get; private set; }

    internal bool SmokeSucceeded { get; private set; }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedCorners();
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or IOException
                                           or UnauthorizedAccessException or WebView2RuntimeNotFoundException or ArgumentException)
        {
            FallbackRequested = true;
            BeginInvoke(new Action(Close));
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedCorners();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Never leave an install half-watched: the window stays until the script has finished (it rolls back on failure).
        if (_busy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
        _closing = true;
        _blockerAnswer?.TrySetResult(false);
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _webView?.Dispose();
        _webView = null;
        base.OnFormClosed(e);
        try { if (Directory.Exists(_userDataFolder)) Directory.Delete(_userDataFolder, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _ = exception; }
    }

    private async Task InitializeAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        var webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        _webView = webView;
        Controls.Add(webView);
        await webView.EnsureCoreWebView2Async(environment);
        var core = webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) => args.Response = Serve(environment, args.Request.Uri);
        core.NavigationStarting += (_, args) => { if (!SetupBridge.IsTrustedSource(args.Uri)) args.Cancel = true; };
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => { args.Cancel = true; args.Handled = true; };
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
        core.ClientCertificateRequested += (_, args) => args.Cancel = true;
        core.ProcessFailed += (_, _) => { if (!_busy) { FallbackRequested = true; BeginInvoke(new Action(Close)); } };
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate(SetupBridge.Origin + "/setup.html");
    }

    /// <summary>Every request is answered from this executable's resources, or refused.</summary>
    private static CoreWebView2WebResourceResponse Serve(CoreWebView2Environment environment, string uri)
    {
        if (SetupBridge.TryResolveAsset(uri, out var asset)
            && Assembly.GetExecutingAssembly().GetManifestResourceStream(asset.ResourceName) is { } stream)
        {
            var buffer = new MemoryStream();
            using (stream) stream.CopyTo(buffer);
            buffer.Position = 0;
            return environment.CreateWebResourceResponse(buffer, 200, "OK",
                $"Content-Type: {asset.ContentType}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store");
        }

        return environment.CreateWebResourceResponse(new MemoryStream([]), 403, "Blocked", "Content-Type: text/plain; charset=utf-8");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_closing || !SetupBridge.IsTrustedSource(e.Source) || !SetupBridge.TryParse(e.WebMessageAsJson, out var request) || request is null) return;
        switch (request.Kind)
        {
            case SetupRequestKind.Hello:
                Post(new { type = "setup.state", version = _version, installed = SetupOperations.IsInstalled });
                if (_smoke) { SmokeSucceeded = true; BeginInvoke(new Action(Close)); }
                break;
            case SetupRequestKind.Install when !_busy:
                await RunAsync("install", _operations.InstallAsync);
                break;
            case SetupRequestKind.Uninstall when !_busy && SetupOperations.IsInstalled:
                await RunAsync("uninstall", _operations.UninstallAsync);
                break;
            case SetupRequestKind.Blockers:
                _blockerAnswer?.TrySetResult(request.Close);
                break;
            case SetupRequestKind.Open:
                if (_operations.OpenInstalledFocusGuard()) Close();
                break;
            case SetupRequestKind.Close when !_busy:
                Close();
                break;
            case SetupRequestKind.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case SetupRequestKind.Drag:
                if (ReleaseCapture()) _ = SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
                break;
        }
    }

    private async Task RunAsync(string operation, Func<Task<int>> run)
    {
        _busy = true;
        var exitCode = -1;
        try
        {
            if (!await ClearBlockersAsync())
            {
                Log(operation == "install" ? "Instalación cancelada: hay programas usando archivos de FocusGuard." : "Desinstalación cancelada: hay programas usando archivos de FocusGuard.");
                return;
            }

            exitCode = await run();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException
                                           or System.ComponentModel.Win32Exception)
        {
            Log($"ERROR: {exception.Message}");
        }
        finally
        {
            _busy = false;
        }

        Post(new { type = "setup.done", operation, ok = exitCode == 0, exitCode });
    }

    /// <summary>The page shows which programs hold files and the person decides; nothing is closed without that answer.</summary>
    private async Task<bool> ClearBlockersAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var blocking = await _operations.FindBlockersAsync();
            if (blocking.Count == 0) return true;
            _blockerAnswer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(new { type = "setup.blockers", apps = blocking.Select(process => process.ApplicationName).Distinct().Take(16).ToArray() });
            var close = await _blockerAnswer.Task;
            _blockerAnswer = null;
            if (!close) return false;
            foreach (var process in blocking) _operations.CloseProcess(process.ProcessId);
            await Task.Delay(500);
        }

        Log("No se pudo liberar todos los archivos de FocusGuard; la instalación puede fallar.");
        return true;
    }

    private void Log(string? line)
    {
        if (line is null) return;
        var text = line.Length > 2000 ? line[..2000] : line;
        Post(new { type = "setup.log", line = text });
    }

    private void Post(object message)
    {
        if (_closing || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => Post(message))); return; }
        if (_webView?.CoreWebView2 is { } core && SetupBridge.IsTrustedSource(core.Source))
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private void ApplyRoundedCorners()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            var round = 2;
            _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            return;
        }

        var radius = (int)Math.Round(14 * DeviceDpi / 96d) * 2;
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, int lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
