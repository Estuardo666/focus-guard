using System.IO.Pipes;
using System.Collections.Concurrent;
using FocusGuard.Contracts;
using FocusGuard.Infrastructure.Windows.Processes;
using FocusGuard.Monitoring;
using FocusGuard.Service;
using Microsoft.Extensions.Logging;

var receiver = new ProcessMonitorPolicyReceiver();
var instance = Guid.NewGuid();
FocusGuard.Domain.PolicyDelivery? lastDelivery = null;
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var gateway = new WindowsProcessGateway();
var controller = new WindowsApplicationProcessController(
    new ProcessTerminationDeveloperOptions(true, [], AllowAnyNonProtectedTarget: true), gateway);
var pendingAudit = new AgentAuditStore();
var worker = new ProcessEnforcementWorker(
    receiver, controller, pendingAudit,
    loggerFactory.CreateLogger<ProcessEnforcementWorker>());
using var cancellation = new CancellationTokenSource();
await worker.StartAsync(cancellation.Token);
try
{
    while (true)
    {
        await using var pipe = new NamedPipeClientStream(".", "FocusGuard.ProcessAgent.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(1500); }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await Task.Delay(500, cancellation.Token);
            continue;
        }
        var delivery = lastDelivery;
        pendingAudit.TryPeek(out var audit);
        var request = new ProcessAgentRequest(
            ProcessAgentProtocol.CurrentVersion,
            audit is null ? ProcessAgentProtocol.HeartbeatType : ProcessAgentProtocol.AuditType,
            Guid.NewGuid(), instance, DateTimeOffset.UtcNow,
            delivery?.PolicyId, delivery?.PolicyHash,
            audit?.Executable, audit?.Outcome);
        await BoundedPipeFraming.WriteAsync(pipe, ProcessAgentProtocol.WriteRequest(request), cancellation.Token);
        var responseBytes = await BoundedPipeFraming.ReadAsync(pipe, cancellation.Token);
        if (responseBytes is not null)
        {
            var response = ProcessAgentProtocol.ReadResponse(responseBytes);
            Console.Error.WriteLine($"agent response valid={response.IsValid} policy={response.Value?.Policy?.PolicyId} confirmed={response.Value?.EnforcementConfirmed}");
            if (!response.IsValid) Console.Error.WriteLine(string.Join(";", response.Errors));
            if (response.IsValid && response.Value?.Policy is not null)
            {
                if (audit is not null) pendingAudit.TryDequeue(out _);
                lastDelivery = response.Value.Policy;
                await receiver.ReceiveAsync(response.Value.Policy, cancellation.Token);
                await receiver.SetConfirmedAsync(response.Value.Policy.PolicyId, response.Value.EnforcementConfirmed, cancellation.Token);
            }
            else if (response.IsValid && response.Value?.SessionState != "Active")
            {
                lastDelivery = null;
                var current = receiver.GetActiveDelivery();
                if (current is not null) await receiver.ClearAsync(current.PolicyId, cancellation.Token);
            }
        }
        await Task.Delay(500, cancellation.Token);
    }
}
finally { await worker.StopAsync(CancellationToken.None); }

sealed record PendingAudit(string Executable, string Outcome);

sealed class AgentAuditStore : IFocusAuditStore
{
    private readonly ConcurrentQueue<PendingAudit> _pending = new();

    public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outcome = auditEvent.Outcome switch
        {
            "Terminated" => "Terminated",
            "AlreadyExited" => "Closed",
            _ => "Failed"
        };
        _pending.Enqueue(new PendingAudit(Path.GetFileName(auditEvent.Target), outcome));
        return Task.CompletedTask;
    }

    public bool TryPeek(out PendingAudit? audit) => _pending.TryPeek(out audit);
    public bool TryDequeue(out PendingAudit? audit) => _pending.TryDequeue(out audit);
    public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FocusAuditEvent>>([]);
}
