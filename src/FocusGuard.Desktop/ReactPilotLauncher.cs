using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FocusGuard.Desktop;

internal enum ReactPilotLaunchResult
{
    Started,
    AlreadyOpen,
    NotInstalled,
    Failed
}

internal interface IReactPilotProcessOperations
{
    bool FileExists(string path);
    bool ActivateExisting(string executablePath);
    bool Start(ProcessStartInfo startInfo);
}

internal sealed class ReactPilotLauncher(string installationDirectory, IReactPilotProcessOperations processOperations)
{
    private static readonly object LaunchGate = new();

    public ReactPilotLaunchResult Open()
    {
        lock (LaunchGate)
        {
            var installRoot = Path.GetFullPath(installationDirectory);
            var executablePath = Path.Combine(installRoot, "FocusGuard.UI.exe");
            if (!processOperations.FileExists(executablePath))
            {
                return ReactPilotLaunchResult.NotInstalled;
            }

            try
            {
                if (processOperations.ActivateExisting(executablePath))
                {
                    return ReactPilotLaunchResult.AlreadyOpen;
                }

                return processOperations.Start(CreateStartInfo(executablePath))
                    ? ReactPilotLaunchResult.Started
                    : ReactPilotLaunchResult.Failed;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or Win32Exception or System.Security.SecurityException
                or ArgumentException)
            {
                return ReactPilotLaunchResult.Failed;
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var startInfo = new ProcessStartInfo(fullPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--pilot");
        return startInfo;
    }
}

internal sealed class WindowsReactPilotProcessOperations : IReactPilotProcessOperations
{
    private const int RestoreWindow = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    public bool FileExists(string path) => File.Exists(path);

    public bool ActivateExisting(string executablePath)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var fullPath = Path.GetFullPath(executablePath);
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fullPath)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != current.SessionId
                        || process.MainModule?.FileName is not { } modulePath
                        || !string.Equals(Path.GetFullPath(modulePath), fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        _ = ShowWindowAsync(handle, RestoreWindow);
                        _ = SetForegroundWindow(handle);
                    }

                    return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception
                    or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                {
                    // An inaccessible process is not evidence that this executable is our UI.
                }
            }
        }

        return false;
    }

    public bool Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}
