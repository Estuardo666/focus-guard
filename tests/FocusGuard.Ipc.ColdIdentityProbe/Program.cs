using FocusGuard.Ipc.Windows;

if (!OperatingSystem.IsWindows()) return 2;
if (args.Length != 1) return 2;
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var name = "FocusGuard.Tests.ColdIdentity." + Guid.NewGuid();
var security = WindowsAuthenticatedPipeStreams.CreateOwnerSecurity(args[0], args[0]);
await using var server = WindowsAuthenticatedPipeStreams.CreateServer(name, security, true);
var waiting = server.WaitForConnectionAsync(deadline.Token);
await using var client = await WindowsAuthenticatedPipeStreams.ConnectAsync(name, deadline.Token);
await waiting;
await client.WriteAsync(new byte[] { 1 }, deadline.Token);
await server.ReadExactlyAsync(new byte[1], deadline.Token);
Console.WriteLine("ClaimsBeforeCapture=" + AppDomain.CurrentDomain.GetAssemblies()
    .Any(assembly => assembly.GetName().Name == "System.Security.Claims"));
using var observed = WindowsPipeIdentity.CaptureClient(server);
if (observed.UserSid != args[0] || observed.ProcessId != (uint)Environment.ProcessId
    || !observed.LogonSid.StartsWith("S-1-5-5-", StringComparison.Ordinal)) return 3;
Console.WriteLine("ColdCaptureSucceeded");
return 0;
