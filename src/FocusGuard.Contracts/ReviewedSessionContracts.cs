namespace FocusGuard.Contracts;

public sealed record ReviewedStopSessionRequest(Guid SessionId);
public sealed record ReviewedSkipBreakRequest(Guid SessionId, int PhaseIndex);

public static partial class IpcProtocol
{
    public static IpcCodecResult<IpcEnvelope> CreateReviewedStopRequest(Guid correlationId, ReviewedStopSessionRequest request) =>
        request is not null && request.SessionId != Guid.Empty
            ? CreateEnvelope(IpcMessageType.ReviewedStopSessionRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure("Reviewed stop identity is invalid.");

    public static IpcCodecResult<ReviewedStopSessionRequest> ReadReviewedStopRequest(IpcEnvelope envelope) =>
        ReadPayload<ReviewedStopSessionRequest>(envelope, IpcMessageType.ReviewedStopSessionRequest, ["sessionId"],
            request => request is not null && request.SessionId != Guid.Empty
                ? IpcValidationResult.Success : new(["Reviewed stop identity is invalid."]));

    public static IpcCodecResult<IpcEnvelope> CreateReviewedSkipRequest(Guid correlationId, ReviewedSkipBreakRequest request) =>
        request is not null && request.SessionId != Guid.Empty && request.PhaseIndex is >= 0 and <= 48
            ? CreateEnvelope(IpcMessageType.ReviewedSkipBreakRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure("Reviewed skip identity or phase is invalid.");

    public static IpcCodecResult<ReviewedSkipBreakRequest> ReadReviewedSkipRequest(IpcEnvelope envelope) =>
        ReadPayload<ReviewedSkipBreakRequest>(envelope, IpcMessageType.ReviewedSkipBreakRequest, ["sessionId", "phaseIndex"],
            request => request is not null && request.SessionId != Guid.Empty && request.PhaseIndex is >= 0 and <= 48
                ? IpcValidationResult.Success : new(["Reviewed skip identity or phase is invalid."]));
}
