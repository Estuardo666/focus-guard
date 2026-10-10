using System.IO.Pipes;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.NativeMessaging;

#if FOCUSGUARD_PRODUCTION
if (args.Length == 0)
{
    return Fail("Usage: status | raw-status | activity | probe-agent-role-denied");
}
if (string.Equals(args[0], "activate-strict", StringComparison.Ordinal))
{
    return Fail("Strict sessions must be started through the reviewed FocusGuard UI; Diagnostic cannot activate sessions.");
}
if (string.Equals(args[0], "acknowledge", StringComparison.Ordinal))
{
    return Fail("Diagnostic cannot acknowledge Native Messaging health; that channel belongs to the extension host.");
}
#else
if (args.Length == 0)
{
    return Fail("Usage: activate-strict <seconds> | status | acknowledge | activity | probe-agent-role-denied");
}
#endif

const string ProductionControlPipe = "FocusGuard.Control.v2";
#if !FOCUSGUARD_PRODUCTION
const string ProductionExtensionHealthPipe = "FocusGuard.ExtensionHealth.v1";
#endif
#if FOCUSGUARD_PRODUCTION
if (!OperatingSystem.IsWindows()) return 2;
using var authenticatedPipes = new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory(AppContext.BaseDirectory,
    FocusGuard.Ipc.Windows.IpcClientRole.Diagnostic);
FocusGuard.Contracts.PipeConnectionFactories.Current = authenticatedPipes;
#endif
var control = new NamedPipeFocusControlClient(ProductionControlPipe);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
try
{
    switch (args[0])
    {
        case "probe-agent-role-denied":
            if (args.Length != 1) return Fail("probe-agent-role-denied takes no arguments.");
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var request = new ProcessAgentRequest(ProcessAgentProtocol.CurrentVersion,
                    ProcessAgentProtocol.HeartbeatType, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
                await using var pipe = await PipeConnectionFactories.Current.ConnectAsync(
                    ProcessAgentProtocol.PipeName, deadline.Token);
                await BoundedPipeFraming.WriteAsync(pipe, ProcessAgentProtocol.WriteRequest(request), deadline.Token);
                var response = await BoundedPipeFraming.ReadAsync(pipe, deadline.Token);
                if (response is null)
                {
                    Console.WriteLine("DENIED_EOF: installed Diagnostic client was rejected by the ProcessAgent endpoint.");
                    return 0;
                }

                var parsed = ProcessAgentProtocol.ReadResponse(response);
                return Fail(parsed.IsValid
                    ? "Unexpected ProcessAgent response to the installed Diagnostic client."
                    : "Unexpected non-empty ProcessAgent response to the installed Diagnostic client.");
            }

#if !FOCUSGUARD_PRODUCTION
        case "activate-strict":
            if (args.Length != 2 || !int.TryParse(args[1], out var durationSeconds))
            {
                return Fail("activate-strict requires an integer duration in seconds.");
            }
            var recovery = RecoveryCodeService.Generate();
            var activation = await control.ActivateAsync(
                new ActivateSessionRequest(
                    Guid.NewGuid(),
                    durationSeconds,
                    true,
                    60,
                    recovery.Credential,
                    ApplicationRules:
                    [new ApplicationPolicyRule(
                        Guid.NewGuid(),
                        "Block",
                        "FileName",
                        "FocusGuard.TestTarget.exe")],
                    WebsiteRules:
                    [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com")],
                    EnforcementEnabled: true),
                CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                activation
            }, json));
            return activation.Accepted ? 0 : 2;
#endif

        case "status":
            Console.WriteLine(JsonSerializer.Serialize(
                await control.GetStatusAsync(CancellationToken.None), json));
            return 0;

        case "raw-status":
            var correlationId = Guid.NewGuid();
            var statusRequest = IpcProtocol.CreateStatusRequest(correlationId).Value!;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await using var pipe = await PipeConnectionFactories.Current.ConnectAsync(ProductionControlPipe, timeout.Token);
                Console.Error.WriteLine("control: connected");
                await BoundedPipeFraming.WriteAsync(
                    pipe, IpcProtocol.WriteEnvelope(statusRequest), timeout.Token);
                Console.Error.WriteLine("control: request written");
                var bytes = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token)
                    ?? throw new EndOfStreamException();
                Console.Error.WriteLine("control: response read");
                var envelope = IpcProtocol.ReadEnvelope(bytes).Value!;
                Console.WriteLine(envelope.PayloadJson);
                var decodedStatus = IpcProtocol.ReadStatusResponse(envelope);
                Console.WriteLine(JsonSerializer.Serialize(decodedStatus, json));
            }
            return 0;

#if !FOCUSGUARD_PRODUCTION
        case "acknowledge":
            var healthClient = new NamedPipeExtensionHealthClient(ProductionExtensionHealthPipe);
            var first = await healthClient.QueryAsync(
                Request(null), CancellationToken.None);
            if (first.Policy is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(first, json));
                return 3;
            }
            var second = await healthClient.QueryAsync(
                Request(first.Policy), CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(second, json));
            return 0;
#endif

        case "activity":
            Console.WriteLine(JsonSerializer.Serialize(
                await control.GetActivityAsync(200, CancellationToken.None), json));
            return 0;

        default:
            return Fail($"Unknown command: {args[0]}");
    }
}
catch (Exception exception) when (
    exception is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
{
    return Fail(exception.Message);
}

#if !FOCUSGUARD_PRODUCTION
static ExtensionHealthRequest Request(FocusGuard.Domain.PolicyDelivery? delivery) =>
    new(
        ExtensionHealthProtocol.CurrentVersion,
        ExtensionHealthProtocol.HeartbeatType,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        delivery?.PolicyId,
        delivery?.PolicyHash);
#endif

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
