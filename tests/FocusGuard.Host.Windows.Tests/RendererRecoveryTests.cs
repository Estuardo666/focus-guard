using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class RendererRecoveryTests
{
    [Fact]
    public void FirstRendererFailureInvalidatesCurrentDocumentAndRequestsOneAutomaticRecreation()
    {
        using var lifetime = new DocumentLifetime();
        var controller = new RendererRecoveryController();
        var lease = lifetime.BeginDocument();

        var action = controller.RecordFailure(lifetime);

        Assert.Equal(RendererFailureAction.RecreateWebView, action);
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(lifetime.IsCurrent(lease));
    }

    [Fact]
    public void RepeatedRendererFailureShowsManualRetryAndManualRetryResetsRecoveryBudget()
    {
        using var lifetime = new DocumentLifetime();
        var controller = new RendererRecoveryController();
        var firstDocument = lifetime.BeginDocument();
        Assert.Equal(RendererFailureAction.RecreateWebView, controller.RecordFailure(lifetime));
        Assert.True(firstDocument.CancellationToken.IsCancellationRequested);

        var recreatedDocument = lifetime.BeginDocument();
        Assert.Equal(RendererFailureAction.ShowManualRetry, controller.RecordFailure(lifetime));
        Assert.True(recreatedDocument.CancellationToken.IsCancellationRequested);
        Assert.False(lifetime.IsCurrent(recreatedDocument));

        controller.BeginManualRetry();
        var manualRetryDocument = lifetime.BeginDocument();
        Assert.Equal(RendererFailureAction.RecreateWebView, controller.RecordFailure(lifetime));
        Assert.True(manualRetryDocument.CancellationToken.IsCancellationRequested);
    }
}
