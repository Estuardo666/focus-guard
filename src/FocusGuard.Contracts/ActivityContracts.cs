namespace FocusGuard.Contracts;

public enum FocusAuditEventKind
{
    ApplicationBlocked,
    WebsiteBlocked,
    EnforcementReleased,
    RecoveryCompleted
}

public sealed record FocusAuditEvent(
    Guid Id,
    DateTimeOffset TimestampUtc,
    FocusAuditEventKind Kind,
    string Target,
    string Outcome,
    Guid? SessionId = null);

public sealed record ActivityRequest(int MaximumCount = 200);

public sealed record ActivityResponse(IReadOnlyList<FocusAuditEvent> Events);
