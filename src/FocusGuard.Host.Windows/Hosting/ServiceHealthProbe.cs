using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Ipc.Windows;

namespace FocusGuard.Host.Windows.Hosting;

internal sealed record ServiceHealthProbeResult(int ExitCode, string Stage, IpcFailureDiagnostic? Failure);

/// <summary>
/// The installer's post-install check: eight status reads over the authenticated pipe, as the signed-in owner. It used to
/// live in the Avalonia shell; the React host now answers it so the installer no longer needs that shell.
/// </summary>
internal static class ServiceHealthProbe
{
    internal const string Argument = "--service-health-check";
    internal const string OwnerProbeArgument = "--installer-owner-probe";
    private const int ProbeCount = 8;

    internal static async Task<ServiceHealthProbeResult> RunAsync(Func<IFocusControlClient> createClient, CancellationToken cancellationToken)
    {
        var stage = "Bootstrap";
        try
        {
            var client = createClient();
            stage = "Exchange";
            var statuses = await Task.WhenAll(Enumerable.Range(0, ProbeCount)
                .Select(_ => client.GetStatusAsync(cancellationToken).AsTask())).ConfigureAwait(false);
            var healthy = statuses.All(status => status.ServiceReady);
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
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "diagnostics");
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
