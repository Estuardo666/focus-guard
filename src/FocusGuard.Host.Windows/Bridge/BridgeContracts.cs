using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeRequest(
    Guid RequestId,
    string DocumentToken,
    string Method,
    int ProfileOffset = 0,
    NormalTimerParameters? NormalTimer = null,
    Guid? PreparationId = null,
    Guid? OperationId = null,
    ProfileCommand? ProfileCommand = null,
    string? CatalogQuery = null,
    string? Objective = null,
    bool CatalogRefresh = false,
    CycleParameters? Cycle = null,
    ActiveSessionParameters? ActiveSession = null,
    CyclePresetParameters? CyclePreset = null,
    TodayRequest? Today = null,
    ScheduleCommand? Schedule = null,
    Guid? SummarySessionId = null,
    CaptureCommand? CaptureAction = null,
    SessionReflectionCommand? SessionReflection = null,
    PreferenceCommand? Preferences = null,
    Guid? SessionIntentId = null,
    StrictSessionParameters? StrictSession = null,
    StrictSessionActivationParameters? StrictActivation = null,
    Guid? StrictCancelPreparationId = null,
    Guid? StrictSaveRecoveryCodeId = null,
    bool RecoveryInfo = false,
    HostWindowCommand? WindowCommand = null,
    HostAppCommand? AppCommand = null,
    HostAccountAction? AccountAction = null,
    string? AccountPairingId = null,
    string? AccountFingerprint = null,
    HostSettingsSyncConflictResolution? SettingsSyncResolution = null,
    HostSettingsSyncResetResolution? SettingsSyncResetResolution = null);

internal sealed record HostWindowCommand(bool? Compact = null, bool? AlwaysOnTop = null, bool? DetailsExpanded = null);

internal enum HostAppAction { Open, Compact, Quit, CloseSurface, StartupGet, StartupSet, SurfaceReady }

/// <summary>Shell-level commands from the tray panel and popups: show a section, the compact view, close the popup, or quit.</summary>
internal sealed record HostAppCommand(HostAppAction Action, string? Section = null, Guid? SessionId = null, bool? Enabled = null);

/// <summary><see cref="StartupEnabled"/> is set only for the sign-in start commands.</summary>
internal sealed record HostAppResult(bool Accepted, bool? StartupEnabled = null);

internal sealed record BridgeAccountResponse(int Version, string RequestId, string DocumentToken,
    string? Error, HostAccountSnapshot Result);

internal sealed record HostWindowStateMessage(string Type, int Version, string DocumentToken, bool Compact, bool AlwaysOnTop, bool DetailsExpanded);

internal sealed record ActiveSessionParameters(Guid SessionId, int? PhaseIndex);

internal sealed record NormalTimerParameters(Guid ProfileId, int DurationSeconds, bool EnforcementEnabled);

internal sealed record BridgeResponse(
    int Version,
    string RequestId,
    string DocumentToken,
    string? Error,
    BridgeStatus? Status);

internal sealed record BridgeStatus(
    bool ServiceReady,
    bool ExtensionReady,
    string SessionState,
    long RemainingSeconds,
    string? SessionId,
    string? ProfileId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    bool StrictMode,
    bool EnforcementConnected,
    string Mode,
    BridgePhaseStatus? CurrentPhase = null,
    BridgePhaseStatus? NextPhase = null,
    DateTimeOffset? FinalEndUtc = null);

internal sealed record BridgePhaseStatus(int Index, string Kind, int FocusCycleNumber, long RemainingSeconds,
    DateTimeOffset EndUtc, DateTimeOffset? StartUtc);

internal sealed record HostInitializationMessage(
    string Type,
    int Version,
    string DocumentToken,
    string Title,
    string Description,
    string ReadyLabel,
    string BridgeUnavailableLabel);

