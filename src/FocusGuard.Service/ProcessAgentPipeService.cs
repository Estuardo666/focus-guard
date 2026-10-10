using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed class ProcessAgentPipeService(
    ProcessAgentPolicyRelay relay,
    PolicyDistributionCoordinator coordinator,
    IFocusService focusService,
    IFocusAuditStore auditStore,
    TimeProvider? timeProvider = null,
    string? pipeName = null,
    IIpcServerSecurity? security = null) : BackgroundService
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly string _pipeName = pipeName ?? ProcessAgentProtocol.PipeName;
    private readonly Guid _serviceInstanceId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NamedPipeServerStream? pending = null;
        var firstInstance = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                pending ??= security is null ? CreatePipe(_pipeName) : security.CreatePipe(_pipeName, firstInstance, 2);
                firstInstance = false;
                var pipe = pending;
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken);
                    pending = null;
                    if (security is not null) pending = security.CreatePipe(_pipeName, false, 2);
                    await HandleConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    // The request deadline releases the slot, not the authoritative session.
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException
                    or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // Malformed/disconnected clients are isolated to their own connection.
                }
                finally
                {
                    if (pending == pipe)
                        pending = stoppingToken.IsCancellationRequested ? null
                            : security is null ? CreatePipe(_pipeName) : security.CreatePipe(_pipeName, false, 2);
                    await pipe.DisposeAsync();
                }
            }
        }
        finally { if (pending is not null) await pending.DisposeAsync(); }
    }

    internal async Task HandleConnectionAsync(Stream pipe, CancellationToken stoppingToken)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout, _clock);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
        var requestToken = requestCancellation.Token;
        var payload = await BoundedPipeFraming.ReadAsync(pipe, requestToken);
        if (payload is null) return;
        var now = _clock.GetUtcNow().ToUniversalTime();
        var parsed = ProcessAgentProtocol.ReadRequest(payload, now);
        if (!parsed.IsValid) return;
        var request = parsed.Value!;
        if (security is not null) security.AuthorizeAgent(pipe as NamedPipeServerStream
            ?? throw new UnauthorizedAccessException("Native pipe proof required."), request.Type);
        relay.RecordHeartbeat();
        var ack = request.AcknowledgedPolicyId is Guid id
            ? new PolicyAcknowledgement(id, request.AcknowledgedPolicyHash!) : null;
        // Polling status first lets a cycle phase boundary redistribute before the
        // agent is handed a delivery, so break allowances are never a beat late.
        var status = await focusService.GetStatusAsync(requestToken);
        var delivery = coordinator.ExchangeProcessAcknowledgement(ack);
        if (delivery is null && ack is not null)
        {
            delivery = coordinator.ConfirmExternalProcessAcknowledgement(ack);
        }
        if (ack is not null)
        {
            await relay.SetConfirmedAsync(
                ack.PolicyId,
                coordinator.IsProcessConnected,
                requestToken);
        }
        if (request.Type == ProcessAgentProtocol.AuditType
            && coordinator.IsProcessConnected
            && status.SessionId is Guid sessionId)
        {
            await auditStore.WriteAsync(
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    now,
                    FocusAuditEventKind.ApplicationBlocked,
                    request.BlockedExecutable!,
                    request.Outcome!,
                    sessionId),
                requestToken);
        }
        var response = new ProcessAgentResponse(
            ProcessAgentProtocol.CurrentVersion,
            ProcessAgentProtocol.StatusResponseType,
            request.CorrelationId,
            _serviceInstanceId, true, coordinator.IsProcessConnected,
            status.SessionState, status.RemainingSeconds, now, delivery);
        await BoundedPipeFraming.WriteAsync(pipe, ProcessAgentProtocol.WriteResponse(response), requestToken);
    }

    internal static NamedPipeServerStream CreatePipe(string? pipeName = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                pipeName ?? ProcessAgentProtocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        foreach (var sidType in new[]
                 {
                     WellKnownSidType.LocalServiceSid,
                     WellKnownSidType.LocalSystemSid,
                     WellKnownSidType.BuiltinAdministratorsSid
                 })
        {
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(sidType, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName ?? ProcessAgentProtocol.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            ProcessAgentProtocol.MaximumMessageBytes + sizeof(uint),
            ProcessAgentProtocol.MaximumMessageBytes + sizeof(uint),
            security);
    }
}
