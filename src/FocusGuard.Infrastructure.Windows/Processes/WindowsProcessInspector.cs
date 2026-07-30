using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Infrastructure.Windows.Processes;

public static class WindowsProcessInspector
{
    private const uint QueryLimitedInformation = 0x1000;
    private const int MaximumPathCharacters = 32_768;

    public static WindowsProcessInstance? TryRead(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return null;
        }

        using var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var path = new StringBuilder(MaximumPathCharacters);
        var pathLength = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref pathLength)
            || !GetProcessTimes(handle, out var created, out _, out _, out _))
        {
            return null;
        }

        try
        {
            var createdUtc = DateTimeOffset.FromFileTime(created.ToLong()).ToUniversalTime();
            return new WindowsProcessInstance(processId, createdUtc, Path.GetFullPath(path.ToString()));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint _low;
        private readonly uint _high;

        public long ToLong() => ((long)_high << 32) | _low;
    }
}
