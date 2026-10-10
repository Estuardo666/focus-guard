using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeCycleConfiguration(long FocusSeconds, long ShortBreakSeconds, long LongBreakSeconds,
    int CyclesBeforeLongBreak, string TerminationMode, int? CycleCount, long? TotalDurationSeconds);
internal sealed record BridgeCyclePreset(string Id, string Name, bool IsBuiltIn, BridgeCycleConfiguration Configuration, string Revision);
internal sealed record BridgeCyclePage(IReadOnlyList<BridgeCyclePreset> Items, int Offset, int Total, bool EditingEnabled);
internal sealed record BridgeCyclePhase(int Index, string Kind, int FocusCycleNumber, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
internal sealed record BridgeCycleReview(string PreparationId, string ProfileId, string ProfileName, string? PresetName,
    BridgeCycleConfiguration Configuration, bool EnforcementEnabled, int ApplicationRuleCount, int WebsiteRuleCount,
    int BreakApplicationRuleCount, int BreakWebsiteRuleCount, IReadOnlyList<string> BreakApplications,
    IReadOnlyList<string> BreakWebsites, bool RulesTruncated, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    DateTimeOffset ExpiresUtc, IReadOnlyList<BridgeCyclePhase> Phases, bool PartialFinalPhase, bool ExtensionReady);
internal sealed record OwnedBridgeCycleReview(string DocumentToken, BridgeCycleReview Review, Guid ProfileId,
    Guid? PresetId, FocusCycleConfiguration Configuration);

internal sealed partial class NativeBridgeDispatcher
{
    private OwnedBridgeCycleReview? _cycleReview;
    private async ValueTask<(string? Error, object? Result)> HandleCycleCommandAsync(BridgeRequest request, CancellationToken token)
    {
        if (profileStore is null || _workflow is null) return ("AuthorizationUnavailable", null);
        if (request.Method == "cycles.list")
        {
            var presets = await profileStore.ListCyclePresetsAsync(token).ConfigureAwait(false);
            if (presets.Count > 10000) return ("InvalidPresets", null);
            var items = presets.Skip(request.ProfileOffset).Take(8).Select(preset =>
                new BridgeCyclePreset(preset.Id.ToString("D"), preset.Name, preset.IsBuiltIn, CycleConfiguration(preset.Configuration), CyclePresetRevision.Compute(preset))).ToArray();
            return (null, new BridgeCyclePage(items, request.ProfileOffset, presets.Count,
                profileStore is IRevisionedCyclePresetStore && authorization.Authorize(HostControlOperation.WriteProfiles).State == HostAuthorizationState.Authorized));
        }
        if (request.Method == "session.prepareCycle")
        {
            _cycleReview = null; _normalReview = null;
            var status = await client.GetStatusAsync(token).ConfigureAwait(false);
            if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
            if (!CanStart(status)) return ("SessionAlreadyActive", null);
            var input = request.Cycle!;
            var review = await _workflow.PrepareSavedNormalCycleAsync(new(input.ProfileId, input.PresetId, input.Configuration, input.EnforcementEnabled), token).ConfigureAwait(false);
            if (!review.Preparation.Accepted) return (review.Preparation.Reason is "ProfileMissing" or "PresetMissing" or "BreakRulesInvalid" ? review.Preparation.Reason : "PreparationRejected", null);
            var prepared = review.Preparation; var config = review.Configuration!;
            var plan = FocusCyclePlanBuilder.Build(config, prepared.StartUtc!.Value).Value!;
            var result = new BridgeCycleReview(prepared.PreparationId!.Value.ToString("N"), input.ProfileId.ToString("D"), review.ProfileName!, review.PresetName,
                CycleConfiguration(config), input.EnforcementEnabled, review.ApplicationRuleCount, review.WebsiteRuleCount,
                review.BreakApplicationRuleCount, review.BreakWebsiteRuleCount,
                VisibleLabels(review.BreakApplications, out var breakAppTruncated),
                VisibleLabels(review.BreakWebsites, out var breakSiteTruncated),
                breakAppTruncated || breakSiteTruncated, prepared.StartUtc.Value, prepared.EndUtc!.Value,
                prepared.ExpiresUtc!.Value < prepared.EndUtc.Value ? prepared.ExpiresUtc.Value : prepared.EndUtc.Value,
                prepared.Phases.Select(phase => new BridgeCyclePhase(phase.Index, phase.Kind.ToString(), phase.FocusCycleNumber, phase.StartUtc, phase.EndUtc)).ToArray(),
                plan.HasPartialFinalPhase, status.ExtensionReady);
            token.ThrowIfCancellationRequested();
            if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
            _cycleReview = new(request.DocumentToken, result, input.ProfileId, input.PresetId, config); return (null, result);
        }
        if (_cycleReview is not { } owned || owned.DocumentToken != request.DocumentToken
            || owned.Review.PreparationId != request.PreparationId!.Value.ToString("N")) return ("PreparationUnavailable", null);
        if (_usedOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_usedOperationIds.Add(request.OperationId!.Value)) return ("DuplicateOperation", null);
        _cycleReview = null;
        var started = await _workflow.ActivatePreparedAsync(new(request.PreparationId.Value), token).ConfigureAwait(false);
        if (!started.Accepted) return ("ActivationRejected", null);
        if (started.Mode != SessionMode.FocusCycle || started.SessionId is not { } id || id == Guid.Empty
            || started.StartUtc != owned.Review.StartUtc || started.EndUtc != owned.Review.EndUtc) return ("OutcomeUnknown", null);
        var durationMinutes = Math.Clamp((int)Math.Ceiling((owned.Review.EndUtc - owned.Review.StartUtc).TotalMinutes), 1, 720);
        RememberAcceptedSession(id, owned.ProfileId, owned.Review.ProfileName, durationMinutes, SessionMode.FocusCycle,
            owned.PresetId, owned.Configuration, owned.Review.EnforcementEnabled, objective: request.Objective ?? string.Empty);
        return (null, new BridgeNormalStarted(id.ToString("D"), started.StartUtc.Value, started.EndUtc.Value));
    }
    private static BridgeCycleConfiguration CycleConfiguration(FocusCycleConfiguration config) =>
        new((long)config.FocusDuration.TotalSeconds, (long)config.ShortBreakDuration.TotalSeconds, (long)config.LongBreakDuration.TotalSeconds,
            config.CyclesBeforeLongBreak, config.TerminationMode.ToString(), config.CycleCount, (long?)config.TotalDuration?.TotalSeconds);
}
