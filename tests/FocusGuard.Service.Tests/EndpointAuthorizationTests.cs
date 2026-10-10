using System.IO.Pipes;
using FocusGuard.Contracts;

namespace FocusGuard.Service.Tests;

public sealed class EndpointAuthorizationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectedChannelHeartbeatCannotRenewItsLease(bool agent)
    {
        var name = "FocusGuard.Tests.ChannelAuthorization." + Guid.NewGuid();
        var relay = new ProcessAgentPolicyRelay();
        var coordinator = new PolicyDistributionCoordinator(relay);
        var focus = new InMemoryFocusService(policyDistribution: coordinator);
        var browser = new ExtensionHealthState();
        var security = new TestSecurity();
        using Microsoft.Extensions.Hosting.BackgroundService server = agent
            ? new ProcessAgentPipeService(relay, coordinator, focus, new EmptyFocusAuditStore(), pipeName: name, security: security)
            : new ExtensionHealthPipeService(browser, focus, coordinator, pipeName: name, security: security);
        await server.StartAsync(default);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(timeout.Token);
            var request = agent ? ProcessAgentProtocol.WriteRequest(new(1, ProcessAgentProtocol.HeartbeatType,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow))
                : ExtensionHealthProtocol.WriteRequest(new(1, ExtensionHealthProtocol.HeartbeatType, Guid.NewGuid(), DateTimeOffset.UtcNow));
            await BoundedPipeFraming.WriteAsync(pipe, request, timeout.Token);
            Assert.Null(await BoundedPipeFraming.ReadAsync(pipe, timeout.Token));
            Assert.False(relay.IsReady());
            Assert.False(browser.IsExtensionReady(DateTimeOffset.UtcNow));
            Assert.False(server.ExecuteTask!.IsFaulted);
            Assert.Equal(1, security.FirstInstanceCalls);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task RejectedControlOperationCannotStopTheAuthoritativeSessionAndListenerSurvives()
    {
        var name = "FocusGuard.Tests.ControlAuthorization." + Guid.NewGuid();
        var focus = new InMemoryFocusService();
        var started = await focus.ActivateAsync(new(Guid.NewGuid(), 300, false, 0), default);
        var security = new TestSecurity();
        using var server = new FocusControlPipeService(focus, name, security: security);
        await server.StartAsync(default);
        try
        {
            await using (var denied = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await denied.ConnectAsync(timeout.Token);
                await BoundedPipeFraming.WriteAsync(denied, IpcProtocol.WriteEnvelope(
                    IpcProtocol.CreateStopSessionRequest(Guid.NewGuid()).Value!), timeout.Token);
                Assert.Null(await BoundedPipeFraming.ReadAsync(denied, timeout.Token));
            }
            var status = await focus.GetStatusAsync(default);
            Assert.Equal(started.Value!.SessionId, status.SessionId);
            Assert.Equal("Active", status.SessionState);
            Assert.True(security.ControlChecks > 0);
            Assert.Equal(1, security.FirstInstanceCalls);
            await using var allowed = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await allowed.ConnectAsync(limit.Token);
            await BoundedPipeFraming.WriteAsync(allowed, IpcProtocol.WriteEnvelope(IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!), limit.Token);
            var response = IpcProtocol.ReadEnvelope((await BoundedPipeFraming.ReadAsync(allowed, limit.Token))!);
            Assert.True(IpcProtocol.ReadStatusResponse(response.Value!).Value!.ServiceReady);
        }
        finally { await server.StopAsync(default); }
    }

    // Exercises real endpoint dispatch ordering and error containment. Native caller policy is independently tested.
    private sealed class TestSecurity : IIpcServerSecurity
    {
        public int FirstInstanceCalls;
        public int ControlChecks;
        public NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances)
        {
            if (firstInstance) FirstInstanceCalls++;
            return new(name, PipeDirection.InOut, maximumInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }
        public void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType)
        {
            ControlChecks++;
            if (requestType != IpcMessageType.StatusRequest) throw new UnauthorizedAccessException("Test role cannot mutate.");
        }
        public void AuthorizeAgent(NamedPipeServerStream pipe, string requestType) => throw new UnauthorizedAccessException();
        public void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType) => throw new UnauthorizedAccessException();
    }
}
