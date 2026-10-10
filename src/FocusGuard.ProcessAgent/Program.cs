using FocusGuard.Infrastructure.Windows.Processes;
using FocusGuard.Monitoring;
using FocusGuard.ProcessAgent;
using FocusGuard.Service;
using Microsoft.Extensions.Logging.Abstractions;

using var singleInstance = new Mutex(
    initiallyOwned: true,
    name: ProcessAgentStartupConfiguration.SingleInstanceName,
    createdNew: out var isFirstInstance);
if (!isFirstInstance)
{
    return;
}

#if FOCUSGUARD_PRODUCTION
if (!OperatingSystem.IsWindows()) return;
using var authenticatedPipes = new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory(AppContext.BaseDirectory,
    FocusGuard.Ipc.Windows.IpcClientRole.ProcessAgent);
FocusGuard.Contracts.PipeConnectionFactories.Current = authenticatedPipes;
#endif

var receiver = new ProcessMonitorPolicyReceiver(confirmationLifetime: ProcessAgentConnection.ConfirmationLifetime);
var gateway = new WindowsProcessGateway();
var controller = new WindowsApplicationProcessController(
    new ProcessTerminationDeveloperOptions(true, [], AllowAnyNonProtectedTarget: true), gateway);
var pendingAudit = new AgentAuditStore();
var worker = new ProcessEnforcementWorker(
    receiver, controller, pendingAudit,
    NullLogger<ProcessEnforcementWorker>.Instance);
using var cancellation = new CancellationTokenSource();
await worker.StartAsync(cancellation.Token);
try
{
    await new ProcessAgentConnection(new NamedPipeProcessAgentClient(), receiver, pendingAudit)
        .RunAsync(cancellation.Token);
}
finally { await worker.StopAsync(CancellationToken.None); }
