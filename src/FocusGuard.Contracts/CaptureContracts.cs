using FocusGuard.Domain;

namespace FocusGuard.Contracts;

/// <summary>
/// A single urge written down on the block screen. Sent by the desktop block window and
/// by the browser extension (through the native host) while a session is running.
/// </summary>
public sealed record CaptureRequest(string? Text, string SourceKind, string SourceValue);

public sealed record CaptureResponse(bool Accepted, string Reason, Guid? CaptureId);

/// <summary>
/// Flattened capture for display. <see cref="SourceLabel"/> is computed service-side so
/// the summary window never has to parse a URL to render a row.
/// </summary>
public sealed record CapturedItemView(
    Guid Id,
    string Text,
    string SourceKind,
    string SourceValue,
    string SourceLabel,
    DateTimeOffset CapturedAtUtc,
    string Resolution);

public sealed record SessionSummaryRequest(Guid? SessionId = null);

/// <summary>
/// What the end-of-session window shows. <see cref="Outcome"/> is "Completed" when the
/// session ran to its end and "Stopped" when the user ended it early: the window drops
/// the green check and the streak in that case rather than claiming a win that did not
/// happen.
/// </summary>
public sealed record SessionSummaryResponse(
    bool Available,
    Guid? SessionId,
    string Outcome,
    string ProfileName,
    int PlannedMinutes,
    int FocusedMinutes,
    int BlockedAttempts,
    int StreakDays,
    DateTimeOffset? EndedAtUtc,
    IReadOnlyList<CapturedItemView> Captures,
    CycleHistoryMetrics? CycleMetrics = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? ProfileId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ScheduledSessionIntent? ScheduledIntent = null);

public sealed record ResolveCapturesRequest(IReadOnlyList<Guid> CaptureIds, string Resolution);

public sealed record ResolveCapturesResponse(bool Accepted, string Reason, int ResolvedCount);

public static class CaptureProtocol
{
    public const int MaximumCapturesPerSession = 200;
    public const string WebsiteKind = "Website";
    public const string ApplicationKind = "Application";
    public const string PendingResolution = "Pending";
    public const string OpenedResolution = "Opened";
    public const string DismissedResolution = "Dismissed";
    public const string CompletedOutcome = "Completed";
    public const string StoppedOutcome = "Stopped";

    public static bool IsSourceKind(string? value) =>
        value is WebsiteKind or ApplicationKind;

    public static bool IsTerminalResolution(string? value) =>
        value is OpenedResolution or DismissedResolution;

    public static bool IsOutcome(string? value) =>
        value is CompletedOutcome or StoppedOutcome;
}
