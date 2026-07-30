using FocusGuard.Contracts;
using FocusGuard.NativeMessaging;

namespace FocusGuard.NativeMessaging.Tests;

public sealed class ServiceHealthNativeMessageHandlerTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 7, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_DoesNotContactServiceForInvalidMessage()
    {
        var client = new RecordingClient();
        var handler = new ServiceHealthNativeMessageHandler(client, new FixedTimeProvider(NowUtc));

        var responseBytes = await handler.HandleAsync(
            """{"type":"activate"}"""u8.ToArray(),
            CancellationToken.None);
        var response = ExtensionHealthProtocol.ReadResponse(responseBytes);

        Assert.True(response.IsValid);
        Assert.Equal("invalidMessage", response.Value!.ErrorCode);
        Assert.False(response.Value.ServiceReady);
        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData(Failure.Timeout, "serviceTimeout")]
    [InlineData(Failure.Disconnected, "serviceDisconnected")]
    public async Task HandleAsync_ReturnsDegradedStatusForServiceFailure(
        Failure failure,
        string expectedError)
    {
        var client = new RecordingClient(failure);
        var handler = new ServiceHealthNativeMessageHandler(client, new FixedTimeProvider(NowUtc));
        var request = CreateRequest();

        var responseBytes = await handler.HandleAsync(
            ExtensionHealthProtocol.WriteRequest(request),
            CancellationToken.None);
        var response = ExtensionHealthProtocol.ReadResponse(responseBytes);

        Assert.True(response.IsValid);
        Assert.Equal(expectedError, response.Value!.ErrorCode);
        Assert.Equal(request.CorrelationId, response.Value.CorrelationId);
        Assert.False(response.Value.ServiceReady);
    }

    [Fact]
    public async Task HandleAsync_PropagatesCallerCancellation()
    {
        var handler = new ServiceHealthNativeMessageHandler(
            new RecordingClient(Failure.WaitForCancellation),
            new FixedTimeProvider(NowUtc));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handler.HandleAsync(
                ExtensionHealthProtocol.WriteRequest(CreateRequest()),
                cancellation.Token));
    }

    private static ExtensionHealthRequest CreateRequest() => new(
        1,
        "heartbeat",
        Guid.NewGuid(),
        NowUtc);

    public enum Failure
    {
        None,
        Timeout,
        Disconnected,
        WaitForCancellation
    }

    private sealed class RecordingClient(Failure failure = Failure.None) : IExtensionHealthClient
    {
        public int CallCount { get; private set; }

        public async ValueTask<ExtensionHealthResponse> QueryAsync(
            ExtensionHealthRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            switch (failure)
            {
                case Failure.Timeout:
                    throw new TimeoutException();
                case Failure.Disconnected:
                    throw new IOException();
                case Failure.WaitForCancellation:
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    break;
            }

            return new ExtensionHealthResponse(
                1,
                "healthStatus",
                request.CorrelationId,
                Guid.NewGuid(),
                true,
                true,
                "Inactive",
                0,
                NowUtc,
                null);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
