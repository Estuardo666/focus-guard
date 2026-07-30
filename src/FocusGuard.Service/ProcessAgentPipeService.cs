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
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                var payload = await BoundedPipeFraming.ReadAsync(pipe, stoppingToken);
                if (payload is null) continue;
                var now = _clock.GetUtcNow().ToUniversalTime();
                var parsed = ProcessAgentProtocol.ReadRequest(payload, now);
                if (!parsed.IsValid) continue;
                var request = parsed.Value!;
                relay.RecordHeartbeat();
                var ack = request.AcknowledgedPolicyId is Guid id
                    ? new PolicyAcknowledgement(id, request.AcknowledgedPolicyHash!) : null;
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
                        stoppingToken);
                }
                var status = await focusService.GetStatusAsync(stoppingToken);
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
                        stoppingToken);
                }
                var response = new ProcessAgentResponse(
                    ProcessAgentProtocol.CurrentVersion,
                    ProcessAgentProtocol.StatusResponseType,
                    request.CorrelationId,
                    Guid.NewGuid(), true, coordinator.IsProcessConnected,
                    status.SessionState, status.RemainingSeconds, now, delivery);
                await BoundedPipeFraming.WriteAsync(pipe, ProcessAgentProtocol.WriteResponse(response), stoppingToken);
            }
            catch (IOException) { }
        }
    }

    internal static NamedPipeServerStream CreatePipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                ProcessAgentProtocol.PipeName, PipeDirection.InOut, 1,
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
            ProcessAgentProtocol.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            ProcessAgentProtocol.MaximumMessageBytes + sizeof(uint),
            ProcessAgentProtocol.MaximumMessageBytes + sizeof(uint),
            security);
    }
}
