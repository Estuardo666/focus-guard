using FocusGuard.Domain;

namespace FocusGuard.Monitoring;

public sealed class ProcessMonitorPolicyReceiver(TimeProvider? timeProvider = null, TimeSpan? confirmationLifetime = null)
    : IConfirmedPolicyDistributionTarget
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan? _confirmationLifetime = ValidateLifetime(confirmationLifetime);
    private long _confirmedAtTimestamp;
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
            if (_confirmed) _confirmedAtTimestamp = _timeProvider.GetTimestamp();
        }

        return ValueTask.CompletedTask;
    }

    private void ClearIfExpiredNoLock()
    {
        if (_activeDelivery is not null
            && (_activeDelivery.Policy.EndUtc <= _timeProvider.GetUtcNow().ToUniversalTime()
                || (_confirmed && _confirmationLifetime is TimeSpan lifetime
                    && _timeProvider.GetElapsedTime(_confirmedAtTimestamp) >= lifetime)))
        {
            _activeDelivery = null;
            _confirmed = false;
        }
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _activeDelivery = null;
            _confirmed = false;
        }
        return ValueTask.CompletedTask;
    }

    private static TimeSpan? ValidateLifetime(TimeSpan? lifetime)
    {
        if (lifetime is TimeSpan value && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        return lifetime;
    }
}
