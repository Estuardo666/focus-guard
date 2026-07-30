using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public static class DevelopmentServiceConstants
{
#if FOCUSGUARD_PRODUCTION
    public const string ServiceName = "FocusGuard";
    public const string DisplayName = "FocusGuard Service";
#else
    public const string ServiceName = "FocusGuard.Development";
    public const string DisplayName = "FocusGuard Development Service";
#endif
}

public sealed class DevelopmentServiceState
{
    private long _heartbeatUtcTicks;
    private long _startedUtcTicks;
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public DateTimeOffset? StartedUtc => ReadUtc(ref _startedUtcTicks);

    public DateTimeOffset? LastHeartbeatUtc => ReadUtc(ref _heartbeatUtcTicks);

    internal void MarkStarted(DateTimeOffset utcNow)
    {
        var ticks = utcNow.UtcTicks;
        Interlocked.Exchange(ref _startedUtcTicks, ticks);
        Interlocked.Exchange(ref _heartbeatUtcTicks, ticks);
        Volatile.Write(ref _isRunning, 1);
    }

    internal void MarkHeartbeat(DateTimeOffset utcNow) =>
        Interlocked.Exchange(ref _heartbeatUtcTicks, utcNow.UtcTicks);

    internal void MarkStopped() => Volatile.Write(ref _isRunning, 0);

    private static DateTimeOffset? ReadUtc(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}

public sealed class DevelopmentServiceWorker : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly DevelopmentServiceState _state;
    private readonly TimeProvider _timeProvider;

    public DevelopmentServiceWorker(
        DevelopmentServiceState state,
        TimeProvider? timeProvider = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.MarkStarted(_timeProvider.GetUtcNow().ToUniversalTime());
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, _timeProvider, stoppingToken);
                _state.MarkHeartbeat(_timeProvider.GetUtcNow().ToUniversalTime());
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _state.MarkStopped();
        }
    }
}
