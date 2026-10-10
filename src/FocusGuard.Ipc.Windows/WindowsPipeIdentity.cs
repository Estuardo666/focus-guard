using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Ipc.Windows;

/// <summary>Observed metadata only. Capture success alone is not authorization.</summary>
public sealed class ObservedPipeClient : IDisposable
{
    internal ObservedPipeClient(SafeProcessHandle handle, uint pid, string sid, string logonSid, uint sessionId,
        string imagePath, DateTimeOffset startedUtc)
    {
        ProcessHandle = handle; ProcessId = pid; UserSid = sid; LogonSid = logonSid;
        SessionId = sessionId; ImagePath = imagePath; StartedUtc = startedUtc;
    }
    internal SafeProcessHandle ProcessHandle { get; }
    public uint ProcessId { get; }
    public string UserSid { get; }
    public string LogonSid { get; }
    public uint SessionId { get; }
    public string ImagePath { get; }
    public DateTimeOffset StartedUtc { get; }
    public void Dispose() => ProcessHandle.Dispose();
}

public static class WindowsPipeIdentity
{
    [SupportedOSPlatform("windows")]
    public static uint ServerSessionId(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerSessionId(pipe.SafePipeHandle, out var sessionId)) throw NativeFailure();
        return sessionId;
    }
    [SupportedOSPlatform("windows")]
    public static bool IsAlive(ObservedPipeClient peer) =>
        !peer.ProcessHandle.IsClosed && !peer.ProcessHandle.IsInvalid
        && GetExitCodeProcess(peer.ProcessHandle, out var code) && code == 259;

    [SupportedOSPlatform("windows")]
    public static uint ServerProcessId(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid == 0) throw NativeFailure();
        return pid;
    }

    [SupportedOSPlatform("windows")]
    public static ObservedPipeClient CaptureClient(NamedPipeServerStream pipe)
    {
        // WindowsIdentity inherits ClaimsIdentity. Load its dependencies under the
        // server token, before an Identification token prevents filesystem access
        // during the callback's first JIT/type load in a fresh service process.
        using (var serverIdentity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
            _ = serverIdentity.User;
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid == 0
            || !GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId)) throw NativeFailure();
        string? sid = null, logonSid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(true)
                ?? throw new UnauthorizedAccessException("Pipe caller token unavailable.");
            sid = identity.User?.Value;
            logonSid = ReadLogonSid(identity.AccessToken);
            // Only token identification is read here; no file/process/service operations under caller token.
        });
        if (sid is null || logonSid is null) throw new UnauthorizedAccessException("Pipe identity incomplete.");
        // This read occurs after RunAsClient has reverted, under the service's own identity.
        return CaptureProcess(pid, sid, logonSid, sessionId);
    }

    [SupportedOSPlatform("windows")]
    internal static ObservedPipeClient CaptureProcess(uint pid, string sid = "", string logonSid = "", uint? sessionId = null)
    {
        var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) { process.Dispose(); throw NativeFailure(); }
        try
        {
            var path = new char[32768];
            var length = path.Length;
            if (!QueryFullProcessImageName(process, 0, path, ref length)
                || !GetProcessTimes(process, out var created, out _, out _, out _)
                || !GetExitCodeProcess(process, out var code) || code != 259
                || !ProcessIdToSessionId(pid, out var processSession)
                || (sessionId is uint expectedSession && processSession != expectedSession))
                throw NativeFailure();
            return new(process, pid, sid, logonSid, processSession,
                Path.GetFullPath(new string(path, 0, length)), DateTimeOffset.FromFileTime(created).ToUniversalTime());
        }
        catch { process.Dispose(); throw; }
    }

    private static Win32Exception NativeFailure() => new(Marshal.GetLastWin32Error(), "Native pipe identity could not be verified.");
    [SupportedOSPlatform("windows")]
    private static string ReadLogonSid(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, 28, IntPtr.Zero, 0, out var needed); // TokenLogonSid
        if (needed is < 1 or > 65536) throw NativeFailure();
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformation(token, 28, buffer, needed, out _)) throw NativeFailure();
            if (Marshal.ReadInt32(buffer) != 1) throw new UnauthorizedAccessException("Interactive logon SID unavailable.");
            var offset = IntPtr.Size == 8 ? 8 : 4;
            var sid = Marshal.ReadIntPtr(buffer, offset);
            return new SecurityIdentifier(sid).Value;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr buffer, int size, out int needed);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(uint pid, out uint session);
}
