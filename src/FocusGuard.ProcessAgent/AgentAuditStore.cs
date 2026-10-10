using FocusGuard.Contracts;
using FocusGuard.Service;

namespace FocusGuard.ProcessAgent;

public sealed record PendingAudit(Guid SessionId, string Executable, string Outcome);

/// <summary>Bounded, session-bound, best-effort audit queue. Uncertain sends are not replayed.</summary>
public sealed class AgentAuditStore : IFocusAuditStore
{
    public const int MaximumPending = 256;
    private readonly object _gate = new();
    private readonly Queue<PendingAudit> _pending = new();

    public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (auditEvent.SessionId is not Guid sessionId) return Task.CompletedTask;
        var outcome = auditEvent.Outcome switch { "Terminated" => "Terminated", "AlreadyExited" => "Closed", _ => "Failed" };
        lock (_gate)
        {
            if (_pending.Count >= MaximumPending) _pending.Dequeue();
            _pending.Enqueue(new(sessionId, Path.GetFileName(auditEvent.Target), outcome));
        }
        return Task.CompletedTask;
    }

    public PendingAudit? TakeForSession(Guid sessionId)
    {
        lock (_gate)
        {
            while (_pending.TryDequeue(out var audit))
                if (audit.SessionId == sessionId) return audit;
            return null;
        }
    }

    public void Clear() { lock (_gate) _pending.Clear(); }
    public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FocusAuditEvent>>([]);
}