internal static class BridgeLimits
{
    public const int ProtocolVersion = 1;
    public const int MaximumRequestBytes = 4 * 1024;
    public const int MaximumResponseBytes = 8 * 1024;
    public const int MaximumRemainingSeconds = 30 * 24 * 60 * 60;
    public const int MaximumRequestsPerMinute = 60;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    public static bool IsValidStatus(StatusResponse status)
    {
        if (!status.ServiceReady
            || status.RemainingSeconds is < 0 or > MaximumRemainingSeconds
            || !Enum.IsDefined(status.Mode)
            || status.SessionId.HasValue != status.ProfileId.HasValue
            || status.SessionId.HasValue != status.StartUtc.HasValue
            || status.SessionId.HasValue != status.EndUtc.HasValue)
        {
            return false;
        }

        if (!status.SessionId.HasValue)
        {
            return status.SessionState == "Inactive"
                && status.RemainingSeconds == 0
                && !status.StrictMode
                && !status.EnforcementConnected
                && status.Mode == SessionMode.SimpleTimer
                && status.CurrentPhase is null
                && status.NextPhase is null
                && status.FinalEndUtc is null
                && status.ScheduledIntent is null;
        }

        if (status.SessionId == Guid.Empty
            || status.ProfileId == Guid.Empty
            || status.StartUtc is not { } startUtc
            || startUtc.Offset != TimeSpan.Zero
            || status.EndUtc is not { } endUtc
            || endUtc.Offset != TimeSpan.Zero
            || startUtc >= endUtc
            || !Enum.TryParse<SessionState>(status.SessionState, ignoreCase: false, out var state)
            || !Enum.IsDefined(state)
            || !string.Equals(Enum.GetName(state), status.SessionState, StringComparison.Ordinal))
        {
            return false;
        }

        return (status.FinalEndUtc is null
                || status.FinalEndUtc.Value.Offset == TimeSpan.Zero && status.FinalEndUtc.Value >= endUtc)
            && (status.Mode != SessionMode.SimpleTimer || status.CurrentPhase is null && status.NextPhase is null && status.FinalEndUtc is null)
            && (status.CurrentPhase is null || status.CurrentPhase.EndUtc <= endUtc && status.CurrentPhase.EndUtc > startUtc
                && (status.CurrentPhase.StartUtc is null || status.CurrentPhase.StartUtc >= startUtc))
            && (status.NextPhase is null || status.NextPhase.EndUtc <= endUtc && status.NextPhase.EndUtc > startUtc
                && (status.NextPhase.StartUtc is null || status.NextPhase.StartUtc >= startUtc))
            && (status.CurrentPhase is null || status.NextPhase is null || status.NextPhase.Index > status.CurrentPhase.Index
                && (status.NextPhase.StartUtc is null || status.NextPhase.StartUtc >= status.CurrentPhase.EndUtc))
            && IsValidPhase(status.CurrentPhase)
            && IsValidPhase(status.NextPhase)
            && (status.ScheduledIntent is null || !status.StrictMode && status.ScheduledIntent.IsValid());
    }

    internal static bool IsValidPhase(PhaseStatus? phase) =>
        phase is null
        || phase.Index is >= 0 and <= 48
            && Enum.IsDefined(phase.Kind)
            && phase.FocusCycleNumber is >= 1 and <= 25
            && phase.EndUtc.Offset == TimeSpan.Zero
            && (phase.StartUtc is null || phase.StartUtc.Value.Offset == TimeSpan.Zero && phase.StartUtc.Value < phase.EndUtc)
            && phase.RemainingSeconds is >= 0 and <= MaximumRemainingSeconds;

    public static BridgeStatus Project(StatusResponse status) => new(
        status.ServiceReady,
        status.ExtensionReady,
        status.SessionState,
        status.RemainingSeconds,
        status.SessionId?.ToString("D"),
        status.ProfileId?.ToString("D"),
        status.StartUtc,
        status.EndUtc,
        status.StrictMode,
        status.EnforcementConnected,
        status.Mode.ToString(),
        ProjectPhase(status.CurrentPhase), ProjectPhase(status.NextPhase), status.FinalEndUtc);

    private static BridgePhaseStatus? ProjectPhase(PhaseStatus? phase) => phase is null ? null :
        new(phase.Index, phase.Kind.ToString(), phase.FocusCycleNumber, phase.RemainingSeconds, phase.EndUtc, phase.StartUtc);
}

internal sealed record BridgeAppResponse(int Version, string RequestId, string DocumentToken, string? Error, HostAppResult Result);

internal sealed record HostNavigateMessage(string Type, int Version, string DocumentToken, string Section);
