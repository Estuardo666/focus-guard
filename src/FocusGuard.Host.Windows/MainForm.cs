using System.Runtime.InteropServices;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Platform;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FocusGuard.Host.Windows;

internal sealed class MainForm : Form, IHostWindowController
{
    private const int CompactClientWidth = 480;
    private const int CompactClientExpandedHeight = 340;
    private const int CompactClientCollapsedHeight = 220;
    private const int CompactMinimumWidth = 400;
    private static readonly JsonSerializerOptions BridgeJson = new(JsonSerializerDefaults.Web);

    private readonly IFocusControlClient _client;
    private readonly IHostControlAuthorization _authorization;
    private readonly IProfileStore? _profileStore;
    private readonly IApplicationCatalog? _applicationCatalog;
    private readonly IApplicationIconProvider? _iconProvider;
    private readonly FocusGuard.Client.Preferences.DesktopSettingsStore? _desktopSettingsStore;
    private readonly FocusGuard.Client.Journal.SessionJournalStore? _sessionJournalStore;
    private readonly HostSmokeScenario _smokeScenario;
    private readonly CancellationTokenSource _smokeTimeout = new();
    private readonly DocumentLifetime _documentLifetime = new();
    private readonly RendererRecoveryController _rendererRecovery = new();
    private readonly Panel _browserPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _errorPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _errorTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
    private readonly Label _errorDescription = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly Button _retryButton = new() { AutoSize = true };
    private readonly Button _closeButton = new() { AutoSize = true };
    private ResourceWebView2? _webView;
    private WebViewResourceController? _resources;
    private CoreWebView2Environment? _environment;
    private NativeBridgeDispatcher? _bridge;
    private string? _currentDocumentToken;
    private ulong _pendingNavigationId;
    private RendererFailureAction? _pendingRendererFailure;
    private int _simulatedRendererFailureCount;
    private Rectangle _fullWindowBounds;
    private FormWindowState _fullWindowState;
    private bool _compactMode;
    private bool _compactDetailsExpanded = true;
    private bool _crashRequested;
    private bool _realRendererExitObserved;
    private bool _initializing;
    private bool _closed;

    internal MainForm(
        IFocusControlClient client,
        IHostControlAuthorization authorization,
        HostSmokeScenario smokeScenario,
        IProfileStore? profileStore = null,
        IApplicationCatalog? applicationCatalog = null,
        IApplicationIconProvider? iconProvider = null,
        FocusGuard.Client.Preferences.DesktopSettingsStore? desktopSettingsStore = null,
        FocusGuard.Client.Journal.SessionJournalStore? sessionJournalStore = null)
    {
        _client = client;
        _authorization = authorization;
        _profileStore = profileStore;
        _applicationCatalog = applicationCatalog;
        _iconProvider = iconProvider;
        _desktopSettingsStore = desktopSettingsStore;
        _sessionJournalStore = sessionJournalStore;
        _smokeScenario = smokeScenario;
        var initialSettings = desktopSettingsStore?.Load() ?? FocusGuard.Client.Preferences.DesktopSettings.Default;
        BackColor = initialSettings.Theme == FocusGuard.Client.Preferences.AppTheme.Light
            ? Color.FromArgb(245, 244, 250) : Color.FromArgb(12, 11, 18);
        _browserPanel.BackColor = BackColor;

        Text = HostWindowStrings.Get("WindowTitle");
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        MinimumSize = new Size(720, 480);
        ClientSize = new Size(1200, 760);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildErrorPanel();
        Controls.Add(_browserPanel);
        Controls.Add(_errorPanel);
        _errorPanel.BringToFront();
        Shown += OnShown;
        FormClosing += OnFormClosing;
    }

    internal bool SmokeSucceeded { get; private set; }

