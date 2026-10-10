using System.Buffers.Binary;
using System.IO.Pipes;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service.Tests;

public sealed class ProcessAgentPipeServiceTests
{
    [Fact]
    public async Task ServiceInstanceIsStableAcrossConnections()
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        try
        {
            var first = await fixture.HeartbeatAsync();
            var second = await fixture.HeartbeatAsync();
            Assert.NotEqual(Guid.Empty, first.ServiceInstanceId);
            Assert.Equal(first.ServiceInstanceId, second.ServiceInstanceId);
        }
        finally { await fixture.StopAsync(); }
    }

    [Fact]
    public async Task GenericHostSurvivesMalformedFrame()
    {
        using var fixture = new ListenerFixture();
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddSingleton<IHostedService>(fixture.Server))
            .Build();
        await host.StartAsync();
        try
        {
            await using (var badClient = await fixture.ConnectAsync())
                await badClient.WriteAsync(Header(0));
            Assert.True((await fixture.HeartbeatAsync()).ServiceReady);
            Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public async Task ExactPolicyAcknowledgementAndSanitizedAuditRemainValid()
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        try
        {
            var activation = await fixture.FocusService.ActivateAsync(new ActivateSessionRequest(
                Guid.NewGuid(), 300, false, 0,
                ApplicationRules: [new(Guid.NewGuid(), "Block", "FileName", "target.exe")],
                WebsiteRules: [new(Guid.NewGuid(), "Block", "example.com")],
                EnforcementEnabled: true), default);
            Assert.True(activation.IsValid, string.Join("; ", activation.Errors));
            Assert.True(activation.Value!.Accepted);
            var initial = await fixture.HeartbeatAsync();
            Assert.NotNull(initial.Policy);
            Assert.False(initial.EnforcementConfirmed);
            var confirmed = await fixture.HeartbeatAsync(initial.Policy);
            Assert.True(confirmed.EnforcementConfirmed);
            // Browser is unconfirmed: process audit authorization remains channel-specific.
            Assert.False((await fixture.FocusService.GetStatusAsync(default)).EnforcementConnected);
            var audited = await fixture.HeartbeatAsync(initial.Policy, "target.exe");
            Assert.True(audited.EnforcementConfirmed);
            var audit = Assert.Single(fixture.Audits.Events);
            Assert.Equal("target.exe", audit.Target);
            Assert.Equal(activation.Value.SessionId, audit.SessionId);
            Assert.Equal("Active", audited.SessionState);
        }
        finally { await fixture.StopAsync(); }
    }

    [Fact]
    public async Task ResponseWriteSharesTheRequestDeadline()
    {
        using var fixture = new ListenerFixture();
        var request = new ProcessAgentRequest(ProcessAgentProtocol.CurrentVersion,
            ProcessAgentProtocol.HeartbeatType, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using var stream = new BlockedWriteStream(ProcessAgentProtocol.WriteRequest(request));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Server.HandleConnectionAsync(stream, default).WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.True(stream.WriteAttempted);
    }

    [Fact]
    public async Task FirstHeartbeatAfterCycleBoundaryCarriesBreakPolicy()
    {
        var clock = new PhaseClock(DateTimeOffset.UtcNow);
        using var fixture = new ListenerFixture(clock);
        await fixture.Server.StartAsync(default);
        try
        {
            var prepared = await fixture.FocusService.PrepareSessionAsync(new PrepareSessionRequest(
                Guid.NewGuid(), "Cycles", SessionMode.FocusCycle, 0,
                new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0,
                ApplicationRules: [new(Guid.NewGuid(), "Block", "FileName", "target.exe")],
                WebsiteRules: [], EnforcementEnabled: true,
                BreakApplicationRules: [], BreakWebsiteRules: []), default);
            Assert.True(prepared.IsValid);
            await fixture.FocusService.ActivatePreparedSessionAsync(
                new(prepared.Value!.PreparationId!.Value), default);
            var focus = await fixture.HeartbeatAsync();
            Assert.NotNull(focus.Policy);
            Assert.True((await fixture.HeartbeatAsync(focus.Policy)).EnforcementConfirmed);
            clock.Advance(TimeSpan.FromSeconds(70));
            var firstBreak = await fixture.HeartbeatAsync(focus.Policy);
            Assert.True(firstBreak.Policy!.Policy.IsAuthorizedBreak(clock.GetUtcNow()));
            Assert.NotEqual(focus.Policy.PolicyId, firstBreak.Policy.PolicyId);
            Assert.False(firstBreak.EnforcementConfirmed);
            Assert.True((await fixture.HeartbeatAsync(firstBreak.Policy)).EnforcementConfirmed);
        }
        finally { await fixture.StopAsync(); }
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("oversized")]
    [InlineData("truncated-header")]
    [InlineData("truncated-payload")]
    [InlineData("invalid-json")]
    [InlineData("invalid-request")]
    public async Task InvalidClientDoesNotFaultListenerAndNextHeartbeatSucceeds(string kind)
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        try
        {
            await using (var badClient = await fixture.ConnectAsync())
            {
                var frame = kind switch
                {
                    "zero" => Header(0),
                    "oversized" => Header(ProcessAgentProtocol.MaximumMessageBytes + 1),
                    "truncated-header" => new byte[] { 1, 0 },
                    "truncated-payload" => Header(10).Concat(new byte[] { 1 }).ToArray(),
                    "invalid-json" => Header(1).Concat(new byte[] { (byte)'{' }).ToArray(),
                    _ => Header(2).Concat("{}"u8.ToArray()).ToArray()
                };
                await badClient.WriteAsync(frame);
                await badClient.FlushAsync();
            }
            var response = await fixture.HeartbeatAsync();
            Assert.True(response.ServiceReady);
            Assert.False(fixture.Server.ExecuteTask!.IsFaulted);
        }
        finally { await fixture.StopAsync(); }
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("header")]
    [InlineData("payload")]
    public async Task StalledClientReleasesTheOnlySlotBeforeNextHeartbeatDeadline(string kind)
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        try
        {
            await using var stalled = await fixture.ConnectAsync();
            if (kind == "header") await stalled.WriteAsync(new byte[] { 1 });
            if (kind == "payload") await stalled.WriteAsync(Header(10).Concat(new byte[] { 1 }).ToArray());
            await stalled.FlushAsync();
            var response = await fixture.HeartbeatAsync();
            Assert.True(response.ServiceReady);
            Assert.False(fixture.Server.ExecuteTask!.IsFaulted);
        }
        finally { await fixture.StopAsync(); }
    }

    [Fact]
    public async Task ShutdownWhileWaitingForConnectionIsGraceful()
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        await fixture.StopAsync();
        // BackgroundService may cancel its scheduled task before ExecuteAsync starts.
        Assert.True(fixture.Server.ExecuteTask!.IsCompleted);
        Assert.False(fixture.Server.ExecuteTask.IsFaulted);
    }

    [Fact]
    public async Task ShutdownInterruptsConnectedReadWithoutWaitingForRequestDeadline()
    {
        using var fixture = new ListenerFixture();
        await fixture.Server.StartAsync(default);
        await using var stalled = await fixture.ConnectAsync();
        await fixture.StopAsync();
        Assert.True(fixture.Server.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static byte[] Header(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)length);
        return header;
    }

    private sealed class ListenerFixture : IDisposable
    {
        private readonly string _name = "FocusGuard.Tests.AgentListener." + Guid.NewGuid().ToString("N");
        public ProcessAgentPipeService Server { get; }
        public InMemoryFocusService FocusService { get; }
        public RecordingAuditStore Audits { get; } = new();
        public ListenerFixture(TimeProvider? clock = null)
        {
            Clock = clock ?? TimeProvider.System;
            var relay = new ProcessAgentPolicyRelay(Clock);
            var coordinator = new PolicyDistributionCoordinator(relay, Clock);
            FocusService = new InMemoryFocusService(Clock, policyDistribution: coordinator, auditStore: Audits, processReadyProvider: relay.IsReady);
            Server = new ProcessAgentPipeService(relay, coordinator, FocusService, Audits, Clock, _name);
        }
        private TimeProvider Clock { get; }
        public async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token = default)
        {
            var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(TimeSpan.FromSeconds(8), token); return pipe; }
            catch { await pipe.DisposeAsync(); throw; }
        }
        public async Task<ProcessAgentResponse> HeartbeatAsync(PolicyDelivery? held = null, string? executable = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await using var client = await ConnectAsync(timeout.Token);
            var id = Guid.NewGuid();
            var request = new ProcessAgentRequest(ProcessAgentProtocol.CurrentVersion,
                executable is null ? ProcessAgentProtocol.HeartbeatType : ProcessAgentProtocol.AuditType,
                id, Guid.NewGuid(), Clock.GetUtcNow(), held?.PolicyId, held?.PolicyHash,
                executable, executable is null ? null : "Terminated");
            await BoundedPipeFraming.WriteAsync(client, ProcessAgentProtocol.WriteRequest(request), timeout.Token);
            var payload = await BoundedPipeFraming.ReadAsync(client, timeout.Token);
            var response = ProcessAgentProtocol.ReadResponse(payload!);
            Assert.True(response.IsValid, string.Join("; ", response.Errors));
            Assert.Equal(id, response.Value!.CorrelationId);
            return response.Value;
        }
        public async Task StopAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Server.StopAsync(timeout.Token);
        }
        public void Dispose() => Server.Dispose();
    }

    private sealed class RecordingAuditStore : IFocusAuditStore
    {
        public List<FocusAuditEvent> Events { get; } = [];
        public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusAuditEvent>>(Events.Take(maximumCount).ToArray());
    }

    private sealed class BlockedWriteStream : MemoryStream
    {
        public bool WriteAttempted { get; private set; }
        public BlockedWriteStream(byte[] payload)
        {
            Write(Header(payload.Length));
            Write(payload);
            Position = 0;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteAttempted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class PhaseClock(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
