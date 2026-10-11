using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using FocusGuard.Client.Platform;

namespace FocusGuard.Platform.Windows;

public sealed class DesktopSingleInstance : IDesktopInstance
{
    private const int ActivationRetryCount = 20;
    private static readonly TimeSpan ActivationRetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private bool _ownsMutex;

    private DesktopSingleInstance(Mutex mutex, EventWaitHandle activation)
    {
        _mutex = mutex;
        _activation = activation;
        _ownsMutex = true;
    }

    [SupportedOSPlatform("windows")]
    public static bool TryAcquire(out DesktopSingleInstance? instance) =>
        TryAcquire(CreateInstanceKey(), out instance);

    /// <summary>
    /// Asks an already running FocusGuard of this user and session to come forward, without ever becoming one. Used
    /// when a launch cannot run itself (for example from the elevated Setup), so it never creates the mutex.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool TrySignalExisting()
    {
        var instanceKey = CreateInstanceKey();
        try
        {
            if (!Mutex.TryOpenExisting($@"Local\FocusGuard.Desktop.Mutex.{instanceKey}", out var existing)) return false;
            existing.Dispose();
            SignalExistingInstance($@"Local\FocusGuard.Desktop.Activate.{instanceKey}");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static bool TryAcquire(string instanceKey, out DesktopSingleInstance? instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceKey);
        var mutexName = $@"Local\FocusGuard.Desktop.Mutex.{instanceKey}";
        var activationName = $@"Local\FocusGuard.Desktop.Activate.{instanceKey}";
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            SignalExistingInstance(activationName);
            instance = null;
            return false;
        }

        try
        {
            var activation = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                activationName);
            instance = new DesktopSingleInstance(mutex, activation);
            return true;
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void StartListening(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (_listener is not null)
        {
            return;
        }

        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _activation, _shutdown.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                activationRequested();
            }
        });
    }

    public void StopListening()
    {
        _shutdown.Cancel();
        try
        {
            _listener?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _listener = null;
    }

    public void Dispose()
    {
        StopListening();
        _activation.Dispose();
        _shutdown.Dispose();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }
        _mutex.Dispose();
    }

    [SupportedOSPlatform("windows")]
    private static void SignalExistingInstance(string activationName)
    {
        for (var attempt = 0; attempt < ActivationRetryCount; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(activationName);
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (attempt < ActivationRetryCount - 1)
                {
                    Thread.Sleep(ActivationRetryDelay);
                }
            }
        }
    }

    private static string CreateInstanceKey()
    {
        var identity = OperatingSystem.IsWindows()
            ? WindowsIdentity.GetCurrent().User?.Value
            : Environment.UserName;
        var source = $"{Environment.ProcessId}:{identity}";
        if (OperatingSystem.IsWindows())
        {
            source = $"{System.Diagnostics.Process.GetCurrentProcess().SessionId}:{identity}";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24];
    }
}
