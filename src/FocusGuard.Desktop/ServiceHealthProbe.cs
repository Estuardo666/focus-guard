using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Ipc.Windows;

namespace FocusGuard.Desktop;

internal sealed record ServiceHealthProbeResult(int ExitCode, string Stage, IpcFailureDiagnostic? Failure);

internal static class ServiceHealthProbe
{
    internal static async Task<ServiceHealthProbeResult> RunAsync(
        Func<IFocusControlClient> createClient, CancellationToken cancellationToken)
    {
        var stage = "Bootstrap";
        try
        {
            var client = createClient();
            stage = "Exchange";
            var healthy = await ServiceHealthCheck.RunAsync(client, cancellationToken).ConfigureAwait(false);
            return new(healthy ? 0 : 14, healthy ? "Ready" : "NotReady", null);
        }
        catch (Exception exception)
        {
            return new(stage == "Bootstrap" ? 11 : 12, stage, IpcFailureDiagnostic.FromException(exception));
        }
    }

    internal static void WriteDiagnostic(ServiceHealthProbeResult result)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusGuard", "diagnostics");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "health-check.json"),
                JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow, Result = result }));
        }
        catch (Exception)
        {
            // Diagnostic failure must never turn a failed health check into success.
        }
    }
}
