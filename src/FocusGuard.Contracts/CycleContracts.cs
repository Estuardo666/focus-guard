using FocusGuard.Domain;

namespace FocusGuard.Contracts;

public sealed record FocusCycleConfigurationContract(
    long FocusSeconds,
    long ShortBreakSeconds,
    long LongBreakSeconds,
    int CyclesBeforeLongBreak,
    CycleTerminationMode TerminationMode,
    int? CycleCount,
    long? TotalDurationSeconds);

public sealed record PhaseStatus(
    int Index,
    SessionPhaseKind Kind,
    int FocusCycleNumber,
    long RemainingSeconds,
    DateTimeOffset EndUtc,
    DateTimeOffset? StartUtc = null);

public sealed record PreparedSessionPhase(
    int Index,
    SessionPhaseKind Kind,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int FocusCycleNumber);

public sealed record PrepareSessionRequest(
    Guid ProfileId,
    string? ProfileName,
    SessionMode Mode,
    int DurationSeconds,
    FocusCycleConfigurationContract? CycleConfiguration,
    bool StrictMode,
    int RecoveryDelaySeconds,
    IReadOnlyList<ApplicationPolicyRule>? ApplicationRules = null,
    IReadOnlyList<WebsitePolicyRule>? WebsiteRules = null,
    bool EnforcementEnabled = false,
    IReadOnlyList<ApplicationPolicyRule>? BreakApplicationRules = null,
    IReadOnlyList<WebsitePolicyRule>? BreakWebsiteRules = null);

public sealed record PrepareSessionResponse(
    bool Accepted,
    string Reason,
    Guid? PreparationId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    DateTimeOffset? ExpiresUtc,
    SessionMode Mode,
    IReadOnlyList<PreparedSessionPhase> Phases);

public sealed record ActivatePreparedSessionRequest(
    Guid PreparationId,
    RecoveryCredential? RecoveryCredential = null);

public sealed record ActivatePreparedSessionResponse(
    bool Accepted,
    string Reason,
    Guid? SessionId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    SessionMode Mode);

public sealed record SkipBreakRequest(int PhaseIndex);

public sealed record SkipBreakResponse(
    bool Accepted,
    string Reason,
    int PhaseIndex,
    PhaseStatus? CurrentPhase,
    DateTimeOffset? SessionEndUtc);