    /// <summary>The first WinForms Show can be overridden by the launcher's SW_HIDE startup information.</summary>
    internal bool PresentToUser()
    {
        if (_closed || IsDisposed || Disposing) return false;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        // The managed Visible flag can already be true while the native window is still hidden.
        // A second explicit SW_SHOW preserves bounds/maximized state and honors this user-requested opening.
        _ = ShowWindow(Handle, 5);
        Activate();
        BringToFront();
        return IsWindowVisible(Handle);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    /// <summary>When the shell owns a tray icon, closing the window only hides it; quitting goes through the tray.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool CloseToTray { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IHostShellController? ShellController { get; set; }

    /// <summary>The signed-in user's "start with Windows" value; absent in smoke runs so tests never touch the registry.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IStartupRegistration? StartupRegistration { get; set; }

    /// <summary>Asks the page to show a section (from the tray panel or a notice). The page validates the message.</summary>
    internal void Navigate(string section)
    {
        var core = _webView?.CoreWebView2;
        if (core is null || _currentDocumentToken is not { } documentToken || !WebViewSecurityPolicy.IsTrustedBridgeSource(core.Source)) return;
        core.PostWebMessageAsJson(JsonSerializer.Serialize(new HostNavigateMessage("host.navigate", BridgeLimits.ProtocolVersion, documentToken, section), BridgeJson));
    }

    private void BuildErrorPanel()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(24),
            Anchor = AnchorStyles.None
        };
        layout.Controls.Add(_errorTitle, 0, 0);
        layout.Controls.Add(_errorDescription, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _retryButton.Text = HostWindowStrings.Get("Retry");
        _retryButton.Click += async (_, _) => await InitializeWebViewAsync(isRetry: true);
        _closeButton.Text = HostWindowStrings.Get("Close");
        _closeButton.Click += (_, _) => Close();
        buttons.Controls.Add(_retryButton);
        buttons.Controls.Add(_closeButton);
        layout.Controls.Add(buttons, 0, 2);

        _errorPanel.Controls.Add(layout);
        _errorPanel.Resize += (_, _) =>
        {
            layout.Left = Math.Max(0, (_errorPanel.ClientSize.Width - layout.Width) / 2);
            layout.Top = Math.Max(0, (_errorPanel.ClientSize.Height - layout.Height) / 2);
        };
    }

    private void OnShown(object? sender, EventArgs eventArgs)
    {
        if (IsSmokeMode)
        {
            _ = CloseSmokeAfterTimeoutAsync(_smokeTimeout.Token);
        }

        _ = InitializeWebViewAsync(isRetry: false);
    }

