using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Ipc.Windows;

public sealed record ObservedServiceIdentity(string ServiceName, uint ProcessId, uint State, string Account, string ImagePath, string CommandLine, uint ServiceType = 0x10);

public static class WindowsServiceIdentity
{
    [SupportedOSPlatform("windows")]
    public static ObservedServiceIdentity Read(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        using var manager = OpenSCManager(null, null, 1); // CONNECT only
        if (manager.IsInvalid) throw Failure();
        using var service = OpenService(manager, serviceName, 5); // QUERY_CONFIG | QUERY_STATUS only
        if (service.IsInvalid) throw Failure();
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _)) throw Failure();
        QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        if (Marshal.GetLastWin32Error() != 122 || needed is < 1 or > 32768) throw Failure();
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _)) throw Failure();
            var config = Marshal.PtrToStructure<ServiceConfig>(buffer);
            var command = Marshal.PtrToStringUni(config.BinaryPath) ?? throw new InvalidDataException("Missing SCM image.");
            var account = Marshal.PtrToStringUni(config.Account) ?? throw new InvalidDataException("Missing SCM account.");
            return new(serviceName, status.ProcessId, status.State, account, ParseImage(command), command, status.Type);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string ParseImage(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 2) throw new InvalidDataException("Ambiguous SCM image.");
            command = command[1..end];
        }
        else
        {
            var executableEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableEnd < 0) throw new InvalidDataException("Missing SCM executable.");
            command = command[..(executableEnd + 4)];
        }
        // Existing Setup registered an unquoted absolute path without arguments.
        // This parser captures configuration only; actual server validation must
        // match the running PID image to a protected expected path and hash.
        if (command.Contains('"') || !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SCM command is not a bare executable path.");
        if (!Path.IsPathFullyQualified(command)) throw new InvalidDataException("SCM image is not absolute.");
        return Path.GetFullPath(command);
    }

    private static Win32Exception Failure() => new(Marshal.GetLastWin32Error(), "SCM identity unavailable.");
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint Type, State, Accepted, Win32Exit, SpecificExit, Checkpoint, Hint, ProcessId, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig { public uint Type, Start, Error; public IntPtr BinaryPath, Group; public uint Tag; public IntPtr Dependencies, Account, Display; }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(ServiceHandle service, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
