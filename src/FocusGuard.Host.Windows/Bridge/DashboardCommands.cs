using FocusGuard.Client.Workflows;
using FocusGuard.Client.Journal;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeCommandResponse(int Version, string RequestId, string DocumentToken,
    string? Error, object? Result);
internal sealed record BridgeProfileSummary(string Id, string Name, int ApplicationRuleCount,
    int WebsiteRuleCount, int BlockedApplicationCount, int BlockedWebsiteCount, string? AccentHex,
    string? DefaultCyclePresetId, string? DefaultCyclePresetName, BridgeCycleConfiguration? DefaultCyclePresetConfiguration);
internal sealed record BridgeProfilePage(IReadOnlyList<BridgeProfileSummary> Items, int Offset, int Total,
    bool PilotEnabled);
internal sealed record BridgeNormalReview(string PreparationId, string ProfileId, string ProfileName,
    int DurationSeconds, bool EnforcementEnabled, int ApplicationRuleCount, int WebsiteRuleCount,
    int BlockedApplicationCount, int BlockedWebsiteCount, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    DateTimeOffset ExpiresUtc, bool ExtensionReady);
internal sealed record BridgeNormalStarted(string SessionId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

internal sealed partial class NativeBridgeDispatcher
{
    private readonly SessionWorkflow? _workflow = profileStore is null ? null : new(client, profileStore, timeProvider);
    private readonly HashSet<Guid> _usedOperationIds = [];
    private (string DocumentToken, BridgeNormalReview Review)? _normalReview;

    private async ValueTask<(string? Error, object? Result)> HandleCommandAsync(
        BridgeRequest request, CancellationToken token)
    {
        if (request.ActiveSession is not null)
            return await HandleActiveSessionAsync(request, token).ConfigureAwait(false);
        if (request.CaptureAction is not null)
            return await HandleCaptureActionAsync(request, token).ConfigureAwait(false);
        if (request.Preferences is not null)
            return await HandlePreferenceCommandAsync(request, token).ConfigureAwait(false);
        if (request.SummarySessionId is not null)
            return await HandleSessionSummaryAsync(request, token).ConfigureAwait(false);
        if (request.SessionIntentId is not null)
            return await HandleSessionIntentAsync(request, token).ConfigureAwait(false);
        if (request.SessionReflection is not null)
            return await HandleSessionReflectionAsync(request, token).ConfigureAwait(false);
        if (request.StrictSession is not null || request.StrictActivation is not null
            || request.StrictCancelPreparationId is not null || request.StrictSaveRecoveryCodeId is not null || request.RecoveryInfo)
            return await HandleStrictSessionCommandAsync(request, token).ConfigureAwait(false);
        if (request.Schedule is not null)
            return await HandleScheduleCommandAsync(request, token).ConfigureAwait(false);
        if (request.CyclePreset is not null)
            return await HandleCyclePresetCommandAsync(request, token).ConfigureAwait(false);
        if (request.Method is "cycles.list" or "session.prepareCycle" or "session.startCycle")
            return await HandleCycleCommandAsync(request, token).ConfigureAwait(false);
        if (request.Method == "applications.list")
            return await ReadApplicationCatalogAsync(request, token).ConfigureAwait(false);
        if (request.Method is "today.get" or "schedules.list")
            return await HandleReadOnlyWorkflowAsync(request, token).ConfigureAwait(false);
        if (request.ProfileCommand is not null)
            return await HandleProfileCommandAsync(request, token).ConfigureAwait(false);
        if (_workflow is null || profileStore is null)
            return ("AuthorizationUnavailable", null);

        if (request.Method == "profiles.list")
        {
            var profiles = await profileStore.ListProfilesAsync(token).ConfigureAwait(false);
            if (profiles.Count > 10000 || profiles.Any(profile => profile.Id.Value == Guid.Empty
                || !Profile.ValidateName(profile.Name).IsValid))
                return ("InvalidProfiles", null);
            var items = new List<BridgeProfileSummary>();
            foreach (var profile in profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.Id.Value).Skip(request.ProfileOffset).Take(8))
            {
                var defaultPresetId = await profileStore.LoadDefaultCyclePresetIdAsync(profile.Id, token).ConfigureAwait(false);
                var preset = defaultPresetId is { } presetId
                    ? await profileStore.LoadCyclePresetAsync(presetId, token).ConfigureAwait(false)
                    : null;
                items.Add(new(profile.Id.Value.ToString("D"), profile.Name,
                    profile.ApplicationRules.Count, profile.WebsiteRules.Count,
                    profile.ApplicationRules.Count(rule => rule.Action == RuleAction.Block),
                    profile.WebsiteRules.Count(rule => rule.Action == RuleAction.Block), profile.AccentHex,
                    defaultPresetId?.ToString("D"), preset?.Name,
                    preset is null ? null : CycleConfiguration(preset.Configuration)));
            }
            return (null, new BridgeProfilePage(items, request.ProfileOffset, profiles.Count,
                authorization.Authorize(HostControlOperation.StartNormal).State == HostAuthorizationState.Authorized));
        }

        if (request.Method == "session.prepareNormal")
        {
            _cycleReview = null;
            _normalReview = null;
            var status = await client.GetStatusAsync(token).ConfigureAwait(false);
            if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
            if (!CanStart(status)) return ("SessionAlreadyActive", null);
            var input = request.NormalTimer!;
            var review = await _workflow.PrepareNormalTimerAsync(
                new(input.ProfileId, input.DurationSeconds, input.EnforcementEnabled), token).ConfigureAwait(false);
            if (!review.Preparation.Accepted)
                return (review.Preparation.Reason == SessionWorkflow.ProfileMissingReason ? "ProfileMissing" : "PreparationRejected", null);
            if (review.EnforcementEnabled && review.BlockedApplicationCount + review.BlockedWebsiteCount == 0)
                return ("NoBlockingRules", null);
            var prepared = review.Preparation;
            var result = new BridgeNormalReview(prepared.PreparationId!.Value.ToString("N"),
                input.ProfileId.ToString("D"), review.ProfileName!, input.DurationSeconds, input.EnforcementEnabled,
                review.ApplicationRuleCount, review.WebsiteRuleCount, review.BlockedApplicationCount,
                review.BlockedWebsiteCount, prepared.StartUtc!.Value, prepared.EndUtc!.Value,
                prepared.ExpiresUtc!.Value < prepared.EndUtc.Value ? prepared.ExpiresUtc.Value : prepared.EndUtc.Value,
                status.ExtensionReady);
            token.ThrowIfCancellationRequested();
            if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
            _normalReview = (request.DocumentToken, result);
            return (null, result);
        }

        if (_normalReview is not { } owned || owned.DocumentToken != request.DocumentToken
            || owned.Review.PreparationId != request.PreparationId!.Value.ToString("N"))
            return ("PreparationUnavailable", null);
        if (_usedOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_usedOperationIds.Add(request.OperationId!.Value)) return ("DuplicateOperation", null);
        // Consume locally before IPC. Timeout/navigation must never permit replay.
        _normalReview = null;
        var started = await _workflow.ActivatePreparedAsync(new(request.PreparationId.Value), token).ConfigureAwait(false);
        if (!started.Accepted) return ("ActivationRejected", null);
        if (started.Mode != SessionMode.SimpleTimer || started.SessionId is not { } sessionId || sessionId == Guid.Empty
            || started.StartUtc != owned.Review.StartUtc || started.EndUtc != owned.Review.EndUtc)
            return ("OutcomeUnknown", null);
        RememberAcceptedSession(sessionId, Guid.Parse(owned.Review.ProfileId), owned.Review.ProfileName,
            owned.Review.DurationSeconds / 60, SessionMode.SimpleTimer, null, null, owned.Review.EnforcementEnabled,
            objective: request.Objective ?? string.Empty);
        return (null, new BridgeNormalStarted(sessionId.ToString("D"), started.StartUtc.Value, started.EndUtc.Value));
    }

    private void RememberAcceptedSession(Guid sessionId, Guid profileId, string profileName, int durationMinutes,
        SessionMode mode, Guid? presetId, FocusCycleConfiguration? configuration, bool enforcementEnabled,
        bool strict = false, string objective = "")
    {
        if (sessionJournalStore is null || settingsStore is null) return;
        try
        {
            var intention = new SessionIntention(sessionId, profileId, profileName, durationMinutes, mode,
                presetId, configuration, Strict: strict, Objective: objective, EnforcementEnabled: enforcementEnabled);
            _ = new SessionJournalWorkflow(sessionJournalStore, settingsStore, timeProvider)
                .RememberAccepted(intention, settingsStore.Load());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // The activation is already accepted. Local replay metadata stays optional.
        }
    }

    private static bool CanStart(StatusResponse status) =>
        status.SessionState is "Inactive" or "Expired" or "Stopped" or "Recovered" or "Failed";
}
