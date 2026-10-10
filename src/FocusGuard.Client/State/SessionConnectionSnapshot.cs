using FocusGuard.Contracts;

namespace FocusGuard.Client.State;

public enum SessionConnectionState
{
    Unknown,
    Connected,
    Disconnected
}

public sealed record SessionConnectionSnapshot(
    SessionConnectionState Connection,
    StatusResponse? LastStatus,
    DateTimeOffset? LastConfirmedUtc)
{
    public static SessionConnectionSnapshot Initial { get; } = new(SessionConnectionState.Unknown, null, null);
    public bool HasConfirmedStatus => LastStatus is not null;
    public bool IsFresh => Connection == SessionConnectionState.Connected && LastStatus?.ServiceReady == true;
    public bool IsStale => HasConfirmedStatus && !IsFresh;
}

/// <summary>Read evidence only; never advances phases or expires a session locally.</summary>
public sealed class SessionConnectionTracker(TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private SessionConnectionSnapshot _snapshot = SessionConnectionSnapshot.Initial;

    public SessionConnectionSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    public void Observe(StatusResponse status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            _snapshot = status.ServiceReady
                ? new(SessionConnectionState.Connected, status, _clock.GetUtcNow().ToUniversalTime())
                : _snapshot with { Connection = SessionConnectionState.Disconnected };
        }
    }

    public void MarkDisconnected()
    {
        lock (_gate) _snapshot = _snapshot with { Connection = SessionConnectionState.Disconnected };
    }
}
