using System.IO.Pipes;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;

namespace FocusGuard.Client.Tests;

public sealed class ReviewedSessionControlTests
{
    [Fact]
    public async Task RealPipeRejectsStaleSessionAndPhaseBeforeStopSkipOrCachedSuccess()
    {
        var clock = new Clock();
        var service = new InMemoryFocusService(clock);
        var pipe = "FocusGuard.Tests.Fg196." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await server.StartAsync(cancellation.Token);
        try
        {
            var client = new NamedPipeFocusControlClient(pipe);
            async Task<Guid> Activate()
            {
                var prepared = await client.PrepareAsync(new(Guid.NewGuid(), "Directed cycle", SessionMode.FocusCycle, 0,
                    new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0, ApplicationRules: [], WebsiteRules: []), cancellation.Token);
                var started = await client.ActivatePreparedAsync(new(prepared.PreparationId!.Value), cancellation.Token);
                Assert.True(started.Accepted);
                return started.SessionId!.Value;
            }
            var firstId = await Activate();
            Assert.False((await client.SkipReviewedAsync(new(firstId, 0), cancellation.Token)).Accepted);
            clock.Advance(TimeSpan.FromMinutes(1));
            var before = await client.GetStatusAsync(cancellation.Token);
            Assert.False((await client.SkipReviewedAsync(new(Guid.NewGuid(), 1), cancellation.Token)).Accepted);
            Assert.Equal(before.EndUtc, (await client.GetStatusAsync(cancellation.Token)).EndUtc);
            var skipped = await client.SkipReviewedAsync(new(firstId, 1), cancellation.Token);
            Assert.True(skipped.Accepted);
            Assert.Equal(before.EndUtc!.Value.AddMinutes(-1), skipped.SessionEndUtc);
            Assert.Equal(skipped, await client.SkipReviewedAsync(new(firstId, 1), cancellation.Token));
            // A cached successful phase response must still reject a different session identity.
            Assert.False((await client.SkipReviewedAsync(new(Guid.NewGuid(), 1), cancellation.Token)).Accepted);
            Assert.True((await client.StopReviewedAsync(new(firstId), cancellation.Token)).Accepted);
            var secondId = await Activate();
            clock.Advance(TimeSpan.FromMinutes(1));
            var second = await client.GetStatusAsync(cancellation.Token);
            Assert.False((await client.StopReviewedAsync(new(firstId), cancellation.Token)).Accepted);
            Assert.False((await client.SkipReviewedAsync(new(firstId, 1), cancellation.Token)).Accepted);
            var unchanged = await client.GetStatusAsync(cancellation.Token);
            Assert.Equal(secondId, unchanged.SessionId);
            Assert.Equal(second.EndUtc, unchanged.EndUtc);
            Assert.Equal(second.CurrentPhase, unchanged.CurrentPhase);
            Assert.Equal("Active", unchanged.SessionState);
            Assert.True((await client.StopReviewedAsync(new(secondId), cancellation.Token)).Accepted);
            Assert.Equal("Stopped", (await client.GetStatusAsync(cancellation.Token)).SessionState);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task InvalidReviewedIdentityIsRejectedBeforeConnecting()
    {
        var client = new NamedPipeFocusControlClient("FocusGuard.Tests.Absent." + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await client.StopReviewedAsync(new(Guid.Empty), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await client.SkipReviewedAsync(new(Guid.NewGuid(), 49), CancellationToken.None));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionFailureDoesNotRetryOrFallBackToLegacyControl(bool skip)
    {
        var factory = new FailingFactory();
        var client = new NamedPipeFocusControlClient(connectionFactory: factory);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (skip) await client.SkipReviewedAsync(new(Guid.NewGuid(), 1), CancellationToken.None);
            else await client.StopReviewedAsync(new(Guid.NewGuid()), CancellationToken.None);
        });
        Assert.Equal(1, factory.Calls);
    }

    private sealed class FailingFactory : IPipeConnectionFactory
    {
        public int Calls { get; private set; }
        public ValueTask<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
        {
            Calls++;
            throw new IOException("Simulated unavailable service.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostReplyAfterRequestWasWrittenDoesNotReplay(bool skip)
    {
        var pipe = "FocusGuard.Tests.Fg196.Lost." + Guid.NewGuid().ToString("N");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(cancellation.Token);
            var bytes = await BoundedPipeFraming.ReadAsync(server, cancellation.Token);
            var envelope = IpcProtocol.ReadEnvelope(bytes!);
            Assert.Equal(skip ? IpcMessageType.ReviewedSkipBreakRequest : IpcMessageType.ReviewedStopSessionRequest, envelope.Value!.MessageType);
            // The request reached the peer; its reply is lost. The client must not send another command.
            server.Disconnect();
        }, cancellation.Token);
        var factory = new CountingFactory();
        var client = new NamedPipeFocusControlClient(pipe, connectionFactory: factory);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            if (skip) await client.SkipReviewedAsync(new(Guid.NewGuid(), 1), cancellation.Token);
            else await client.StopReviewedAsync(new(Guid.NewGuid()), cancellation.Token);
        });
        await serverTask;
        Assert.Equal(1, factory.Calls);
    }

    private sealed class CountingFactory : IPipeConnectionFactory
    {
        public int Calls { get; private set; }
        public async ValueTask<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
        {
            Calls++;
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(token); return pipe; }
            catch { pipe.Dispose(); throw; }
        }
    }
}
