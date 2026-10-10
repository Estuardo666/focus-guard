using FocusGuard.Client.State;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

public sealed record StartNormalInput(
    Guid ProfileId,
    int DurationSeconds,
    bool EnforcementEnabled = false);

public sealed record NormalTimerReview(
    PrepareSessionResponse Preparation,
    string? ProfileName,
    int ApplicationRuleCount,
    int WebsiteRuleCount,
    int BlockedApplicationCount,
    int BlockedWebsiteCount,
    bool EnforcementEnabled);

public sealed record SavedNormalCycleInput(Guid ProfileId, Guid? PresetId,
    FocusCycleConfiguration? CustomConfiguration, bool EnforcementEnabled = false);

public sealed record NormalCycleReview(PrepareSessionResponse Preparation, string? ProfileName,
    string? PresetName, FocusCycleConfiguration? Configuration, int ApplicationRuleCount,
    int WebsiteRuleCount, int BreakApplicationRuleCount, int BreakWebsiteRuleCount,
    IReadOnlyList<string> BreakApplications, IReadOnlyList<string> BreakWebsites, bool EnforcementEnabled);

// Reviewed snapshots are explicit inputs. Do not reload mutable profile data
// between showing strict consequences and consuming the backend preparation.
public sealed record CycleSessionInput(
    StoredProfile Profile,
    FocusCycleConfiguration Configuration,
    ProfileCycleSettings? BreakSettings = null,
    bool StrictMode = false,
    int RecoveryDelaySeconds = 0,
    bool EnforcementEnabled = false);

public sealed record StrictTimerInput(
    StoredProfile Profile,
    int DurationSeconds,
    int RecoveryDelaySeconds,
    RecoveryCredential? RecoveryCredential,
    bool EnforcementEnabled = false);

public sealed record PrepareStrictTimerInput(StoredProfile Profile, int DurationSeconds, int RecoveryDelaySeconds,
    bool EnforcementEnabled = false);

public sealed record NormalCycleStartResult(
    PrepareSessionResponse Preparation,
    ActivatePreparedSessionResponse? Activation);

public interface ISessionWorkflow
{
    SessionConnectionSnapshot ConnectionSnapshot { get; }

    void InvalidateConnection();

    ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<ActivateSessionResponse> StartNormalAsync(
        StartNormalInput input,
        CancellationToken cancellationToken);

    ValueTask<NormalTimerReview> PrepareNormalTimerAsync(
        StartNormalInput input,
        CancellationToken cancellationToken);

    ValueTask<NormalTimerReview> PrepareStrictTimerAsync(
        PrepareStrictTimerInput input,
        CancellationToken cancellationToken);

    ValueTask<NormalCycleReview> PrepareSavedNormalCycleAsync(
        SavedNormalCycleInput input, CancellationToken cancellationToken);

    ValueTask<ActivateSessionResponse> StartStrictTimerAsync(
        StrictTimerInput input, CancellationToken cancellationToken);

    ValueTask<PrepareSessionResponse> PrepareCycleAsync(
        CycleSessionInput input, CancellationToken cancellationToken);

    ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
        ActivatePreparedSessionRequest request, CancellationToken cancellationToken);

    ValueTask<NormalCycleStartResult> StartNormalCycleAsync(
        CycleSessionInput input, CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopNormalAsync(CancellationToken cancellationToken);

    ValueTask<SkipBreakResponse> SkipBreakAsync(int phaseIndex, CancellationToken cancellationToken);
}
