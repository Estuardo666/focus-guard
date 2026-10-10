using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

/// <summary>
/// Keeps <see cref="NormalSessionJournal"/> in step with the running normal session and, on
/// start, records a session that a crashed or killed service process never closed. The end
/// time of such a session is its last heartbeat, so it is accurate to the heartbeat interval.
/// </summary>
public sealed class NormalSessionJournalWorker(
    IFocusService focusService,
    NormalSessionJournal journal,
    TimeProvider? timeProvider = null,
    TimeSpan? pollInterval = null,
    TimeSpan? heartbeat = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _heartbeat = heartbeat ?? TimeSpan.FromSeconds(10);
    private Guid _journaledSessionId;
    private DateTimeOffset _journaledEndUtc;
    private long _lastWriteTimestamp;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReconcileAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        // The shutdown recorder stops first and has already closed a normal session, so a
        // journal left now would only make the next start record it twice.
        if (focusService.CreateInterruptionJournalEntry() is null)
        {
            await GuardAsync(() =>
            {
                journal.Delete();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>Records and removes a journal left behind by a previous service process.</summary>
    public Task ReconcileAsync(CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            var orphan = await journal.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (orphan is not null)
            {
                await focusService.RecordOrphanedInterruptionAsync(orphan, cancellationToken).ConfigureAwait(false);
                journal.Delete();
            }
        });

    /// <summary>One poll: write, refresh or remove the journal.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var entry = focusService.CreateInterruptionJournalEntry();
        if (entry is null)
        {
            if (_journaledSessionId != Guid.Empty || journal.Exists)
            {
                journal.Delete();
                _journaledSessionId = Guid.Empty;
            }

            return;
        }

        var now = _timeProvider.GetTimestamp();
        var newSession = entry.SessionId != _journaledSessionId;
        // The heartbeat interval is monotonic; a wall clock moved backwards never rewinds the
        // recorded end, so focus time already journaled is kept.
        if (newSession
            || _timeProvider.GetElapsedTime(_lastWriteTimestamp, now) >= _heartbeat
                && entry.EndedAtUtc > _journaledEndUtc)
        {
            await journal.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            _journaledSessionId = entry.SessionId;
            _journaledEndUtc = entry.EndedAtUtc;
            _lastWriteTimestamp = now;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await GuardAsync(() => TickAsync(stoppingToken)).ConfigureAwait(false);
                await Task.Delay(_pollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    // The journal only improves history; its I/O failures are logged and never stop the service.
    private static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            await ServiceFailureLog.WriteAsync(nameof(NormalSessionJournalWorker), exception).ConfigureAwait(false);
        }
    }
}
