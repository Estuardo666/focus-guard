using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FocusGuard.Infrastructure.Windows.Processes;

public interface IWindowsProcessIdSource
{
    IReadOnlyList<int> GetProcessIds();
}

public sealed class WindowsProcessIdSource : IWindowsProcessIdSource
{
    private const int InitialCapacity = 256;

    public IReadOnlyList<int> GetProcessIds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<int>();
        }

        var capacity = InitialCapacity;
        while (true)
        {
            var buffer = ArrayPool<int>.Shared.Rent(capacity);
            try
            {
                if (!K32EnumProcesses(
                        buffer,
                        checked(buffer.Length * sizeof(int)),
                        out var bytesWritten))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (bytesWritten < buffer.Length * sizeof(int))
                {
                    var count = bytesWritten / sizeof(int);
                    var result = new int[count];
                    Array.Copy(buffer, result, count);
                    return result;
                }

                capacity = checked(buffer.Length * 2);
            }
            finally
            {
                ArrayPool<int>.Shared.Return(buffer);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32EnumProcesses(
        [Out] int[] processIds,
        int bufferSizeBytes,
        out int bytesWritten);
}
