using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class RestrictedPipeClientTests
{
    [SupportedOSPlatform("windows")]
    [Fact]
    public async Task RestrictedTokenFailsPipeDaclSecondCheckAndImpersonationIsReverted()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pipeName = "FocusGuard.Tests.RestrictedClient." + Guid.NewGuid().ToString("N");
        using var currentIdentity = WindowsIdentity.GetCurrent(TokenAccessLevels.Duplicate | TokenAccessLevels.Query);
        var userSid = currentIdentity.User?.Value;
        Assert.False(string.IsNullOrWhiteSpace(userSid));
        // The logon ACE is a fixture sentinel: this test exercises the OS restricted-SID
        // second access check, not the production logon grant. The current user's
        // server SID ACE permits the control exchange before and after denial.
        // Native TokenLogonSid capture is covered separately.
        var security = WindowsAuthenticatedPipeStreams.CreateSecurity("S-1-5-5-10-20", userSid!);
        await using var server = WindowsAuthenticatedPipeStreams.CreateServer(pipeName, security, firstInstance: true, maximumInstances: 1);
        using var exchangeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Prove the same GUID pipe and DACL accept the process's ordinary token.
        await ExchangeWithCurrentTokenAsync(server, pipeName, exchangeTimeout.Token, 0x31);
        Assert.False(server.IsConnected);

        using var restrictedToken = CreateRestrictedToken(currentIdentity.AccessToken,
            new SecurityIdentifier(WellKnownSidType.WorldSid, null));
        var deniedError = await WindowsIdentity.RunImpersonatedAsync(restrictedToken,
            () => ConnectUnderRestrictedTokenAsync(pipeName));

        Assert.Equal(5, deniedError); // ERROR_ACCESS_DENIED from the OS pipe ACL check.
        Assert.Null(WindowsIdentity.GetCurrent(true));
        using (var revertedIdentity = WindowsIdentity.GetCurrent())
        {
            Assert.Equal(userSid, revertedIdentity.User?.Value);
        }

        // A second ordinary exchange proves the failed impersonated attempt did
        // not poison the pipe instance or leave a restricted thread token set.
        await ExchangeWithCurrentTokenAsync(server, pipeName, exchangeTimeout.Token, 0x32);
        Assert.False(server.IsConnected);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> ConnectUnderRestrictedTokenAsync(string pipeName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await using var client = await WindowsAuthenticatedPipeStreams.ConnectAsync(pipeName, timeout.Token);
            return 0;
        }
        catch (Win32Exception error)
        {
            return error.NativeErrorCode;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task ExchangeWithCurrentTokenAsync(
        NamedPipeServerStream server,
        string pipeName,
        CancellationToken cancellationToken,
        byte payload)
    {
        var waiting = server.WaitForConnectionAsync(cancellationToken);
        await using (var client = await WindowsAuthenticatedPipeStreams.ConnectAsync(pipeName, cancellationToken))
        {
            await waiting;
            var received = new byte[1];
            var reading = server.ReadExactlyAsync(received, cancellationToken).AsTask();
            await client.WriteAsync(new[] { payload }, cancellationToken);
            await reading;
            Assert.Equal(payload, received[0]);
        }

        if (server.IsConnected) server.Disconnect();
    }

    [SupportedOSPlatform("windows")]
    private static SafeAccessTokenHandle CreateRestrictedToken(
        SafeAccessTokenHandle sourceToken,
        SecurityIdentifier restrictingSid)
    {
        var sidBytes = new byte[restrictingSid.BinaryLength];
        restrictingSid.GetBinaryForm(sidBytes, 0);
        var sidPointer = Marshal.AllocHGlobal(sidBytes.Length);
        var sidAndAttributesPointer = IntPtr.Zero;
        SafeAccessTokenHandle? restrictedToken = null;
        try
        {
            Marshal.Copy(sidBytes, 0, sidPointer, sidBytes.Length);
            sidAndAttributesPointer = Marshal.AllocHGlobal(Marshal.SizeOf<SidAndAttributes>());
            // CreateRestrictedToken requires Attributes = 0 for restricting SIDs;
            // the OS treats every restriction SID as enabled during access checks.
            Marshal.StructureToPtr(new SidAndAttributes(sidPointer, 0), sidAndAttributesPointer, false);

            if (!CreateRestrictedToken(sourceToken, 0, 0, IntPtr.Zero, 0, IntPtr.Zero,
                    1, sidAndAttributesPointer, out restrictedToken))
            {
                var error = Marshal.GetLastWin32Error();
                restrictedToken?.Dispose();
                throw new Win32Exception(error, $"CreateRestrictedToken failed for the pipe ACL test (Win32 {error}).");
            }

            return restrictedToken;
        }
        finally
        {
            if (sidAndAttributesPointer != IntPtr.Zero) Marshal.FreeHGlobal(sidAndAttributesPointer);
            Marshal.FreeHGlobal(sidPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SidAndAttributes(IntPtr sid, uint attributes)
    {
        public readonly IntPtr Sid = sid;
        public readonly uint Attributes = attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existingTokenHandle,
        uint flags,
        uint disableSidCount,
        IntPtr sidsToDisable,
        uint deletePrivilegeCount,
        IntPtr privilegesToDelete,
        uint restrictedSidCount,
        IntPtr sidsToRestrict,
        out SafeAccessTokenHandle newTokenHandle);
}
