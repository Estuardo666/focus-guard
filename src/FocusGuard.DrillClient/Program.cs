using System.IO.Pipes;
using System.Text.Json;
using FocusGuard.Contracts;
using FocusGuard.Desktop;
using FocusGuard.NativeMessaging;

if (args.Length == 0)
{
    return Fail("Usage: activate-strict <seconds> | status | acknowledge | activity");
}

const string ProductionControlPipe = "FocusGuard.Control.v1";
const string ProductionExtensionHealthPipe = "FocusGuard.ExtensionHealth.v1";
var control = new NamedPipeFocusControlClient(ProductionControlPipe);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
try
{
    switch (args[0])
    {
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

        case "status":
            Console.WriteLine(JsonSerializer.Serialize(
                await control.GetStatusAsync(CancellationToken.None), json));
            return 0;

        case "raw-status":
            var correlationId = Guid.NewGuid();
            var statusRequest = IpcProtocol.CreateStatusRequest(correlationId).Value!;
            await using (var pipe = new NamedPipeClientStream(
                ".", ProductionControlPipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                Console.Error.WriteLine($"control: connecting to {ProductionControlPipe}");
                await pipe.ConnectAsync(timeout.Token);
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

static ExtensionHealthRequest Request(FocusGuard.Domain.PolicyDelivery? delivery) =>
    new(
        ExtensionHealthProtocol.CurrentVersion,
        ExtensionHealthProtocol.HeartbeatType,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        delivery?.PolicyId,
        delivery?.PolicyHash);

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
