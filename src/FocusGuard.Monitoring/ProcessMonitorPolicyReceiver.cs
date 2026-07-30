using FocusGuard.Domain;

namespace FocusGuard.Monitoring;

public sealed class ProcessMonitorPolicyReceiver(TimeProvider? timeProvider = null)
    : IConfirmedPolicyDistributionTarget
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private PolicyDelivery? _activeDelivery;
    private bool _confirmed;

    public PolicyDelivery? GetActiveDelivery()
    {
        lock (_gate)
        {
            ClearIfExpiredNoLock();
            return _confirmed ? _activeDelivery : null;
        }
    }

    public ValueTask<PolicyAcknowledgement> ReceiveAsync(
        PolicyDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();
        if (!delivery.HasValidIntegrity()
            || delivery.Policy.EndUtc <= _timeProvider.GetUtcNow().ToUniversalTime())
        {
            throw new InvalidDataException("Process monitor rejected an invalid or expired policy delivery.");
        }

        lock (_gate)
        {
            _activeDelivery = delivery;
            _confirmed = false;
        }

        return ValueTask.FromResult(
            new PolicyAcknowledgement(delivery.PolicyId, delivery.PolicyHash));
    }

    public ValueTask ClearAsync(Guid policyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_activeDelivery?.PolicyId == policyId)
            {
                _activeDelivery = null;
                _confirmed = false;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetConfirmedAsync(
        Guid policyId,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ClearIfExpiredNoLock();
            _confirmed = _activeDelivery?.PolicyId == policyId && confirmed;
        }

        return ValueTask.CompletedTask;
    }

    private void ClearIfExpiredNoLock()
    {
        if (_activeDelivery is not null
            && _activeDelivery.Policy.EndUtc <= _timeProvider.GetUtcNow().ToUniversalTime())
        {
            _activeDelivery = null;
            _confirmed = false;
        }
    }
}
