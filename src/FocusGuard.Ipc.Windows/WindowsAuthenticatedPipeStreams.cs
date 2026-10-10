using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Ipc.Windows;

/// <summary>Local-only transport primitives. Native peer/role verification is still required per request.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsAuthenticatedPipeStreams
{
    // Specific data/attribute/read-control/synchronize rights, never FILE_CREATE_PIPE_INSTANCE (4).
    public const uint ClientAccessMask = 0x0012019b;
    public const uint RemoteClientsRejected = 8;

    public static PipeSecurity CreateSecurity(string ownerLogonSid, string serverSid)
    {
        var owner = new SecurityIdentifier(ownerLogonSid);
        if (!owner.Value.StartsWith("S-1-5-5-", StringComparison.Ordinal))
            throw new ArgumentException("Interactive logon SID required.", nameof(ownerLogonSid));
        return CreateOwnerSecurity(owner.Value, serverSid);
    }

    public static PipeSecurity CreateOwnerSecurity(string ownerSid, string serverSid)
    {
        var owner = new SecurityIdentifier(ownerSid);
        var server = new SecurityIdentifier(serverSid);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(server, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(owner, (PipeAccessRights)ClientAccessMask, AccessControlType.Allow));
        return security;
    }

    public static NamedPipeServerStream CreateServer(string name, PipeSecurity security, bool firstInstance, int maximumInstances = 16)
    {
        ValidateName(name);
        if (maximumInstances is < 1 or > 254) throw new ArgumentOutOfRangeException(nameof(maximumInstances));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pinned.AddrOfPinnedObject(), Inherit = false
            };
            var handle = CreateNamedPipe(@"\\.\pipe\" + name,
                3u | 0x40000000u | (firstInstance ? 0x80000u : 0u), RemoteClientsRejected,
                (uint)maximumInstances, 65540, 65540, 0, ref attributes);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { pinned.Free(); }
    }

    public static async Task<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
    {
        ValidateName(name);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Identification SQOS: a squatter cannot obtain an impersonation-capable token.
            var handle = CreateFile(@"\\.\pipe\" + name, ClientAccessMask, 0, IntPtr.Zero, 3,
                0x40000000u | 0x00110000u, IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                try { return new NamedPipeClientStream(PipeDirection.InOut, true, true, handle); }
                catch { handle.Dispose(); throw; }
            }
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error is not (2 or 231)) throw new Win32Exception(error);
            await Task.Delay(TimeSpan.FromMilliseconds(25), token).ConfigureAwait(false);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("Local pipe leaf name required.", nameof(name));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint mode, uint pipeMode, uint maximum,
        uint outSize, uint inSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateFile(string name, uint access, uint share, IntPtr attributes,
        uint creation, uint flags, IntPtr template);
}
