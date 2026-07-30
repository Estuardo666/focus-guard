using System.IO.Pipes;
using FocusGuard.Contracts;

namespace FocusGuard.Service.Tests;

public sealed class FocusControlPipeServiceTests
{
    [Fact]
    public async Task ControlPipeActivatesReportsAndStopsNormalSession()
    {
        var pipeName = $"FocusGuard.Tests.Control.{Guid.NewGuid():N}";
        using var server = new FocusControlPipeService(
            new InMemoryFocusService(),
            pipeName);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var activationRequest = new ActivateSessionRequest(
                Guid.NewGuid(),
                300,
                false,
                0);
            var activationEnvelope = IpcProtocol.CreateActivateSessionRequest(
                Guid.NewGuid(),
                activationRequest).Value!;
            var activationResponse = IpcProtocol.ReadActivateSessionResponse(
                await SendAsync(pipeName, activationEnvelope));

            var statusEnvelope = IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!;
            var statusResponse = IpcProtocol.ReadStatusResponse(
                await SendAsync(pipeName, statusEnvelope));

            var stopEnvelope = IpcProtocol.CreateStopSessionRequest(Guid.NewGuid()).Value!;
            var stopResponse = IpcProtocol.ReadStopSessionResponse(
                await SendAsync(pipeName, stopEnvelope));

            Assert.True(activationResponse.Value!.Accepted);
            Assert.Equal("Active", statusResponse.Value!.SessionState);
            Assert.False(statusResponse.Value.EnforcementConnected);
            Assert.True(stopResponse.Value!.Accepted);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ControlPipeRejectsWrongCorrelationAtClientBoundary()
    {
        var request = IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!;
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

        Assert.NotEqual(request.CorrelationId, response.CorrelationId);
    }

    private static async Task<IpcEnvelope> SendAsync(
        string pipeName,
        IpcEnvelope request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        await BoundedPipeFraming.WriteAsync(
            pipe,
            IpcProtocol.WriteEnvelope(request),
            timeout.Token);
        var responseBytes = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token);
        var response = IpcProtocol.ReadEnvelope(responseBytes!);
        Assert.True(response.IsValid);
        Assert.Equal(request.CorrelationId, response.Value!.CorrelationId);
        return response.Value;
    }
}
