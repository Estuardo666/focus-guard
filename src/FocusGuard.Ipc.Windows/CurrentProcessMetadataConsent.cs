using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows;

/// <summary>Explicit, lifetime-scoped current-process metadata permission. Never grants process control.</summary>
[SupportedOSPlatform("windows")]
public sealed class CurrentProcessMetadataConsent : IDisposable
{
    public const int QueryLimitedInformation = 0x1000;
    private static readonly object Gate = new();
    private static byte[]? SharedOriginal;
    private static byte[]? SharedApplied;
    private static int References;
    private readonly byte[]? _originalDacl;
    private readonly byte[]? _appliedDacl;
    private bool _disposed;

    private CurrentProcessMetadataConsent(byte[]? original, byte[]? applied)
    {
        _originalDacl = original; _appliedDacl = applied;
    }

    public static CurrentProcessMetadataConsent Enable(bool explicitlyConsented)
    {
        if (!explicitlyConsented) throw new UnauthorizedAccessException("Current-process metadata consent required.");
        lock (Gate) return EnableLocked();
    }

    private static CurrentProcessMetadataConsent EnableLocked()
    {
        var original = ReadCurrentDacl();
        if (SharedApplied is not null)
        {
            if (!original.AsSpan().SequenceEqual(SharedApplied))
                throw new InvalidOperationException("Process ACL changed while metadata consent is active.");
            References++;
            return new(SharedOriginal, SharedApplied);
        }
        var acl = new RawAcl(original, 0);
        var localService = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
        var alreadyAllowed = false;
        foreach (GenericAce ace in acl)
        {
            if (ace is CommonAce entry && entry.SecurityIdentifier.Equals(localService)
                && (entry.AccessMask & QueryLimitedInformation) != 0)
            {
                if (entry.AceQualifier == AceQualifier.AccessDenied)
                    throw new UnauthorizedAccessException("Existing LocalService metadata deny is retained.");
                if (entry.AceQualifier == AceQualifier.AccessAllowed) alreadyAllowed = true;
            }
        }
        if (alreadyAllowed) return new(null, null);
        acl.InsertAce(acl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed,
            QueryLimitedInformation, localService, false, null));
        var applied = new byte[acl.BinaryLength];
        acl.GetBinaryForm(applied, 0);
        WriteCurrentDacl(applied);
        SharedOriginal = original; SharedApplied = applied; References = 1;
        return new(original, applied);
    }

    public void Dispose()
    {
        lock (Gate) DisposeLocked();
    }

    private void DisposeLocked()
    {
        if (_disposed) return;
        _disposed = true;
        if (_originalDacl is null || _appliedDacl is null) return;
        if (--References > 0) return;
        var current = ReadCurrentDacl();
        if (!current.AsSpan().SequenceEqual(_appliedDacl))
            throw new InvalidOperationException("Process ACL changed concurrently; refusing to overwrite it during consent release.");
        WriteCurrentDacl(_originalDacl);
        SharedOriginal = null; SharedApplied = null;
    }

    internal static byte[] ReadCurrentDacl()
    {
        var result = GetSecurityInfo(GetCurrentProcess(), 6, 4, out _, out _, out var dacl, out _, out var descriptor);
        if (result != 0) throw new Win32Exception((int)result);
        try
        {
            if (dacl == IntPtr.Zero) throw new UnauthorizedAccessException("Null current-process DACL denied.");
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            var raw = new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl
                ?? throw new UnauthorizedAccessException("Missing current-process DACL.");
            var acl = new byte[raw.BinaryLength];
            raw.GetBinaryForm(acl, 0);
            return acl;
        }
        finally { LocalFree(descriptor); }
    }

    private static void WriteCurrentDacl(byte[] acl)
    {
        var pinned = GCHandle.Alloc(acl, GCHandleType.Pinned);
        try
        {
            var result = SetSecurityInfo(GetCurrentProcess(), 6, 4, IntPtr.Zero, IntPtr.Zero,
                pinned.AddrOfPinnedObject(), IntPtr.Zero);
            if (result != 0) throw new Win32Exception((int)result);
        }
        finally { pinned.Free(); }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(IntPtr handle, int type, uint flags,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll")] private static extern uint SetSecurityInfo(IntPtr handle, int type, uint flags,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
}
