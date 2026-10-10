using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Client.Workflows;

public sealed partial class SessionWorkflow
{
    public async ValueTask<ActivateSessionResponse> StartStrictTimerAsync(
        StrictTimerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Profile);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = input.Profile;
        var request = new ActivateSessionRequest(profile.Id.Value, input.DurationSeconds,
            StrictMode: true, input.RecoveryDelaySeconds, input.RecoveryCredential,
            profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(
                rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray(),
            profile.WebsiteRules.Select(rule => new WebsitePolicyRule(
                rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray(),
            input.EnforcementEnabled, profile.Name);
        Validate(request);
        return await client.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PrepareSessionResponse> PrepareCycleAsync(
        CycleSessionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Profile);
        ArgumentNullException.ThrowIfNull(input.Configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = input.Profile;
        var configuration = input.Configuration;
        var request = new PrepareSessionRequest(profile.Id.Value, profile.Name, SessionMode.FocusCycle, 0,
            new FocusCycleConfigurationContract(
                checked((long)configuration.FocusDuration.TotalSeconds),
                checked((long)configuration.ShortBreakDuration.TotalSeconds),
                checked((long)configuration.LongBreakDuration.TotalSeconds),
                configuration.CyclesBeforeLongBreak, configuration.TerminationMode, configuration.CycleCount,
                configuration.TotalDuration is { } total ? checked((long)total.TotalSeconds) : null),
            input.StrictMode, input.RecoveryDelaySeconds,
            profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(
                rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray(),
            profile.WebsiteRules.Select(rule => new WebsitePolicyRule(
                rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray(),
            input.EnforcementEnabled,
            input.BreakSettings?.ApplicationRules.Select(rule => new ApplicationPolicyRule(
                rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray(),
            input.BreakSettings?.WebsiteRules.Select(rule => new WebsitePolicyRule(
                rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray());
        var validation = CycleIpcProtocol.ValidatePrepareRequest(request);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return await client.PrepareAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
        ActivatePreparedSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.PreparationId == Guid.Empty)
            throw new InvalidDataException("Prepared-session activation request is invalid.");
        // The service owns TTL, consume-once, strictness and persistence. Never
        // reconstruct or prepare another session here after a lost response.
        return client.ActivatePreparedAsync(request, cancellationToken);
    }

    public async ValueTask<NormalCycleStartResult> StartNormalCycleAsync(
        CycleSessionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.StrictMode || input.RecoveryDelaySeconds != 0)
            throw new InvalidDataException("Strict cycles require a separate review and activation.");
        var preparation = await PrepareCycleAsync(input, cancellationToken).ConfigureAwait(false);
        if (!preparation.Accepted || preparation.PreparationId is not { } id)
            return new(preparation, null);
        var activation = await ActivatePreparedAsync(new(id), cancellationToken).ConfigureAwait(false);
        return new(preparation, activation);
    }

    public ValueTask<StopSessionResponse> StopNormalAsync(CancellationToken cancellationToken) =>
        client.StopAsync(cancellationToken);

    public ValueTask<SkipBreakResponse> SkipBreakAsync(int phaseIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = new SkipBreakRequest(phaseIndex);
        var validation = CycleIpcProtocol.CreateSkipBreakRequest(Guid.NewGuid(), request);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));
        return client.SkipBreakAsync(request, cancellationToken);
    }
}
