using System.IO.Pipes;
using FocusGuard.Contracts;
using FocusGuard.Desktop;

namespace FocusGuard.Desktop.Tests;

public sealed class FocusControlClientTests
{
    [Fact]
    public async Task ClientRejectsWrongCorrelation()
    {
        var pipeName = $"FocusGuard.Tests.DesktopControl.{Guid.NewGuid():N}";
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            _ = await BoundedPipeFraming.ReadAsync(server, CancellationToken.None);
            var response = IpcProtocol.CreateStatusResponse(
                Guid.NewGuid(),
                new StatusResponse(
                    true,
                    false,
                    "Inactive",
                    0,
                    null,
                    null,
                    null,
                    null,
                    StrictMode: false,
                    EnforcementConnected: false)).Value!;
            await BoundedPipeFraming.WriteAsync(
                server,
                IpcProtocol.WriteEnvelope(response),
                CancellationToken.None);
        });
        var client = new NamedPipeFocusControlClient(pipeName, TimeSpan.FromSeconds(2));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await client.GetStatusAsync(CancellationToken.None));
        await serverTask;

        Assert.Contains("correlation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientTimesOutWhenPipeIsMissing()
    {
        var client = new NamedPipeFocusControlClient(
            $"FocusGuard.Tests.Missing.{Guid.NewGuid():N}",
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.GetStatusAsync(CancellationToken.None));
    }
}
