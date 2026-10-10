using FocusGuard.Platform.Windows;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>
/// Keeps the per-user enforcement agent available while this UI host is running.
/// Disposing the supervisor stops retries; it deliberately does not terminate the
/// independent agent process or interrupt an active session.
/// </summary>
internal sealed class ProcessAgentSupervisorLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _supervision;
    private int _disposed;

    public ProcessAgentSupervisorLifetime(ProcessAgentSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervision = supervisor.RunAsync(_cancellation.Token);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        try
        {
            _supervision.GetAwaiter().GetResult();
        }
        finally
        {
            _cancellation.Dispose();
        }
    }
}
