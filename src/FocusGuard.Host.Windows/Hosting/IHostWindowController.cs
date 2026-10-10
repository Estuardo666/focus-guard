namespace FocusGuard.Host.Windows.Hosting;

internal sealed record HostWindowPresentation(bool Compact, bool AlwaysOnTop, bool DetailsExpanded);

internal interface IHostWindowController
{
    ValueTask<HostWindowPresentation> SetCompactModeAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<HostWindowPresentation> SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<HostWindowPresentation> SetDetailsExpandedAsync(bool enabled, CancellationToken cancellationToken);
}
