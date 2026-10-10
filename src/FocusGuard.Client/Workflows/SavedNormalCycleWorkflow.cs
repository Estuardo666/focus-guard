using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Client.Workflows;

public sealed partial class SessionWorkflow
{
    public async ValueTask<NormalCycleReview> PrepareSavedNormalCycleAsync(
        SavedNormalCycleInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ProfileId == Guid.Empty || input.PresetId == Guid.Empty
            || input.PresetId.HasValue == (input.CustomConfiguration is not null))
            throw new InvalidDataException("Choose exactly one saved preset or custom cycle configuration.");
        var profile = await profileStore.LoadProfileAsync(new(input.ProfileId), cancellationToken).ConfigureAwait(false);
        if (profile is null) return MissingCycleReview(ProfileMissingReason, input.EnforcementEnabled);
        if (profile.Id.Value != input.ProfileId) throw new InvalidDataException("Profile store returned a different profile.");
        var preset = input.PresetId is { } presetId
            ? await profileStore.LoadCyclePresetAsync(presetId, cancellationToken).ConfigureAwait(false) : null;
        if (input.PresetId.HasValue && preset is null) return MissingCycleReview("PresetMissing", input.EnforcementEnabled);
        if (preset is not null && preset.Id != input.PresetId) throw new InvalidDataException("Preset store returned a different preset.");
        var configuration = preset?.Configuration ?? input.CustomConfiguration!;
        var settings = await profileStore.LoadProfileCycleSettingsAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (settings is not null && settings.ProfileId != profile.Id) throw new InvalidDataException("Cycle settings belong to another profile.");
        // Break exception tables persist Block rules: these remain denied while
        // the ordinary focus rules are relaxed. Reject unknown actions, but never
        // rewrite an existing rule to fit a different contract.
        if (settings is not null && (settings.ApplicationRules.Any(rule => rule.Action != RuleAction.Block)
            || settings.WebsiteRules.Any(rule => rule.Action != RuleAction.Block)))
            return MissingCycleReview("BreakRulesInvalid", input.EnforcementEnabled);
        var prepared = await PrepareCycleAsync(new(profile, configuration, settings,
            StrictMode: false, RecoveryDelaySeconds: 0, EnforcementEnabled: input.EnforcementEnabled), cancellationToken).ConfigureAwait(false);
        ValidateCycleReview(prepared, configuration);
        return new(prepared, profile.Name, preset?.Name, configuration, profile.ApplicationRules.Count,
            profile.WebsiteRules.Count, settings?.ApplicationRules.Count ?? 0, settings?.WebsiteRules.Count ?? 0,
            settings?.ApplicationRules.Select(rule => rule.Value).ToArray() ?? [],
            settings?.WebsiteRules.Select(rule => rule.Pattern).ToArray() ?? [], input.EnforcementEnabled);
    }

    private static NormalCycleReview MissingCycleReview(string reason, bool enforcement) =>
        new(new(false, reason, null, null, null, null, SessionMode.FocusCycle, []), null, null, null,
            0, 0, 0, 0, [], [], enforcement);

    public static void ValidateCycleReview(PrepareSessionResponse response, FocusCycleConfiguration configuration)
    {
        if (response.Mode != SessionMode.FocusCycle || string.IsNullOrWhiteSpace(response.Reason) || response.Phases is null)
            throw new InvalidDataException("Invalid cycle preparation response.");
        if (!response.Accepted)
        {
            if (response.PreparationId is not null || response.StartUtc is not null || response.EndUtc is not null
                || response.ExpiresUtc is not null || response.Phases.Count != 0)
                throw new InvalidDataException("Rejected cycle preparation contains activation metadata.");
            return;
        }
        if (response.PreparationId is not { } id || id == Guid.Empty
            || response.StartUtc is not { } start || start.Offset != TimeSpan.Zero
            || response.EndUtc is not { } end || end.Offset != TimeSpan.Zero
            || response.ExpiresUtc is not { } expiry || expiry.Offset != TimeSpan.Zero
            || expiry <= start || expiry - start > TimeSpan.FromMinutes(2))
            throw new InvalidDataException("Invalid cycle preparation identity or UTC timestamps.");
        var expected = FocusCyclePlanBuilder.Build(configuration, start);
        if (!expected.IsValid || expected.Value!.EndUtc != end || response.Phases.Count != expected.Value.Phases.Count)
            throw new InvalidDataException("Cycle preparation does not match the reviewed configuration.");
        for (var index = 0; index < response.Phases.Count; index++)
        {
            var actual = response.Phases[index]; var phase = expected.Value.Phases[index];
            if (actual.Index != phase.Index || actual.Kind != phase.Kind || actual.FocusCycleNumber != phase.FocusCycleNumber
                || actual.StartUtc.Offset != TimeSpan.Zero || actual.EndUtc.Offset != TimeSpan.Zero
                || actual.StartUtc != phase.StartUtc || actual.EndUtc != phase.EndUtc)
                throw new InvalidDataException("Cycle phase plan does not match the reviewed configuration.");
        }
    }
}
