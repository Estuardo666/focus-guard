using FocusGuard.Client;
using FocusGuard.Domain;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeSessionControlled(string SessionId, string Action);

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandleActiveSessionAsync(BridgeRequest request, CancellationToken token)
    {
        if (client is not IReviewedSessionControlClient directed) return ("ControlUnavailable", null);
        var input = request.ActiveSession!;
        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (status.SessionId != input.SessionId) return ("SessionChanged", null);
        if (status.StrictMode || status.SessionState is not ("Active" or "Activating")) return ("NormalSessionRequired", null);
        if (request.Method == "session.skipBreak" && (status.Mode != SessionMode.FocusCycle || status.CurrentPhase is not { } phase
            || phase.Index != input.PhaseIndex || phase.Kind is not (SessionPhaseKind.ShortBreak or SessionPhaseKind.LongBreak))) return ("BreakChanged", null);
        if (_usedOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_usedOperationIds.Add(request.OperationId!.Value)) return ("DuplicateOperation", null);
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        _normalReview = null; _cycleReview = null;
        // The service checks identity again under its mutation lock. Never use unscoped legacy controls.
        if (request.Method == "session.stopNormal")
        {
            var result = await directed.StopReviewedAsync(new(input.SessionId), token).ConfigureAwait(false);
            if (!result.Accepted) return ("ControlRejected", null);
        }
        else
        {
            var result = await directed.SkipReviewedAsync(new(input.SessionId, input.PhaseIndex!.Value), token).ConfigureAwait(false);
            if (!result.Accepted) return ("ControlRejected", null);
            if (result.PhaseIndex != input.PhaseIndex || result.SessionEndUtc is not { } end || end.Offset != TimeSpan.Zero
                || end <= status.StartUtc || end > status.EndUtc || !BridgeLimits.IsValidPhase(result.CurrentPhase)
                || result.CurrentPhase is { } next && (next.Index <= input.PhaseIndex || next.EndUtc > end)) return ("OutcomeUnknown", null);
        }
        return (null, new BridgeSessionControlled(input.SessionId.ToString("D"), request.Method == "session.stopNormal" ? "Stopped" : "BreakSkipped"));
    }
}
