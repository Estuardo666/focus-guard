using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class WindowsPipeIdentityTests
{
    [Fact]
    public async Task RealPipeCapturesNativeIdentityAndRevertsIdentification()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "FocusGuard.Tests.PeerIdentity." + Guid.NewGuid();
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        var connected = server.WaitForConnectionAsync(cancellation.Token);
        await client.ConnectAsync(cancellation.Token);
        await connected;
        var claims = System.Text.Encoding.UTF8.GetBytes("{\"pid\":1,\"sid\":\"S-1-5-18\",\"role\":\"Desktop\"}");
        var data = new byte[claims.Length];
        var reading = server.ReadExactlyAsync(data, cancellation.Token).AsTask();
        await client.WriteAsync(claims, cancellation.Token);
        await reading;
        using var observed = WindowsPipeIdentity.CaptureClient(server);
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        Assert.Equal((uint)process.Id, observed.ProcessId);
        Assert.NotEqual(1u, observed.ProcessId);
        Assert.NotEqual("S-1-5-18", observed.UserSid);
        Assert.Equal(identity.User!.Value, observed.UserSid);
        Assert.Equal((uint)process.SessionId, observed.SessionId);
        Assert.Equal(TimeSpan.Zero, observed.StartedUtc.Offset);
        Assert.StartsWith("S-1-5-5-", observed.LogonSid, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), observed.ImagePath);
        Assert.Equal((uint)process.Id, WindowsPipeIdentity.ServerProcessId(client));
        var realService = WindowsServiceIdentity.Read("RpcSs");
        // A valid Windows service registration cannot authenticate this unrelated pipe server.
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            IpcServerAuthentication.Verify(client, new("RpcSs", realService.ImagePath, new string('A', 64)));
        });
        Assert.Null(WindowsIdentity.GetCurrent(true));
    }

    [Fact]
    public void MissingServiceIdentityFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            WindowsServiceIdentity.Read("FocusGuard.Tests.Missing." + Guid.NewGuid());
        });
    }

    [Fact]
    public void ReadsExistingWindowsServiceWithoutChangingItsState()
    {
        if (!OperatingSystem.IsWindows()) return;
        // RpcSs is a standard Windows service; only QUERY_CONFIG/QUERY_STATUS rights are requested.
        var first = WindowsServiceIdentity.Read("RpcSs");
        var second = WindowsServiceIdentity.Read("RpcSs");
        Assert.Equal(first, second);
        Assert.NotEqual(0u, first.ProcessId);
        Assert.Equal(4u, first.State);
        Assert.True(Path.IsPathFullyQualified(first.ImagePath));
    }
}
