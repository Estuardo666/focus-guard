namespace FocusGuard.Host.Windows.Hosting;

internal sealed class DocumentLifetime : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _documentCancellation;
    private string? _token;
    private long _generation;

    public DocumentLease BeginDocument()
    {
        lock (_sync)
        {
            InvalidateLocked();
            _documentCancellation = new CancellationTokenSource();
            _token = Guid.NewGuid().ToString("N");
            _generation++;
            return new DocumentLease(_generation, _token, _documentCancellation.Token);
        }
    }

    public bool IsCurrent(DocumentLease lease)
    {
        lock (_sync)
        {
            return _generation == lease.Generation
                && string.Equals(_token, lease.Token, StringComparison.Ordinal)
                && _documentCancellation is { IsCancellationRequested: false };
        }
    }

    public bool TryGetCurrent(string token, out DocumentLease lease)
    {
        lock (_sync)
        {
            if (_token is not null
                && _documentCancellation is { IsCancellationRequested: false } source
                && string.Equals(_token, token, StringComparison.Ordinal))
            {
                lease = new DocumentLease(_generation, _token, source.Token);
                return true;
            }
        }

        lease = default;
        return false;
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            InvalidateLocked();
            _generation++;
        }
    }

    public void Dispose() => Invalidate();

    private void InvalidateLocked()
    {
        _token = null;
        _documentCancellation?.Cancel();
        _documentCancellation?.Dispose();
        _documentCancellation = null;
    }
}

internal readonly record struct DocumentLease(
    long Generation,
    string Token,
    CancellationToken CancellationToken);
