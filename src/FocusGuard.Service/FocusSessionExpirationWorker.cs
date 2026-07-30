using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed class FocusSessionExpirationWorker(
    IFocusService focusService,
    TimeProvider? timeProvider = null,
    TimeSpan? interval = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await focusService.GetStatusAsync(stoppingToken);
                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ServiceFailureLog.WriteAsync(nameof(FocusSessionExpirationWorker), exception);
            throw;
        }
    }
}
