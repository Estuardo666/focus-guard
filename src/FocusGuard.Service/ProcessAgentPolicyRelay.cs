using FocusGuard.Domain;

namespace FocusGuard.Service;

public sealed class ProcessAgentPolicyRelay(TimeProvider? timeProvider = null) :
    IExternallyAcknowledgedPolicyDistributionTarget
{
    public static readonly TimeSpan HeartbeatFreshness = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private PolicyDelivery? _delivery;
    private bool _confirmed;
    private long _lastHeartbeatUtcTicks;

    public PolicyDelivery? CurrentDelivery
    {
        get { lock (_gate) return _delivery; }
    }

    public bool EnforcementConfirmed
    {
        get { lock (_gate) return _confirmed && IsReadyNoLock(); }
    }

    public void RecordHeartbeat()
    {
        lock (_gate)
        {
            _lastHeartbeatUtcTicks = _timeProvider.GetUtcNow().ToUniversalTime().UtcTicks;
        }
    }

    public bool IsReady()
    {
        lock (_gate) return IsReadyNoLock();
    }

    public ValueTask<PolicyAcknowledgement> ReceiveAsync(
        PolicyDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _delivery = delivery;
            _confirmed = false;
        }
        return ValueTask.FromResult(new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
    }

    public ValueTask SetConfirmedAsync(
        Guid policyId,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _confirmed = _delivery?.PolicyId == policyId && confirmed && IsReadyNoLock();
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_delivery?.PolicyId == policyId)
            {
                _delivery = null;
                _confirmed = false;
            }
        }
        return ValueTask.CompletedTask;
    }

    private bool IsReadyNoLock() =>
        _lastHeartbeatUtcTicks != 0
        && _timeProvider.GetUtcNow().ToUniversalTime()
            - new DateTimeOffset(_lastHeartbeatUtcTicks, TimeSpan.Zero) <= HeartbeatFreshness;
}