    private async Task CloseSmokeAfterTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            if (!_closed)
            {
                Close();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task InitializeWebViewAsync(bool isRetry)
    {
        if (_initializing || _closed)
        {
            return;
        }

        _initializing = true;
        _retryButton.Enabled = false;
        _errorPanel.Visible = false;
        _browserPanel.Visible = true;
        if (isRetry)
        {
            _rendererRecovery.BeginManualRetry();
        }

        TearDownWebView();
        try
        {
            var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
            var entryFile = Path.Combine(uiFolder, "index.html");
            if (!File.Exists(entryFile))
            {
                ShowNativeError("HostFailureTitle", "HostFailureDescription");
                return;
            }

            var userDataFolder = GetUserDataFolder();
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: GetBrowserExecutableFolder(),
                userDataFolder: userDataFolder,
                options: null);
            if (_closed)
            {
                return;
            }

            _environment = environment;
            var webView = new ResourceWebView2 { Dock = DockStyle.Fill, Visible = true, DefaultBackgroundColor = BackColor };
            _webView = webView;
            _browserPanel.Controls.Add(webView);
            await webView.EnsureCoreWebView2Async(environment);
            if (_closed)
            {
                return;
            }

            ConfigureCoreWebView(webView.CoreWebView2, uiFolder);
            var settings = _desktopSettingsStore?.Load() ?? FocusGuard.Client.Preferences.DesktopSettings.Default;
            var theme = settings.Theme == FocusGuard.Client.Preferences.AppTheme.Light ? "light" : "dark";
            var reduce = settings.ReduceMotion ? "true" : "false";
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                $"(()=>{{const apply=()=>{{document.documentElement.dataset.theme='{theme}';document.documentElement.lang='{settings.UiLanguage}';document.documentElement.dataset.reduceMotion='{reduce}';}};if(document.documentElement)apply();else document.addEventListener('DOMContentLoaded',apply,{{once:true}});}})();");
            if (_closed) return;
            _resources = new WebViewResourceController(this, webView);
            webView.CoreWebView2.Navigate(WebViewSecurityPolicy.EntryUri.AbsoluteUri);
        }
        catch (Exception exception) when (IsRuntimeMissing(exception))
        {
            ShowNativeError("RuntimeMissingTitle", "RuntimeMissingDescription");
        }
        catch (Exception exception) when (exception is COMException or IOException or InvalidOperationException
                                         or UnauthorizedAccessException or ArgumentException)
        {
            ShowNativeError("HostFailureTitle", "HostFailureDescription");
        }
        finally
        {
            _initializing = false;
            _retryButton.Enabled = true;
            if (_pendingRendererFailure is { } pendingFailure && !_closed)
            {
                _pendingRendererFailure = null;
                BeginInvoke(new Action(() => _ = CompleteRendererFailureAsync(pendingFailure)));
            }
        }
    }

    private void ConfigureCoreWebView(CoreWebView2 core, string uiFolder)
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
        core.SetVirtualHostNameToFolderMapping(
            WebViewSecurityPolicy.VirtualHostName,
            uiFolder,
            CoreWebView2HostResourceAccessKind.Deny);

        _bridge = new NativeBridgeDispatcher(_client, _authorization, _documentLifetime, profileStore: _profileStore,
            settingsStore: _desktopSettingsStore,
            sessionJournalStore: _sessionJournalStore,
            recoveryCodeSaver: SaveRecoveryCodeAsync,
            applicationCatalog: _applicationCatalog, iconProvider: _iconProvider,
            windowController: this, shellController: ShellController,
            startupRegistration: StartupRegistration);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.PermissionRequested += OnPermissionRequested;
        core.ServerCertificateErrorDetected += OnServerCertificateErrorDetected;
        core.BasicAuthenticationRequested += OnBasicAuthenticationRequested;
        core.ClientCertificateRequested += OnClientCertificateRequested;
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += OnProcessFailed;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!WebViewSecurityPolicy.IsAllowedTopLevelNavigation(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            return;
        }

        _documentLifetime.Invalidate();
        _currentDocumentToken = null;
        _pendingNavigationId = eventArgs.NavigationId;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (eventArgs.NavigationId != _pendingNavigationId)
        {
            return;
        }

        if (!eventArgs.IsSuccess)
        {
            ShowNativeError("HostFailureTitle", "HostFailureDescription");
            return;
        }

        var lease = _documentLifetime.BeginDocument();
        _currentDocumentToken = lease.Token;
        var initialization = new HostInitializationMessage(
            "host.initialize",
            BridgeLimits.ProtocolVersion,
            lease.Token,
            HostWindowStrings.Get("ShellTitle"),
            HostWindowStrings.Get("ShellDescription"),
            HostWindowStrings.Get("ShellReady"),
            HostWindowStrings.Get("BridgeUnavailable"));
        if (_webView?.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(initialization, BridgeJson));
            PostWindowState();
            _resources?.RefreshPage();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.M))
        {
            ApplyCompactMode(!_compactMode);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public ValueTask<HostWindowPresentation> SetCompactModeAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (InvokeRequired)
        {
            var result = (HostWindowPresentation)Invoke(new Func<HostWindowPresentation>(() => ApplyCompactMode(enabled)));
            return ValueTask.FromResult(result);
        }

        return ValueTask.FromResult(ApplyCompactMode(enabled));
    }

    public ValueTask<HostWindowPresentation> SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (InvokeRequired)
        {
            var result = (HostWindowPresentation)Invoke(new Func<HostWindowPresentation>(() => ApplyAlwaysOnTop(enabled)));
            return ValueTask.FromResult(result);
        }

        return ValueTask.FromResult(ApplyAlwaysOnTop(enabled));
    }

    public ValueTask<HostWindowPresentation> SetDetailsExpandedAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (InvokeRequired)
        {
            var result = (HostWindowPresentation)Invoke(new Func<HostWindowPresentation>(() => ApplyDetailsExpanded(enabled)));
            return ValueTask.FromResult(result);
        }

        return ValueTask.FromResult(ApplyDetailsExpanded(enabled));
    }

    private HostWindowPresentation ApplyCompactMode(bool enabled)
    {
        if (_closed || IsDisposed || Disposing)
        {
            throw new InvalidOperationException("The FocusGuard window is closing.");
        }

        if (_compactMode != enabled)
        {
            if (enabled)
            {
                _fullWindowBounds = WindowState is FormWindowState.Maximized or FormWindowState.Minimized
                    ? RestoreBounds
                    : Bounds;
                _fullWindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
                if (WindowState != FormWindowState.Normal)
                {
                    WindowState = FormWindowState.Normal;
                }

                _compactMode = true;
                var nonClientHeight = Math.Max(0, Height - ClientSize.Height);
                MinimumSize = new Size(CompactMinimumWidth, CompactClientCollapsedHeight + nonClientHeight);
                MaximizeBox = false;
                ClientSize = new Size(CompactClientWidth,
                    _compactDetailsExpanded ? CompactClientExpandedHeight : CompactClientCollapsedHeight);
                Bounds = KeepWindowOnScreen(Bounds);
            }
            else
            {
                _compactMode = false;
                MinimumSize = new Size(720, 480);
                MaximizeBox = true;
                if (!_fullWindowBounds.IsEmpty)
                {
                    Bounds = KeepWindowOnScreen(_fullWindowBounds);
                }

                if (_fullWindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Maximized;
                }
            }

            PostWindowState();
        }

        return CurrentWindowPresentation();
    }

    private HostWindowPresentation ApplyAlwaysOnTop(bool enabled)
    {
        if (_closed || IsDisposed || Disposing)
        {
            throw new InvalidOperationException("The FocusGuard window is closing.");
        }

        TopMost = enabled;
        PostWindowState();
        return CurrentWindowPresentation();
    }

    private HostWindowPresentation ApplyDetailsExpanded(bool enabled)
    {
        if (_closed || IsDisposed || Disposing)
        {
            throw new InvalidOperationException("The FocusGuard window is closing.");
        }

        if (_compactDetailsExpanded != enabled)
        {
            _compactDetailsExpanded = enabled;
            if (_compactMode)
            {
                ClientSize = new Size(CompactClientWidth,
                    enabled ? CompactClientExpandedHeight : CompactClientCollapsedHeight);
                Bounds = KeepWindowOnScreen(Bounds);
            }

            PostWindowState();
        }

        return CurrentWindowPresentation();
    }

    private Rectangle KeepWindowOnScreen(Rectangle bounds)
    {
        var workingArea = Screen.FromRectangle(bounds).WorkingArea;
        var width = Math.Min(bounds.Width, workingArea.Width);
        var height = Math.Min(bounds.Height, workingArea.Height);
        var left = Math.Clamp(bounds.Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        var top = Math.Clamp(bounds.Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));
        return new Rectangle(left, top, width, height);
    }

    private HostWindowPresentation CurrentWindowPresentation() => new(_compactMode, TopMost, _compactDetailsExpanded);

    private void PostWindowState()
    {
        var core = _webView?.CoreWebView2;
        if (core is null || _currentDocumentToken is not { } documentToken
            || !WebViewSecurityPolicy.IsTrustedBridgeSource(core.Source))
        {
            return;
        }

        var state = CurrentWindowPresentation();
        core.PostWebMessageAsJson(JsonSerializer.Serialize(
            new HostWindowStateMessage("host.windowState", BridgeLimits.ProtocolVersion, documentToken,
                state.Compact, state.AlwaysOnTop, state.DetailsExpanded),
            BridgeJson));
    }

    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs) =>
        eventArgs.Cancel = true;

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs) =>
        eventArgs.Handled = true;

    private static void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        eventArgs.Handled = true;
    }

    private ValueTask<bool> SaveRecoveryCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return ValueTask.FromResult(false);
        try
        {
            var saved = InvokeRequired
                ? (bool)Invoke(new Func<bool>(() => SaveRecoveryCodeOnUiThread(code, cancellationToken)))
                : SaveRecoveryCodeOnUiThread(code, cancellationToken);
            return ValueTask.FromResult(saved);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return ValueTask.FromResult(false);
        }
    }

    private bool SaveRecoveryCodeOnUiThread(string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new SaveFileDialog
        {
            Title = HostWindowStrings.Get("RecoveryCodeFileTitle"),
            FileName = "focusguard-recovery-code.txt",
            DefaultExt = "txt",
            Filter = "Text file (*.txt)|*.txt",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(dialog.FileName, code, new System.Text.UTF8Encoding(false));
        return true;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs eventArgs) =>
        eventArgs.State = CoreWebView2PermissionState.Deny;

    private static void OnServerCertificateErrorDetected(
        object? sender,
        CoreWebView2ServerCertificateErrorDetectedEventArgs eventArgs) =>
        eventArgs.Action = CoreWebView2ServerCertificateErrorAction.Cancel;

    private static void OnBasicAuthenticationRequested(
        object? sender,
        CoreWebView2BasicAuthenticationRequestedEventArgs eventArgs) =>
        eventArgs.Cancel = true;

    private static void OnClientCertificateRequested(
        object? sender,
        CoreWebView2ClientCertificateRequestedEventArgs eventArgs) =>
        eventArgs.Cancel = true;

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        if (WebViewSecurityPolicy.IsAllowedLocalResource(eventArgs.Request.Uri))
        {
            return;
        }

        var environment = _environment
            ?? throw new InvalidOperationException("WebView2 resource filtering was attached before environment setup.");
        var body = new MemoryStream([]);
        eventArgs.Response = environment.CreateWebResourceResponse(
            body,
            403,
            "Blocked",
            "Content-Type: text/plain; charset=utf-8");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            if (_bridge is null || !WebViewSecurityPolicy.IsTrustedBridgeSource(eventArgs.Source))
            {
                return;
            }

            var source = eventArgs.Source;
            var message = eventArgs.WebMessageAsJson;
            var response = await _bridge.HandleAsync(source, message, CancellationToken.None);
            if (response is null || _closed)
            {
                return;
            }

            var core = _webView?.CoreWebView2;
            if (core is not null && WebViewSecurityPolicy.IsTrustedBridgeSource(core.Source))
            {
                core.PostWebMessageAsJson(response);
                if (IsSmokeMode && response.Contains("AuthorizationUnavailable", StringComparison.Ordinal))
                {
                    if (_smokeScenario == HostSmokeScenario.LocalLoad)
                    {
                        SmokeSucceeded = true;
                        BeginInvoke(new Action(Close));
                    }
                    else if (_smokeScenario == HostSmokeScenario.RendererRecovery)
                    {
                        _simulatedRendererFailureCount++;
                        if (_simulatedRendererFailureCount <= 2)
                        {
                            BeginInvoke(new Action(DispatchRendererFailure));
                        }
                    }
                    else if (_smokeScenario == HostSmokeScenario.RendererCrash)
                    {
                        if (_crashRequested && _realRendererExitObserved)
                        {
                            SmokeSucceeded = true;
                            BeginInvoke(new Action(Close));
                        }
                        else if (!_crashRequested)
                        {
                            _crashRequested = true;
                            // Fixed native diagnostic command on this isolated WebView only.
                            // No process IDs, external processes or JavaScript-supplied methods.
                            _ = CrashSmokeRendererAsync(core);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or OperationCanceledException)
        {
            _ = exception;
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        if (_closed || IsDisposed || Disposing)
        {
            return;
        }

        if (_smokeScenario == HostSmokeScenario.RendererCrash
            && eventArgs.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
            _realRendererExitObserved = true;
        DispatchRendererFailure();
    }

    private async Task CrashSmokeRendererAsync(CoreWebView2 core)
    {
        try { await core.CallDevToolsProtocolMethodAsync("Page.crash", "{}").WaitAsync(_smokeTimeout.Token); }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or OperationCanceledException)
        {
            // The crashing target may reject its own command response. Only the
            // real ProcessFailed event and subsequent document reply prove recovery.
        }
    }

    private void DispatchRendererFailure()
    {
        if (_closed || IsDisposed || Disposing)
        {
            return;
        }

        var action = _rendererRecovery.RecordFailure(_documentLifetime);
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => _ = CompleteRendererFailureAsync(action)));
        }
        else if (_initializing)
        {
            _pendingRendererFailure = action;
        }
        else
        {
            _ = CompleteRendererFailureAsync(action);
        }
    }

    private async Task CompleteRendererFailureAsync(RendererFailureAction action)
    {
        if (_closed)
        {
            return;
        }

        if (_initializing)
        {
            _pendingRendererFailure = action;
            return;
        }

        if (action == RendererFailureAction.ShowManualRetry)
        {
            ShowNativeError("RendererFailureTitle", "RendererFailureDescription");
            return;
        }

        await InitializeWebViewAsync(isRetry: false);
    }

    private void ShowNativeError(string titleKey, string descriptionKey)
    {
        if (IsSmokeMode)
        {
            var expectedError =
                _smokeScenario == HostSmokeScenario.RuntimeMissing && titleKey == "RuntimeMissingTitle"
                || _smokeScenario == HostSmokeScenario.RendererRecovery && titleKey == "RendererFailureTitle";
            if (!expectedError)
            {
                BeginInvoke(new Action(Close));
                return;
            }

            PresentNativeError(titleKey, descriptionKey);
            SmokeSucceeded = _errorPanel.Visible
                && _errorTitle.Text == HostWindowStrings.Get(titleKey)
                && _errorDescription.Text == HostWindowStrings.Get(descriptionKey)
                && _retryButton.Enabled
                && _closeButton.Enabled;
            BeginInvoke(new Action(Close));
            return;
        }

        PresentNativeError(titleKey, descriptionKey);
    }

    private void PresentNativeError(string titleKey, string descriptionKey)
    {
        _documentLifetime.Invalidate();
        _browserPanel.Visible = false;
        _retryButton.Enabled = true;
        _closeButton.Enabled = true;
        _errorTitle.Text = HostWindowStrings.Get(titleKey);
        _errorDescription.Text = HostWindowStrings.Get(descriptionKey);
        _errorPanel.Visible = true;
        _errorPanel.BringToFront();
        _errorPanel.PerformLayout();
    }

    private void TearDownWebView()
    {
        _resources?.Dispose();
        _resources = null;
        if (_webView is null)
        {
            return;
        }

        if (_webView.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.FrameNavigationStarting -= OnFrameNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.DownloadStarting -= OnDownloadStarting;
            core.PermissionRequested -= OnPermissionRequested;
            core.ServerCertificateErrorDetected -= OnServerCertificateErrorDetected;
            core.BasicAuthenticationRequested -= OnBasicAuthenticationRequested;
            core.ClientCertificateRequested -= OnClientCertificateRequested;
            core.WebResourceRequested -= OnWebResourceRequested;
            core.WebMessageReceived -= OnWebMessageReceived;
            core.ProcessFailed -= OnProcessFailed;
        }

        _browserPanel.Controls.Remove(_webView);
        _webView.Dispose();
        _webView = null;
        _bridge = null;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (CloseToTray && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            // The service keeps the session either way; the tray keeps notices and the block screen alive.
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        _closed = true;
        _smokeTimeout.Cancel();
        _smokeTimeout.Dispose();
        _documentLifetime.Dispose();
        TearDownWebView();
        // Closing the renderer never sends a session stop request. The service
        // remains the authority for any existing session.
    }

    private string GetUserDataFolder()
    {
        if (IsSmokeMode)
        {
            return Path.Combine(GetSmokeRoot(), "userdata-" + Guid.NewGuid().ToString("N"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Local user data folder is unavailable.");
        }

        return Path.Combine(localAppData, "FocusGuard", "WebView2");
    }

    private string? GetBrowserExecutableFolder()
    {
        if (_smokeScenario != HostSmokeScenario.RuntimeMissing)
        {
            return null;
        }

        var emptyRuntimeFolder = Path.Combine(GetSmokeRoot(), "empty-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRuntimeFolder);
        return emptyRuntimeFolder;
    }

    private static string GetSmokeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard.Host.Windows.Smoke");
        Directory.CreateDirectory(root);
        return root;
    }

    private bool IsSmokeMode => _smokeScenario != HostSmokeScenario.None;

    private static bool IsRuntimeMissing(Exception exception) =>
        exception.GetType().Name.Contains("RuntimeNotFound", StringComparison.Ordinal)
        || exception is COMException comException && comException.HResult == unchecked((int)0x80040154)
        || exception is FileNotFoundException
        || exception is DllNotFoundException
        || exception is TypeInitializationException wrapped
            && wrapped.InnerException is { } inner
            && IsRuntimeMissing(inner);
}
