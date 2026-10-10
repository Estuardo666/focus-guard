using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class WindowsAuthenticatedPipeStreamsTests
{
    [Fact]
    public void LogonClientAclCannotCreateServerInstances()
    {
        if (!OperatingSystem.IsWindows()) return;
        var security = WindowsAuthenticatedPipeStreams.CreateSecurity("S-1-5-5-10-20", "S-1-5-19");
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()
            .Select(rule =>
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                return new { Sid = rule.IdentityReference.Value, rule.AccessControlType, rule.PipeAccessRights };
            }).ToArray();
        var client = Assert.Single(rules, rule => rule.Sid == "S-1-5-5-10-20");
        Assert.Equal(AccessControlType.Allow, client.AccessControlType);
        Assert.Equal(0, (int)(client.PipeAccessRights & PipeAccessRights.CreateNewInstance));
        Assert.Equal((PipeAccessRights)WindowsAuthenticatedPipeStreams.ClientAccessMask, client.PipeAccessRights);
        Assert.DoesNotContain(rules, rule => rule.Sid is "S-1-5-11" or "S-1-1-0");
    }

    [Fact]
    public async Task NativeLocalStreamsExchangeWithIdentificationAndPreventSecondFirstInstance()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "FocusGuard.Tests.SecureStreams." + Guid.NewGuid();
        using var identity = WindowsIdentity.GetCurrent();
        // Test server runs under the current token, with real ACL; production server remains LocalService.
        var security = WindowsAuthenticatedPipeStreams.CreateSecurity("S-1-5-5-10-20", identity.User!.Value);
        await using var server = WindowsAuthenticatedPipeStreams.CreateServer(name, security, true);
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var duplicate = WindowsAuthenticatedPipeStreams.CreateServer(name, security, true);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = server.WaitForConnectionAsync(cancellation.Token);
        await using var client = await WindowsAuthenticatedPipeStreams.ConnectAsync(name, cancellation.Token);
        await waiting;
        var payload = new byte[] { 1, 2, 3 };
        var received = new byte[3];
        var reading = server.ReadExactlyAsync(received, cancellation.Token).AsTask();
        await client.WriteAsync(payload, cancellation.Token);
        await reading;
        Assert.Equal(payload, received);
        using var peer = WindowsPipeIdentity.CaptureClient(server);
        Assert.Equal(identity.User.Value, peer.UserSid);
        Assert.Null(WindowsIdentity.GetCurrent(true));
    }

    [Fact]
    public async Task RejectedRemoteClientDoesNotCompleteTheServerAccept()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "FocusGuard.Tests.RemoteDenied." + Guid.NewGuid().ToString("N");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var security = WindowsAuthenticatedPipeStreams.CreateOwnerSecurity(sid, sid);
        await using var server = WindowsAuthenticatedPipeStreams.CreateServer(name, security, true, 1);
        using var acceptDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accept = server.WaitForConnectionAsync(acceptDeadline.Token);

        await using var client = new NamedPipeClientStream(
            "localhost", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var rejection = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ConnectAsync(connectDeadline.Token));

        Assert.Equal(unchecked((int)0x80070005), rejection.HResult);
        Assert.False(accept.IsCompleted);

        acceptDeadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept);
    }
}
