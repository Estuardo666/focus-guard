using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Monitoring;

namespace FocusGuard.ProcessAgent;

/// <summary>One actor owns the service exchange; only fresh validated replies authorize this receiver.</summary>
public sealed class ProcessAgentConnection(
    IProcessAgentClient client, ProcessMonitorPolicyReceiver receiver, AgentAuditStore auditStore, TimeProvider? clock = null)
{
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Guid _instance = Guid.NewGuid();
    private Guid? _serviceInstance;
    private PolicyDelivery? _held;
    private long? _lastReplyTimestamp;

    public async Task<bool> TickAsync(CancellationToken token)
    {
        try
        {
            if (_lastReplyTimestamp is long timestamp && _clock.GetElapsedTime(timestamp) >= ConfirmationLifetime)
                await ResetAsync().ConfigureAwait(false);
            var audit = _held is not null && receiver.GetActiveDelivery() is not null
                ? auditStore.TakeForSession(_held.Policy.SessionId) : null;
            var request = new ProcessAgentRequest(ProcessAgentProtocol.CurrentVersion,
                audit is null ? ProcessAgentProtocol.HeartbeatType : ProcessAgentProtocol.AuditType,
                Guid.NewGuid(), _instance, _clock.GetUtcNow().ToUniversalTime(), _held?.PolicyId, _held?.PolicyHash,
                audit?.Executable, audit?.Outcome);
            var response = await client.ExchangeAsync(request, token).ConfigureAwait(false);
            // Validate again at the actor boundary so an alternate transport cannot grant stale proof.
            var decoded = ProcessAgentProtocol.ReadResponse(ProcessAgentProtocol.WriteResponse(response));
            if (!decoded.IsValid || response.CorrelationId != request.CorrelationId || !response.ServiceReady
                || (_clock.GetUtcNow().ToUniversalTime() - response.ObservedAtUtc).Duration() > ProcessAgentProtocol.MaximumClockSkew)
                throw new InvalidDataException("Agent service proof is invalid.");
            var changedService = _serviceInstance is Guid previous && previous != response.ServiceInstanceId;
            if (changedService) await ResetAsync().ConfigureAwait(false);
            _serviceInstance = response.ServiceInstanceId;
            _lastReplyTimestamp = _clock.GetTimestamp();
            if (response.Policy is not { } policy)
            {
                await ResetAsync().ConfigureAwait(false);
                return true;
            }
            if (response.SessionState is not ("Active" or "Activating")
                || policy.Policy.EndUtc <= _clock.GetUtcNow().ToUniversalTime())
                throw new InvalidDataException("Inactive response cannot grant a policy.");
            if (_held?.PolicyId != policy.PolicyId || _held.PolicyHash != policy.PolicyHash)
            {
                await receiver.ReceiveAsync(policy, token).ConfigureAwait(false);
                _held = policy;
            }
            var acknowledged = request.AcknowledgedPolicyId == policy.PolicyId
                && request.AcknowledgedPolicyHash == policy.PolicyHash;
            if (response.EnforcementConfirmed && !acknowledged)
                throw new InvalidDataException("Confirmation does not match this request acknowledgement.");
            await receiver.SetConfirmedAsync(policy.PolicyId,
                response.EnforcementConfirmed && acknowledged && !changedService, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException
            or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException)
        {
            await ResetAsync().ConfigureAwait(false);
            return false;
        }
    }

    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await TickAsync(token).ConfigureAwait(false);
                await Task.Delay(PollInterval, _clock, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { await ResetAsync().ConfigureAwait(false); }
    }

    private async ValueTask ResetAsync()
    {
        await receiver.ResetAsync(CancellationToken.None).ConfigureAwait(false);
        _held = null;
        _serviceInstance = null;
        _lastReplyTimestamp = null;
        auditStore.Clear();
    }
}
