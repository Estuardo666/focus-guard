using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

/// <summary>
/// Records an active normal session as interrupted when the service stops (service restart,
/// update or Windows shutdown). Registered last so the host stops it first, while the history
/// store and the policy pipes are still available. A process crash skips StopAsync, so that
/// case still loses the session without a record.
/// </summary>
public sealed class NormalSessionShutdownRecorder(
    IFocusService focusService,
    TimeSpan? timeout = null) : IHostedService
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_timeout);
        try
        {
            await focusService.InterruptNormalSessionForShutdownAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (bounded.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ServiceFailureLog.WriteAsync(nameof(NormalSessionShutdownRecorder), exception).ConfigureAwait(false);
        }
    }
}
