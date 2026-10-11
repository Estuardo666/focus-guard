using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FocusGuard.Setup;

/// <summary>Use Explorer's actual desktop dispatcher, never a newly spawned elevated Explorer process.</summary>
internal static class InteractiveAppLauncher
{
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    private static string? OwnerSid(uint processId)
    {
        var process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) return null;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, 8, out token)) return null;
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value;
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }

    internal static bool IsPresented(string executable)
    {
        foreach (var process in Candidates(executable))
        {
            using (process)
            {
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle)) return true;
            }
        }
        return false;
    }

    private static IEnumerable<Process> Candidates(string executable)
    {
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var desktopPid) == 0) yield break;
        var owner = OwnerSid(desktopPid);
        if (owner is null) yield break;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            var match = false;
            try { match = process.SessionId == session && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase) && OwnerSid((uint)process.Id) == owner; }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (match) yield return process; else process.Dispose();
        }
    }

    internal static void Open(string executable)
    {
        // A button in the foreground installer may grant foreground activation to this exact installed UI.
        foreach (var process in Candidates(executable)) { using (process) _ = AllowSetForegroundWindow((uint)process.Id); }
        object? windows = null, desktop = null, document = null, application = null;
        try
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var desktopPid) == 0)
                throw new InvalidOperationException("The interactive desktop is unavailable.");
            using var desktopProcess = Process.GetProcessById((int)desktopPid);
            using var currentProcess = Process.GetCurrentProcess();
            if (desktopProcess.SessionId != currentProcess.SessionId) throw new InvalidOperationException("The interactive desktop session does not match.");
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!);
            object location = Type.Missing, root = Type.Missing;
            int desktopWindow;
            desktop = ((dynamic)windows!).FindWindowSW(location, root, 8, out desktopWindow, 1);
            if (desktop is null || desktopWindow != shell.ToInt32()) throw new InvalidOperationException("The desktop dispatcher did not match this session.");
            document = ((dynamic)desktop).Document;
            application = ((dynamic)document!).Application;
            ((dynamic)application!).ShellExecute(executable, "", Path.GetDirectoryName(executable), "open", 1);
        }
        finally
        {
            foreach (var value in new[] { application, document, desktop, windows })
                if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }
}
