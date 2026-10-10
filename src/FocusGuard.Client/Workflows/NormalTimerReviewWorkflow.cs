using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Client.Workflows;

public sealed partial class SessionWorkflow
{
    /// <summary>
    /// Resolves one saved profile for the dashboard's normal-timer review. This
    /// does not activate or distribute policy. Activation consumes the service's
    /// preparation id, never a reloaded profile or a locally computed deadline.
    /// </summary>
    public async ValueTask<NormalTimerReview> PrepareNormalTimerAsync(
        StartNormalInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ProfileId == Guid.Empty || input.DurationSeconds is < 60 or > 720 * 60
            || input.DurationSeconds % 60 != 0)
        {
            throw new InvalidDataException("Normal dashboard timers require 1–720 whole minutes.");
        }

        var profile = await profileStore.LoadProfileAsync(
            new ProfileId(input.ProfileId), cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new(new(false, ProfileMissingReason, null, null, null, null,
                SessionMode.SimpleTimer, []), null, 0, 0, 0, 0, input.EnforcementEnabled);
        }

        if (profile.Id.Value != input.ProfileId)
            throw new InvalidDataException("Profile store returned a different profile.");

        var applications = Array.AsReadOnly(profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(
            rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray());
        var websites = Array.AsReadOnly(profile.WebsiteRules.Select(rule => new WebsitePolicyRule(
            rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray());
        var request = new PrepareSessionRequest(input.ProfileId, profile.Name,
            SessionMode.SimpleTimer, input.DurationSeconds, null, false, 0,
            applications, websites, input.EnforcementEnabled);
        var validation = CycleIpcProtocol.ValidatePrepareRequest(request);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join(" ", validation.Errors));

        cancellationToken.ThrowIfCancellationRequested();
        // No retry: an uncertain reply must be reconciled, never re-prepared
        // automatically. A later explicit review may replace the old plan.
        var prepared = await client.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateNormalPreparation(prepared, input.DurationSeconds);
        return new(prepared, profile.Name, applications.Count, websites.Count,
            applications.Count(rule => rule.Action == nameof(RuleAction.Block)),
            websites.Count(rule => rule.Action == nameof(RuleAction.Block)), input.EnforcementEnabled);
    }

    public async ValueTask<NormalTimerReview> PrepareStrictTimerAsync(
        PrepareStrictTimerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Profile.Id.Value == Guid.Empty || input.DurationSeconds is < 60 or > 720 * 60
            || input.DurationSeconds % 60 != 0 || input.RecoveryDelaySeconds is < 60 or > IpcProtocol.MaximumRecoveryDelaySeconds)
            throw new InvalidDataException("Strict dashboard timers require a finite duration and recovery delay.");

        var profile = input.Profile;

        var applications = Array.AsReadOnly(profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(
            rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray());
        var websites = Array.AsReadOnly(profile.WebsiteRules.Select(rule => new WebsitePolicyRule(
            rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray());
        var request = new PrepareSessionRequest(profile.Id.Value, profile.Name, SessionMode.SimpleTimer,
            input.DurationSeconds, null, true, input.RecoveryDelaySeconds, applications, websites,
            input.EnforcementEnabled);
        var validation = CycleIpcProtocol.ValidatePrepareRequest(request);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        cancellationToken.ThrowIfCancellationRequested();

        var prepared = await client.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateNormalPreparation(prepared, input.DurationSeconds);
        return new(prepared, profile.Name, applications.Count, websites.Count,
            applications.Count(rule => rule.Action == nameof(RuleAction.Block)),
            websites.Count(rule => rule.Action == nameof(RuleAction.Block)), input.EnforcementEnabled);
    }

    private static void ValidateNormalPreparation(PrepareSessionResponse prepared, int durationSeconds)
    {
        if (prepared.Mode != SessionMode.SimpleTimer || string.IsNullOrWhiteSpace(prepared.Reason)
            || prepared.Phases is null || prepared.Phases.Count != 0)
            throw new InvalidDataException("Invalid normal timer preparation response.");

        if (!prepared.Accepted)
        {
            if (prepared.PreparationId is not null || prepared.StartUtc is not null
                || prepared.EndUtc is not null || prepared.ExpiresUtc is not null)
                throw new InvalidDataException("Rejected preparation contains activation metadata.");
            return;
        }

        if (prepared.PreparationId is not { } id || id == Guid.Empty
            || prepared.StartUtc is not { } start || start.Offset != TimeSpan.Zero
            || prepared.EndUtc is not { } end || end.Offset != TimeSpan.Zero
            || end - start != TimeSpan.FromSeconds(durationSeconds)
            || prepared.ExpiresUtc is not { } expiry || expiry.Offset != TimeSpan.Zero
            || expiry <= start || expiry - start > TimeSpan.FromMinutes(2))
            throw new InvalidDataException("Invalid normal timer preparation response.");
    }
}
