namespace FocusGuard.Contracts;

public enum FocusAuditEventKind
{
    ApplicationBlocked,
    WebsiteBlocked,
    EnforcementReleased,
    RecoveryCompleted,
    SessionInterrupted
}

public sealed record FocusAuditEvent(
    Guid Id,
    DateTimeOffset TimestampUtc,
    FocusAuditEventKind Kind,
    string Target,
    string Outcome,
    Guid? SessionId = null);

/// <summary>Maximum recent events requested; the service may return fewer to stay within the IPC frame limit.</summary>
public sealed record ActivityRequest(int MaximumCount = 200);

public sealed record ActivityResponse(IReadOnlyList<FocusAuditEvent> Events);
