using FocusGuard.Contracts;

namespace FocusGuard.Client;

/// <summary>Session-directed controls never fall back to unscoped legacy commands.</summary>
public interface IReviewedSessionControlClient
{
    ValueTask<StopSessionResponse> StopReviewedAsync(ReviewedStopSessionRequest request, CancellationToken token);
    ValueTask<SkipBreakResponse> SkipReviewedAsync(ReviewedSkipBreakRequest request, CancellationToken token);
}

public sealed partial class NamedPipeFocusControlClient
{
    public async ValueTask<StopSessionResponse> StopReviewedAsync(ReviewedStopSessionRequest request, CancellationToken token)
    {
        var correlation = Guid.NewGuid();
        var encoded = IpcProtocol.CreateReviewedStopRequest(correlation, request);
        if (!encoded.IsValid) throw new InvalidDataException("Invalid reviewed stop request.");
        var response = await SendAsync(encoded.Value!, correlation, token).ConfigureAwait(false);
        var decoded = IpcProtocol.ReadStopSessionResponse(response);
        return decoded.IsValid ? decoded.Value! : throw new InvalidDataException("Invalid reviewed stop response.");
    }

    public async ValueTask<SkipBreakResponse> SkipReviewedAsync(ReviewedSkipBreakRequest request, CancellationToken token)
    {
        var correlation = Guid.NewGuid();
        var encoded = IpcProtocol.CreateReviewedSkipRequest(correlation, request);
        if (!encoded.IsValid) throw new InvalidDataException("Invalid reviewed skip request.");
        var response = await SendAsync(encoded.Value!, correlation, token).ConfigureAwait(false);
        var decoded = CycleIpcProtocol.ReadSkipBreakResponse(response);
        return decoded.IsValid ? decoded.Value! : throw new InvalidDataException("Invalid reviewed skip response.");
    }
}
