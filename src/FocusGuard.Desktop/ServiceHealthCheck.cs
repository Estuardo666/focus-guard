using FocusGuard.Client;
using FocusGuard.Contracts;

namespace FocusGuard.Desktop;

internal static class ServiceHealthCheck
{
    internal const string Argument = "--service-health-check";
    private const int ProbeCount = 8;

    public static async Task<bool> RunAsync(
        IFocusControlClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var probes = Enumerable.Range(0, ProbeCount)
            .Select(_ => client.GetStatusAsync(cancellationToken).AsTask());
        var statuses = await Task.WhenAll(probes).ConfigureAwait(false);
        return statuses.All(status => status.ServiceReady);
    }
}
