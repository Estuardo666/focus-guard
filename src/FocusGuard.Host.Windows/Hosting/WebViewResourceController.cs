using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;

namespace FocusGuard.Host.Windows.Hosting;

internal sealed class ResourceWebView2 : WebView2
{
    internal void SetResourceVisibility(bool visible)
    {
        Visible = visible;
        // When a parent is already hidden, WinForms sees false -> false and can skip the child's notification.
        // Synchronize WebView2's native controller too: TrySuspend requires its IsVisible to be false.
        base.OnVisibleChanged(EventArgs.Empty);
    }
}

/// <summary>Available physical RAM, with hysteresis so decorative GPU resources do not churn near a threshold.</summary>
internal sealed class MemoryPressurePolicy
{
    private const ulong GiB = 1024UL * 1024 * 1024;
    internal bool Constrained { get; private set; }

    internal bool Observe(ulong available, ulong total)
    {
        if (total == 0 || available > total) return Constrained;
        var fraction = (double)available / total;
        Constrained = Constrained
            ? available < GiB + GiB / 2 || fraction < 0.15
            : available < GiB || fraction < 0.10;
        return Constrained;
    }
}

/// <summary>A late suspend must never leave a window that was reopened during the await suspended.</summary>
internal sealed class RendererVisibilityController(Func<Task<bool>> suspend, Action resume, Action<bool> setVisible) : IDisposable
{
    private bool _visible;
    private bool _suspending;
    private bool _disposed;
    internal bool? LastSuspendSucceeded { get; private set; }

    internal async Task UpdateAsync(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        setVisible(visible);
        if (visible) { resume(); return; }
        if (_suspending) return;
        _suspending = true;
        try
        {
            LastSuspendSucceeded = await suspend();
            if (!_disposed && _visible) resume();
        }
        finally { _suspending = false; }
    }

    public void Dispose() => _disposed = true;
}

/// <summary>
/// Owns only renderer resources. The service and native shell keep enforcing policies and delivering notices.
/// Suspension retains the document, including unsaved edits. No GC loops, working-set trimming or GPU-disabling flags.
/// </summary>
internal sealed class WebViewResourceController : IDisposable
{
    internal string? LastFailure { get; private set; }
    internal bool? LastSuspendSucceeded => _visibility.LastSuspendSucceeded;
    private readonly Form _form;
    private readonly ResourceWebView2 _view;
    private readonly MemoryPressurePolicy _pressure = new();
    private readonly RendererVisibilityController _visibility;
    private readonly Func<(ulong Available, ulong Total)> _readMemory;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private bool? _lastConstrained;
    private bool? _lastHidden;
    private bool? _lastInactive;
    private bool _updating;
    private bool _refreshPending;
    private bool _active;
    private bool _disposed;

    internal WebViewResourceController(Form form, ResourceWebView2 view, Func<(ulong Available, ulong Total)>? readMemory = null)
    {
        _form = form;
        _view = view;
        _active = form.ContainsFocus || Form.ActiveForm == form;
        _readMemory = readMemory ?? ReadMemory;
        _visibility = new(() => view.CoreWebView2.TrySuspendAsync(), () => view.CoreWebView2.Resume(),
            view.SetResourceVisibility);
        form.VisibleChanged += OnChanged;
        form.Resize += OnChanged;
        form.Activated += OnActivated;
        form.Deactivate += OnDeactivated;
        _timer.Tick += OnChanged;
        _timer.Start();
    }

    internal void RefreshPage()
    {
        _lastConstrained = null;
        _lastHidden = null;
        _lastInactive = null;
        OnChanged(this, EventArgs.Empty);
    }

    private void OnActivated(object? sender, EventArgs e) { _active = true; OnChanged(sender, e); }
    private void OnDeactivated(object? sender, EventArgs e) { _active = false; OnChanged(sender, e); }

    private async void OnChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        // Resume synchronously even if an earlier script/suspend operation is pending.
        var visible = _form.Visible && _form.WindowState != FormWindowState.Minimized;
        var inactive = !_active;
        try
        {
            if (visible) await _visibility.UpdateAsync(true);
            if (_updating) { _refreshPending = true; return; }
            _updating = true;
            try
            {
                var memory = _readMemory();
                _pressure.Observe(memory.Available, memory.Total);
                if ((_lastConstrained != _pressure.Constrained || _lastHidden != !visible || _lastInactive != inactive)
                    && !_view.CoreWebView2.IsSuspended
                    && WebViewSecurityPolicy.IsTrustedBridgeSource(_view.CoreWebView2.Source))
                {
                    var constrained = _pressure.Constrained ? "true" : "false";
                    var hidden = visible ? "false" : "true";
                    await _view.CoreWebView2.ExecuteScriptAsync(
                        $"document.documentElement.dataset.resourceConstrained='{constrained}';document.documentElement.dataset.hostHidden='{hidden}';document.documentElement.dataset.hostInactive='{(inactive ? "true" : "false")}';");
                    if (_disposed) return;
                    _lastConstrained = _pressure.Constrained;
                    _lastHidden = !visible;
                    _lastInactive = inactive;
                }
                if (!_disposed) await _visibility.UpdateAsync(_form.Visible && _form.WindowState != FormWindowState.Minimized);
            }
            finally
            {
                _updating = false;
                if (_refreshPending && !_disposed)
                {
                    _refreshPending = false;
                    OnChanged(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ObjectDisposedException or NotImplementedException)
        {
            LastFailure = $"{exception.GetType().Name}: {exception.Message} ({exception.HResult:X})";
            // Unsupported/failed resource hints must not interfere with the window or renderer recovery.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _form.VisibleChanged -= OnChanged;
        _form.Resize -= OnChanged;
        _form.Activated -= OnActivated;
        _form.Deactivate -= OnDeactivated;
        _timer.Stop();
        _timer.Dispose();
        _visibility.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    private static (ulong Available, ulong Total) ReadMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? (memory.AvailablePhysical, memory.TotalPhysical) : (0, 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
