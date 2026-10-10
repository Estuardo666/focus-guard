namespace FocusGuard.Host.Windows.Hosting;

internal enum RendererFailureAction
{
    RecreateWebView,
    ShowManualRetry
}

internal sealed class RendererRecoveryController
{
    private bool _automaticRecoveryUsed;

    public RendererFailureAction RecordFailure(DocumentLifetime documentLifetime)
    {
        documentLifetime.Invalidate();
        if (_automaticRecoveryUsed)
        {
            return RendererFailureAction.ShowManualRetry;
        }

        _automaticRecoveryUsed = true;
        return RendererFailureAction.RecreateWebView;
    }

    public void BeginManualRetry() => _automaticRecoveryUsed = false;
}
