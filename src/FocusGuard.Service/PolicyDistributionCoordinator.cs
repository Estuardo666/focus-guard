using FocusGuard.Domain;

namespace FocusGuard.Service;

public sealed class PolicyDistributionCoordinator(
    IPolicyDistributionTarget processMonitor,
    TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private readonly IPolicyDistributionTarget _processMonitor =
        processMonitor ?? throw new ArgumentNullException(nameof(processMonitor));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private PolicyDelivery? _delivery;
    private PolicyAcknowledgement? _processAcknowledgement;
    private PolicyAcknowledgement? _browserAcknowledgement;

    public async ValueTask<PolicyDelivery> DistributeAsync(
        EnforcementPolicy policy,
        CancellationToken cancellationToken)
    {
        var delivery = PolicyDelivery.Create(policy);
        PolicyAcknowledgement? processAcknowledgement = null;
        try
        {
            processAcknowledgement = await _processMonitor.ReceiveAsync(
                delivery,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or TimeoutException)
        {
            // The authoritative service retains a degraded pending delivery.
        }

        bool processConfirmed;
        lock (_gate)
        {
            _delivery = delivery;
            _processAcknowledgement = _processMonitor is IExternallyAcknowledgedPolicyDistributionTarget
                ? null
                : Matches(delivery, processAcknowledgement)
                ? processAcknowledgement
                : null;
            _browserAcknowledgement = null;
            processConfirmed = Matches(delivery, _processAcknowledgement);
        }

        if (_processMonitor is IConfirmedPolicyDistributionTarget target)
        {
            await target.SetConfirmedAsync(
                delivery.PolicyId,
                processConfirmed,
                cancellationToken).ConfigureAwait(false);
        }

        return delivery;
    }

    public PolicyDelivery? ExchangeBrowserAcknowledgement(PolicyAcknowledgement? acknowledgement)
    {
        lock (_gate)
        {
            ExpireNoLock();
            if (_delivery is null)
            {
                return null;
            }

            _browserAcknowledgement = Matches(_delivery, acknowledgement)
                ? acknowledgement
                : null;
            return _delivery;
        }
    }

    internal PolicyDelivery? GetCurrentDeliveryForBrowserStatus()
    {
        lock (_gate)
        {
            ExpireNoLock();
            return _delivery;
        }
    }

    public PolicyDelivery? ExchangeProcessAcknowledgement(PolicyAcknowledgement? acknowledgement)
    {
        PolicyDelivery? delivery;
        bool confirmed;
        lock (_gate)
        {
            ExpireNoLock();
            if (_delivery is null)
            {
                return null;
            }
            _processAcknowledgement = Matches(_delivery, acknowledgement)
                ? acknowledgement
                : null;
            delivery = _delivery;
            confirmed = Matches(delivery, _processAcknowledgement);
        }
        if (_processMonitor is IConfirmedPolicyDistributionTarget target)
        {
            target.SetConfirmedAsync(delivery.PolicyId, confirmed, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }
        return delivery;
    }

    public PolicyDelivery? ConfirmExternalProcessAcknowledgement(PolicyAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        PolicyDelivery? delivery;
        lock (_gate)
        {
            ExpireNoLock();
            if (_delivery is null || !Matches(_delivery, acknowledgement)) return null;
            _processAcknowledgement = acknowledgement;
            delivery = _delivery;
        }

        if (_processMonitor is IConfirmedPolicyDistributionTarget target)
        {
            target.SetConfirmedAsync(delivery.PolicyId, true, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }

        return delivery;
    }

    public bool IsProcessConnected
    {
        get
        {
            lock (_gate)
            {
                ExpireNoLock();
                return _delivery is not null
                    && Matches(_delivery, _processAcknowledgement);
            }
        }
    }

    public bool IsBrowserConnected
    {
        get
        {
            lock (_gate)
            {
                ExpireNoLock();
                return _delivery is not null
                    && Matches(_delivery, _browserAcknowledgement);
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                ExpireNoLock();
                var applicationRequested = _delivery is not null && HasApplicationRules(_delivery.Policy);
                var websiteRequested = _delivery is not null && HasWebsiteRules(_delivery.Policy);
                return _delivery is not null
                    && (!applicationRequested
                        || Matches(_delivery, _processAcknowledgement))
                    && (!websiteRequested
                        || Matches(_delivery, _browserAcknowledgement));
            }
        }
    }

    public bool HasConnectedRequestedChannel
    {
        get
        {
            lock (_gate)
            {
                ExpireNoLock();
                if (_delivery is null)
                {
                    return false;
                }

                var applicationRequested = HasApplicationRules(_delivery.Policy);
                var websiteRequested = HasWebsiteRules(_delivery.Policy);

                return (!applicationRequested && !websiteRequested)
                    || (applicationRequested
                        && Matches(_delivery, _processAcknowledgement))
                    || (websiteRequested
                        && Matches(_delivery, _browserAcknowledgement));
            }
        }
    }

    public void ReconcileBrowserHealth(bool browserReady)
    {
        lock (_gate)
        {
            ExpireNoLock();
            if (browserReady || _delivery is null)
            {
                return;
            }

            // Browser acknowledgement is its own lease. Losing it must not revoke
            // a separately authenticated process-agent lease.
            _browserAcknowledgement = null;
        }
    }

    public void ReconcileProcessHealth(bool processReady)
    {
        PolicyDelivery? delivery;
        lock (_gate)
        {
            ExpireNoLock();
            delivery = _delivery;
            if (processReady || delivery is null)
            {
                return;
            }
            _processAcknowledgement = null;
        }
        if (_processMonitor is IConfirmedPolicyDistributionTarget target)
        {
            target.SetConfirmedAsync(delivery.PolicyId, false, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        PolicyDelivery? delivery;
        lock (_gate)
        {
            delivery = _delivery;
            _delivery = null;
            _processAcknowledgement = null;
            _browserAcknowledgement = null;
        }

        if (delivery is not null)
        {
            if (_processMonitor is IConfirmedPolicyDistributionTarget target)
            {
                await target.SetConfirmedAsync(delivery.PolicyId, false, cancellationToken)
                    .ConfigureAwait(false);
            }
            await _processMonitor.ClearAsync(delivery.PolicyId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void ExpireNoLock()
    {
        if (_delivery is not null
            && _delivery.Policy.EndUtc <= _timeProvider.GetUtcNow().ToUniversalTime())
        {
            var expiredPolicyId = _delivery.PolicyId;
            _delivery = null;
            _processAcknowledgement = null;
            _browserAcknowledgement = null;
            if (_processMonitor is IConfirmedPolicyDistributionTarget target)
            {
                target.SetConfirmedAsync(expiredPolicyId, false, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
            }
        }
    }

    private static bool Matches(PolicyDelivery delivery, PolicyAcknowledgement? acknowledgement) =>
        acknowledgement?.PolicyId == delivery.PolicyId
        && string.Equals(
            acknowledgement.PolicyHash,
            delivery.PolicyHash,
            StringComparison.Ordinal);

    private static bool HasApplicationRules(EnforcementPolicy policy) =>
        policy.ApplicationRules.Count > 0 || policy.BreakApplicationRules is { Count: > 0 };

    private static bool HasWebsiteRules(EnforcementPolicy policy) =>
        policy.WebsiteRules.Count > 0 || policy.BreakWebsiteRules is { Count: > 0 };
}
