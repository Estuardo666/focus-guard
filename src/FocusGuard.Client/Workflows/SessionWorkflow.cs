using FocusGuard.Client.State;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

/// <summary>
/// Coordinates normal-session requests using saved profiles. The service remains
/// authoritative for activation, enforcement, time and session state.
/// </summary>
public sealed partial class SessionWorkflow(
    IFocusControlClient client,
    IProfileStore profileStore,
    TimeProvider? timeProvider = null) : ISessionWorkflow
{
    public const string ProfileMissingReason = "ProfileMissing";
    private readonly SessionConnectionTracker _connection = new(timeProvider);

    public SessionConnectionSnapshot ConnectionSnapshot => _connection.Snapshot;

    public void InvalidateConnection() => _connection.MarkDisconnected();

    public async ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            _connection.Observe(status);
            return status;
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or InvalidDataException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _connection.MarkDisconnected();
            throw;
        }
    }

    public async ValueTask<ActivateSessionResponse> StartNormalAsync(
        StartNormalInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new ActivateSessionRequest(
            input.ProfileId,
            input.DurationSeconds,
            StrictMode: false,
            RecoveryDelaySeconds: 0,
            EnforcementEnabled: input.EnforcementEnabled);
        Validate(request);

        var profile = await profileStore.LoadProfileAsync(
            new ProfileId(input.ProfileId),
            cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new ActivateSessionResponse(
                false, ProfileMissingReason, null, null, null);
        }

        if (profile.Id.Value != input.ProfileId)
        {
            throw new InvalidDataException("Profile store returned a different profile.");
        }

        // Resolve once and copy rules into the activation snapshot. Later profile
        // edits must not affect the request or an already-running session.
        request = request with
        {
            ProfileName = profile.Name,
            ApplicationRules = profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(
                rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray(),
            WebsiteRules = profile.WebsiteRules.Select(rule => new WebsitePolicyRule(
                rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray()
        };
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Do not retry activation after timeout or disconnect: the service may
        // already have accepted it. The caller must reconcile authoritative status.
        return await client.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(ActivateSessionRequest request)
    {
        var validation = IpcProtocol.ValidateActivateSessionRequest(request);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }
    }
}
