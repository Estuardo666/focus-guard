using System.IO.Pipes;
using FocusGuard.Contracts;
using FocusGuard.Service;

namespace FocusGuard.ProcessAgent.Tests;

public sealed class NamedPipeProcessAgentClientTests
{
    [Fact]
    public void DefaultEndpointAndMutexFollowTheBuildFlavor()
    {
        Assert.Equal(ProcessAgentProtocol.PipeName, new NamedPipeProcessAgentClient().PipeName);
        Assert.Equal("Local\\" + ProcessAgentProtocol.PipeName, ProcessAgentStartupConfiguration.SingleInstanceName);
#if FOCUSGUARD_PRODUCTION
        Assert.Equal("FocusGuard.ProcessAgent.v1", ProcessAgentProtocol.PipeName);
#else
        Assert.Equal("FocusGuard.Development.ProcessAgent.v1", ProcessAgentProtocol.PipeName);
#endif
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("malformed")]
    [InlineData("correlation")]
    [InlineData("partial")]
    [InlineData("silent")]
    [InlineData("nonreading")]
    public async Task BadPeerCannotHoldOrForgeAnExchange(string kind)
    {
        var name = "FocusGuard.Tests.AgentClient." + Guid.NewGuid().ToString("N");
        var expectsTimeout = kind is "partial" or "silent" or "nonreading";
        // Peers that do answer get the production deadline so a busy thread pool (the full suite
        // runs in parallel) cannot turn their answer into a timeout; deadline cases stay short.
        var clientTimeout = expectsTimeout ? TimeSpan.FromMilliseconds(500) : NamedPipeProcessAgentClient.DefaultTimeout;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, kind == "nonreading" ? 1 : 4096, 4096);
        var peer = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellation.Token);
            if (kind == "nonreading") { await Task.Delay(Timeout.Infinite, cancellation.Token); return; }
            var payload = await BoundedPipeFraming.ReadAsync(server, cancellation.Token);
            var request = ProcessAgentProtocol.ReadRequest(payload!, DateTimeOffset.UtcNow).Value!;
            if (kind == "eof") { server.Disconnect(); return; }
            if (kind == "silent") { await Task.Delay(Timeout.Infinite, cancellation.Token); return; }
            if (kind == "partial") { await server.WriteAsync(new byte[] { 20, 0, 0, 0, (byte)'{' }, cancellation.Token); await Task.Delay(Timeout.Infinite, cancellation.Token); return; }
            if (kind == "malformed") { await BoundedPipeFraming.WriteAsync(server, "{"u8.ToArray(), cancellation.Token); return; }
            await BoundedPipeFraming.WriteAsync(server, ProcessAgentProtocol.WriteResponse(new(1,
                ProcessAgentProtocol.StatusResponseType, Guid.NewGuid(), Guid.NewGuid(), true, false,
                "Idle", 0, DateTimeOffset.UtcNow)), cancellation.Token);
        });
        try
        {
            var client = new NamedPipeProcessAgentClient(name, clientTimeout);
            var exchange = client.ExchangeAsync(Request(), default).AsTask();
            var exception = await Record.ExceptionAsync(() => exchange.WaitAsync(clientTimeout + TimeSpan.FromSeconds(3)));
            Assert.NotNull(exception);
            if (expectsTimeout) Assert.IsType<TimeoutException>(exception);
            else Assert.True(exception is InvalidDataException or IOException, $"Unexpected {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            cancellation.Cancel();
            try { await peer; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (InvalidDataException) when (cancellation.IsCancellationRequested) { }
        }
    }

    [Fact]
    public async Task AbsentServerTimesOutAndCallerCancellationRemainsCancellation()
    {
        var client = new NamedPipeProcessAgentClient("FocusGuard.Tests.Absent." + Guid.NewGuid(), TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<TimeoutException>(() => client.ExchangeAsync(Request(), default).AsTask());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExchangeAsync(Request(), canceled.Token).AsTask());
    }

    [Fact]
    public async Task RealServiceReconnectPreservesAuthoritativeSessionAndIndependentBrowserChannel()
    {
        var name = "FocusGuard.Tests.AgentReconnect." + Guid.NewGuid();
        var relay = new ProcessAgentPolicyRelay();
        var coordinator = new PolicyDistributionCoordinator(relay);
        var service = new InMemoryFocusService(policyDistribution: coordinator, processReadyProvider: relay.IsReady);
        using var listener = new ProcessAgentPipeService(relay, coordinator, service, new EmptyAuditStore(), pipeName: name);
        using var replacement = new ProcessAgentPipeService(relay, coordinator, service, new EmptyAuditStore(), pipeName: name);
        var receiver = new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver(confirmationLifetime: ProcessAgentConnection.ConfirmationLifetime);
        // Successful ticks are real pipe exchanges; the production deadline keeps them reliable
        // while the full suite loads the thread pool.
        var actor = new ProcessAgentConnection(new NamedPipeProcessAgentClient(name, NamedPipeProcessAgentClient.DefaultTimeout), receiver, new AgentAuditStore());
        await listener.StartAsync(default);
        try
        {
            var started = await service.ActivateAsync(new(Guid.NewGuid(), 300, false, 0,
                ApplicationRules: [new(Guid.NewGuid(), "Block", "FileName", "target.exe")],
                WebsiteRules: [new(Guid.NewGuid(), "Block", "example.com")], EnforcementEnabled: true), default);
            Assert.True(started.Value!.Accepted);
            for (var i = 0; i < 3; i++) Assert.True(await actor.TickAsync(default));
            Assert.NotNull(receiver.GetActiveDelivery());
            var policy = receiver.GetActiveDelivery()!;
            coordinator.ExchangeBrowserAcknowledgement(new(policy.PolicyId, policy.PolicyHash));
            var before = await service.GetStatusAsync(default);
            Assert.True(before.EnforcementConnected);
            await listener.StopAsync(default);
            Assert.False(await actor.TickAsync(default));
            Assert.Null(receiver.GetActiveDelivery());
            Assert.True(coordinator.IsBrowserConnected);
            await replacement.StartAsync(default);
            Assert.True(await actor.TickAsync(default));
            Assert.Null(receiver.GetActiveDelivery());
            Assert.True(await actor.TickAsync(default));
            Assert.NotNull(receiver.GetActiveDelivery());
            var after = await service.GetStatusAsync(default);
            Assert.Equal(before.SessionId, after.SessionId);
            Assert.Equal(before.EndUtc, after.EndUtc);
            Assert.True(coordinator.IsBrowserConnected);
        }
        finally { await listener.StopAsync(default); await replacement.StopAsync(default); }
    }

    private static ProcessAgentRequest Request() => new(1, ProcessAgentProtocol.HeartbeatType,
        Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    private sealed class EmptyAuditStore : IFocusAuditStore
    {
        public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusAuditEvent>>([]);
    }
}
