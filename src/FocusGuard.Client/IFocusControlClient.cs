using FocusGuard.Contracts;

namespace FocusGuard.Client;

public interface IFocusControlClient
{
    ValueTask<TodayResponse> GetTodayAsync(TodayRequest request, CancellationToken token) => ValueTask.FromResult(new TodayResponse(0, 0, []));
    ValueTask<ScheduleResponse> GetSchedulesAsync(CancellationToken token) => ValueTask.FromResult(new ScheduleResponse(false, "Unavailable", []));
    ValueTask<ScheduleResponse> ChangeScheduleAsync(ScheduleChangeRequest request, CancellationToken token) => ValueTask.FromResult(new ScheduleResponse(false, "Unavailable", []));
    ValueTask<PrepareSessionResponse> PrepareAsync(
        PrepareSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
        ActivatePreparedSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<SkipBreakResponse> SkipBreakAsync(
        SkipBreakRequest request,
        CancellationToken cancellationToken);

    ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<ActivateSessionResponse> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken);

    ValueTask<ActivityResponse> GetActivityAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    ValueTask<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);

    ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        Guid? sessionId,
        CancellationToken cancellationToken);

    ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
        ResolveCapturesRequest request,
        CancellationToken cancellationToken);
}
