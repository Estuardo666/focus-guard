using FocusGuard.Application;
using FocusGuard.Domain;
using FocusGuard.Infrastructure.Windows.Processes;
using FocusGuard.Monitoring;
using FocusGuard.Service;
using FocusGuard.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
#if FOCUSGUARD_PRODUCTION
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
builder.Services.AddSingleton<FocusGuard.Contracts.IIpcServerSecurity>(
    new FocusGuard.Ipc.Windows.AuthenticatedIpcServer(AppContext.BaseDirectory));
#endif
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = DevelopmentServiceConstants.ServiceName;
});
builder.Logging.ClearProviders();
builder.Services.AddSingleton<DevelopmentServiceState>();
builder.Services.AddSingleton<ExtensionHealthState>();
builder.Services.AddSingleton<IStrictSessionStore, StrictSessionStore>();
builder.Services.AddSingleton<ProcessMonitorPolicyReceiver>();
builder.Services.AddSingleton<ProcessAgentPolicyRelay>();
builder.Services.AddSingleton<IPolicyDistributionTarget>(services =>
    services.GetRequiredService<ProcessAgentPolicyRelay>());
builder.Services.AddSingleton<IWindowsProcessGateway, WindowsProcessGateway>();
builder.Services.AddSingleton<IApplicationProcessController>(services =>
    new WindowsApplicationProcessController(
        new ProcessTerminationDeveloperOptions(true, [], AllowAnyNonProtectedTarget: true),
        services.GetRequiredService<IWindowsProcessGateway>()));
builder.Services.AddSingleton<PolicyDistributionCoordinator>();
builder.Services.AddSingleton<IFocusAuditStore>(_ => new JsonLinesFocusAuditStore(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FocusGuard",
        "audit",
        "events.jsonl")));
// Session history and the capture inbox live in one database next to the audit log.
builder.Services.AddSingleton(_ => new SqliteSessionHistoryStore(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FocusGuard",
        "history.db")));
builder.Services.AddSingleton<ISessionHistoryStore>(services =>
    services.GetRequiredService<SqliteSessionHistoryStore>());
builder.Services.AddSingleton<ICaptureStore>(services =>
    services.GetRequiredService<SqliteSessionHistoryStore>());
builder.Services.AddSingleton<IFocusService>(services =>
{
    var health = services.GetRequiredService<ExtensionHealthState>();
    return new InMemoryFocusService(
        strictSessionStore: services.GetRequiredService<IStrictSessionStore>(),
        extensionReadyProvider: () => health.IsExtensionReady(DateTimeOffset.UtcNow),
        processReadyProvider: () => services.GetRequiredService<ProcessAgentPolicyRelay>().IsReady(),
        policyDistribution: services.GetRequiredService<PolicyDistributionCoordinator>(),
        auditStore: services.GetRequiredService<IFocusAuditStore>(),
        captureStore: services.GetRequiredService<ICaptureStore>(),
        historyStore: services.GetRequiredService<ISessionHistoryStore>());
});
builder.Services.AddHostedService<DevelopmentServiceWorker>();
builder.Services.AddHostedService<FocusSessionExpirationWorker>();
builder.Services.AddSingleton<DesktopWorkflowService>();
builder.Services.AddHostedService<WeeklyScheduleWorker>();
builder.Services.AddHostedService<ExtensionHealthPipeService>();
builder.Services.AddHostedService<ProcessAgentPipeService>();
builder.Services.AddHostedService<FocusControlPipeService>();
#if !FOCUSGUARD_PRODUCTION
builder.Services.AddHostedService<RebootExpirationDrillWorker>();
#endif
builder.Services.AddHostedService<StrictPolicyPersistenceWorker>();
builder.Services.AddSingleton(_ => new NormalSessionJournal(NormalSessionJournal.DefaultPath));
builder.Services.AddHostedService<NormalSessionJournalWorker>();
// Registered last: hosted services stop in reverse order, so this runs first on shutdown.
builder.Services.AddHostedService<NormalSessionShutdownRecorder>();

await builder.Build().RunAsync();
