using System.ComponentModel;
using FocusGuard.Client;
using FocusGuard.Contracts;

namespace FocusGuard.Desktop.Tests;

public sealed class ServiceHealthProbeTests
{
    [Fact]
    public async Task BootstrapFailuresAreReportedInsteadOfCrashingBeforeHealthCheck()
    {
        var result = await ServiceHealthProbe.RunAsync(() => throw new UnauthorizedAccessException(
            "Only the provisioned owner can use IPC."), CancellationToken.None);
        Assert.Equal(11, result.ExitCode);
        Assert.Equal("Bootstrap", result.Stage);
        Assert.Equal("OwnerDenied", result.Failure!.Code);
    }

    [Fact]
    public async Task ExchangeFailurePreservesInnerNativeError()
    {
        var result = await ServiceHealthProbe.RunAsync(() => new FailingClient(), CancellationToken.None);
        Assert.Equal(12, result.ExitCode);
        Assert.Equal("Exchange", result.Stage);
        Assert.Equal(5, result.Failure!.NativeError);
    }

    private sealed class FailingClient : IFocusControlClient
    {
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) =>
            ValueTask.FromException<StatusResponse>(new IOException("failure", new Win32Exception(5)));
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
