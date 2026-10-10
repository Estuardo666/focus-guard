using FocusGuard.Client;
using FocusGuard.Contracts;

namespace FocusGuard.Desktop.Tests;

public sealed class ServiceHealthCheckTests
{
    [Fact]
    public async Task RunAsyncRequiresEveryConcurrentAuthoritativeProbeToBeReady()
    {
        var healthy = new ProbeClient(serviceReady: true);
        var unhealthy = new ProbeClient(serviceReady: true, failAtProbe: 5);

        Assert.True(await ServiceHealthCheck.RunAsync(healthy, CancellationToken.None));
        Assert.False(await ServiceHealthCheck.RunAsync(unhealthy, CancellationToken.None));
        Assert.Equal(8, healthy.ProbeCount);
        Assert.Equal(8, unhealthy.ProbeCount);
    }

    private sealed class ProbeClient(bool serviceReady, int failAtProbe = -1) : IFocusControlClient
    {
        private int _probeCount;

        public int ProbeCount => _probeCount;

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probe = Interlocked.Increment(ref _probeCount);
            return ValueTask.FromResult(new StatusResponse(
                serviceReady && probe != failAtProbe,
                false,
                "Inactive",
                0,
                null,
                null,
                null,
                null,
                false,
                false));
        }

        public ValueTask<PrepareSessionResponse> PrepareAsync(
            PrepareSessionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
            ActivatePreparedSessionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<SkipBreakResponse> SkipBreakAsync(
            SkipBreakRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ActivateSessionResponse> ActivateAsync(
            ActivateSessionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<StopSessionResponse> StopAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ActivityResponse> GetActivityAsync(
            int maximumCount,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CaptureResponse> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
            Guid? sessionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
            ResolveCapturesRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
