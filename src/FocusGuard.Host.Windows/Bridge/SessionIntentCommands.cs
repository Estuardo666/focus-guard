using FocusGuard.Client.Journal;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeSessionIntent(string SessionId, bool Available, bool Replayable, string? UnavailableReason,
    string? OriginalProfileName, BridgeProfileSummary? Profile, int? DurationMinutes, string? Mode,
    bool? Strict, bool? EnforcementEnabled, string? CyclePresetId, BridgeCycleConfiguration? CycleConfiguration,
    string? Objective);

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandleSessionIntentAsync(
        BridgeRequest request, CancellationToken token)
    {
        var sessionId = request.SessionIntentId!.Value;
        var unavailable = new BridgeSessionIntent(sessionId.ToString("D"), false, false,
            "IntentUnavailable", null, null, null, null, null, null, null, null, null);
        if (sessionJournalStore is null) return (null, unavailable);

        var intention = await Task.Run(() => sessionJournalStore.GetLatest(sessionId), token).ConfigureAwait(false);
        if (intention is null || intention.EnforcementEnabled is null)
        {
            var observed = await ReadScheduledIntentAsync(sessionId, token).ConfigureAwait(false);
            if (observed is not null)
            {
                sessionJournalStore.SaveObserved(observed);
                intention = observed with
                {
                    Objective = intention?.Objective ?? string.Empty,
                    Reflection = intention?.Reflection ?? string.Empty
                };
            }
        }
        if (intention is null || intention.SessionId != sessionId || intention.ProfileId == Guid.Empty
            || intention.ProfileName is not { Length: > 0 and <= 80 } || intention.DurationMinutes is < 1 or > 720
            || !Enum.IsDefined(intention.Mode)) return (null, unavailable);

        FocusCycleConfiguration? validConfiguration = null;
        var configurationIsValid = intention.Mode == SessionMode.SimpleTimer
            ? intention.CycleConfiguration is null && intention.CyclePresetId is null
            : intention.CycleConfiguration is { } configuration
                && FocusCycleConfiguration.Create(configuration.FocusDuration, configuration.ShortBreakDuration,
                    configuration.LongBreakDuration, configuration.CyclesBeforeLongBreak,
                    configuration.TerminationMode, configuration.CycleCount, configuration.TotalDuration) is { IsValid: true };
        if (configurationIsValid && intention.Mode == SessionMode.FocusCycle)
            validConfiguration = intention.CycleConfiguration;

        BridgeProfileSummary? profileSummary = null;
        if (profileStore is not null)
        {
            var profile = await profileStore.LoadProfileAsync(new ProfileId(intention.ProfileId), token).ConfigureAwait(false);
            if (profile?.Id.Value == intention.ProfileId && Profile.ValidateName(profile.Name).IsValid)
            {
                var defaultPresetId = await profileStore.LoadDefaultCyclePresetIdAsync(profile.Id, token).ConfigureAwait(false);
                var defaultPreset = defaultPresetId is { } id
                    ? await profileStore.LoadCyclePresetAsync(id, token).ConfigureAwait(false)
                    : null;
                profileSummary = new(profile.Id.Value.ToString("D"), profile.Name, profile.ApplicationRules.Count,
                    profile.WebsiteRules.Count, profile.ApplicationRules.Count(rule => rule.Action == RuleAction.Block),
                    profile.WebsiteRules.Count(rule => rule.Action == RuleAction.Block), profile.AccentHex,
                    defaultPresetId?.ToString("D"), defaultPreset?.Name,
                    defaultPreset is null ? null : CycleConfiguration(defaultPreset.Configuration));
            }
        }

        var reason = profileSummary is null ? "MissingProfile"
            : intention.EnforcementEnabled is null ? "EnforcementConsentUnknown"
            : !configurationIsValid ? "CycleConfigurationUnavailable"
            : null;
        var replayable = reason is null;
        var result = new BridgeSessionIntent(sessionId.ToString("D"), true, replayable, reason,
            intention.ProfileName, profileSummary, intention.DurationMinutes, intention.Mode.ToString(), intention.Strict,
            intention.EnforcementEnabled, intention.CyclePresetId?.ToString("D"), validConfiguration is null
                ? null : ProjectCycleConfiguration(validConfiguration), (intention.Objective ?? string.Empty)[..Math.Min(intention.Objective?.Length ?? 0, 240)]);
        return (null, result);
    }

    private async ValueTask<SessionIntention?> ReadScheduledIntentAsync(Guid sessionId, CancellationToken token)
    {
        try
        {
            var status = await client.GetStatusAsync(token).ConfigureAwait(false);
            if (BridgeLimits.IsValidStatus(status) && status.SessionId == sessionId && !status.StrictMode
                && status.ProfileId is { } profileId && status.ScheduledIntent is { } scheduled
                && scheduled.IsValid())
            {
                return new SessionIntention(sessionId, profileId, scheduled.ProfileName, scheduled.DurationMinutes,
                    SessionMode.SimpleTimer, null, null, Strict: false, Objective: string.Empty,
                    EnforcementEnabled: scheduled.EnforcementEnabled);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Security.SecurityException or TimeoutException)
        {
            // A locally accepted intention remains usable if the service cannot answer.
        }

        try
        {
            var summary = await client.GetSessionSummaryAsync(sessionId, token).ConfigureAwait(false);
            if (summary.Available && summary.SessionId == sessionId && summary.ProfileId is { } profileId
                && profileId != Guid.Empty && !string.IsNullOrWhiteSpace(summary.ProfileName)
                && summary.ScheduledIntent is { } scheduled && scheduled.IsValid()
                && string.Equals(summary.ProfileName, scheduled.ProfileName, StringComparison.Ordinal))
            {
                return new SessionIntention(sessionId, profileId, scheduled.ProfileName, scheduled.DurationMinutes,
                    SessionMode.SimpleTimer, null, null, Strict: false, Objective: string.Empty,
                    EnforcementEnabled: scheduled.EnforcementEnabled);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Security.SecurityException or TimeoutException)
        {
            // Older sessions have no schedule metadata, and summary data is optional.
        }

        return null;
    }
    private static BridgeCycleConfiguration ProjectCycleConfiguration(FocusCycleConfiguration configuration) =>
        new((long)configuration.FocusDuration.TotalSeconds, (long)configuration.ShortBreakDuration.TotalSeconds,
            (long)configuration.LongBreakDuration.TotalSeconds, configuration.CyclesBeforeLongBreak,
            configuration.TerminationMode.ToString(), configuration.CycleCount,
            (long?)configuration.TotalDuration?.TotalSeconds);
}
