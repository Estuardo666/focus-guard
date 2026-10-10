using FocusGuard.Client.Platform;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeIcon(int Width, int Height, string Bgra);
internal sealed record BridgeCatalogApplication(string Id, string Name, string Path, BridgeIcon? Icon);
internal sealed record BridgeApplicationCatalog(IReadOnlyList<BridgeCatalogApplication> Items, int Offset, int Total, string Query);

internal sealed partial class NativeBridgeDispatcher
{
    private readonly Dictionary<Guid, InstalledApplication> _catalogApplications = [];
    private readonly Dictionary<Guid, BridgeIcon?> _catalogIcons = [];
    private string? _catalogDocument;

    private async ValueTask<(string? Error, object? Result)> ReadApplicationCatalogAsync(BridgeRequest request, CancellationToken token)
    {
        if (applicationCatalog is null) return ("CatalogUnavailable", null);
        await EnsureApplicationCatalogAsync(request, token).ConfigureAwait(false);
        var query = request.CatalogQuery!.Trim();
        var matches = _catalogApplications.Where(pair => pair.Value.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || pair.Value.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(pair => pair.Value.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var items = new List<BridgeCatalogApplication>();
        foreach (var pair in matches.Skip(request.ProfileOffset).Take(6))
        {
            token.ThrowIfCancellationRequested();
            var icon = await CatalogIconAsync(pair.Key, pair.Value.Path, token).ConfigureAwait(false);
            items.Add(new(pair.Key.ToString("N"), pair.Value.Name, pair.Value.Path, icon));
        }
        return (null, new BridgeApplicationCatalog(items, request.ProfileOffset, matches.Length, query));
    }

    private async Task EnsureApplicationCatalogAsync(BridgeRequest request, CancellationToken token)
    {
        if (applicationCatalog is null) return;
        if (_catalogDocument != request.DocumentToken || request.CatalogRefresh)
        {
            var found = await applicationCatalog.ListAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) throw new OperationCanceledException(token);
            _catalogApplications.Clear(); _catalogIcons.Clear();
            foreach (var app in found.Take(2000).Where(app => !string.IsNullOrWhiteSpace(app.Name) && app.Name.Length <= 160
                && app.Path.Length <= 1024 && Path.IsPathFullyQualified(app.Path) && !app.Path.StartsWith(@"\\", StringComparison.Ordinal)
                && string.Equals(Path.GetExtension(app.Path), ".exe", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(app => app.Path, StringComparer.OrdinalIgnoreCase))
                _catalogApplications.Add(Guid.NewGuid(), app);
            _catalogDocument = request.DocumentToken;
        }
    }

    private async Task<BridgeIcon?> CatalogIconAsync(Guid id, string path, CancellationToken token)
    {
        if (_catalogIcons.TryGetValue(id, out var cached)) return cached;
        var pixels = iconProvider is null ? null : await Task.Run(() => iconProvider.Read(path), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var icon = pixels is null ? null : SmallIcon(pixels);
        _catalogIcons[id] = icon;
        return icon;
    }

    private bool TryGetCatalogApplication(string document, Guid id, out InstalledApplication? app)
    {
        app = null;
        return _catalogDocument == document && _catalogApplications.TryGetValue(id, out app);
    }
    private string CatalogPath(string document, Guid id) => TryGetCatalogApplication(document, id, out var app)
        ? app!.Path : throw new InvalidDataException("Application catalog snapshot changed.");

    private static BridgeIcon SmallIcon(ApplicationIconPixels icon)
    {
        const int size = 32;
        var output = new byte[size * size * 4];
        var input = icon.Pixels.Span;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var source = ((y * icon.Height / size) * icon.Width + x * icon.Width / size) * 4;
                input.Slice(source, 4).CopyTo(output.AsSpan((y * size + x) * 4, 4));
            }
        return new(size, size, Convert.ToBase64String(output));
    }
}
