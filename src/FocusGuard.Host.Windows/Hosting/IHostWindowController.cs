namespace FocusGuard.Host.Windows.Hosting;

internal sealed record HostWindowPresentation(bool Compact, bool AlwaysOnTop, bool DetailsExpanded);

internal interface IHostWindowController
{
    ValueTask<HostWindowPresentation> SetCompactModeAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<HostWindowPresentation> SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken);
    ValueTask<HostWindowPresentation> SetDetailsExpandedAsync(bool enabled, CancellationToken cancellationToken);
}

/// <summary>Shell commands raised by the tray panel and the popups (never by an arbitrary page: the bridge validates them).</summary>
internal interface IHostShellController
{
    ValueTask<bool> ExecuteAsync(FocusGuard.Host.Windows.Bridge.HostAppCommand command, CancellationToken cancellationToken);
}
