using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Host.Windows.Hosting;

internal sealed record CyclePhaseTransition(
    SessionPhaseKind? EndedKind,
    int? EndedFocusCycleNumber,
    SessionPhaseKind? StartedKind,
    int? StartedFocusCycleNumber,
    DateTimeOffset? StartedEndUtc);

/// <summary>
/// Deduplicates status polls into phase transitions without becoming a phase clock: every transition reported here
/// has already been authorized by the service. Same rules as the Avalonia shell it replaces.
/// </summary>
internal sealed class CyclePhaseTransitionTracker
{
    private Guid? _sessionId;
    private PhaseStatus? _phase;

    internal CyclePhaseTransition? Observe(StatusResponse status)
    {
        var isRunningCycle = status.SessionState is "Activating" or "Active"
            && status.Mode == SessionMode.FocusCycle
            && status.SessionId is not null;
        if (!isRunningCycle)
        {
            if (_sessionId is null) return null;
            var ended = Relevant(_phase?.Kind);
            var transition = ended is null ? null : new CyclePhaseTransition(ended, _phase?.FocusCycleNumber, null, null, null);
            _sessionId = null;
            _phase = null;
            return transition;
        }

        var isNewSession = status.SessionId != _sessionId;
        var previous = isNewSession ? null : _phase;
        _sessionId = status.SessionId;
        _phase = status.CurrentPhase;
        if (_phase is null) return null;
        if (!isNewSession && previous is not null && previous.Index == _phase.Index && previous.Kind == _phase.Kind) return null;

        var endedKind = Relevant(previous?.Kind);
        var startedKind = Relevant(_phase.Kind);
        return endedKind is null && startedKind is null
            ? null
            : new CyclePhaseTransition(endedKind, previous?.FocusCycleNumber, startedKind,
                startedKind is null ? null : _phase.FocusCycleNumber, startedKind is null ? null : _phase.EndUtc);
    }

    private static SessionPhaseKind? Relevant(SessionPhaseKind? kind) =>
        kind is SessionPhaseKind.Focus or SessionPhaseKind.ShortBreak ? kind : null;
}

/// <summary>A session is "closed" on the first poll after it stops being Activating or Active; tracking the ID keeps a reconnect from replaying it.</summary>
internal sealed class SessionClosureTracker
{
    private Guid? _running;

    internal Guid? Observe(StatusResponse status)
    {
        if (status.SessionState is "Activating" or "Active")
        {
            if (status.SessionId is { } id) _running = id;
            return null;
        }

        var finished = _running;
        _running = null;
        return finished;
    }
}

/// <summary>
/// Only application blocks raise the desktop block screen (a blocked site already gets the extension's page). The first
/// read of a session sees history, not a fresh block, so it is never announced.
/// </summary>
internal sealed class ApplicationBlockTracker
{
    private Guid? _sessionId;
    private Guid _lastBlockId = Guid.Empty;
    private bool _primed;

    internal string? Observe(Guid sessionId, DateTimeOffset sessionStart, IReadOnlyList<FocusAuditEvent> events)
    {
        if (_sessionId != sessionId)
        {
            _sessionId = sessionId;
            _lastBlockId = Guid.Empty;
            _primed = false;
        }

        var latest = events
            .Where(item => item.Kind == FocusAuditEventKind.ApplicationBlocked && item.TimestampUtc >= sessionStart)
            .OrderByDescending(item => item.TimestampUtc)
            .FirstOrDefault();
        var wasPrimed = _primed;
        _primed = true;
        if (latest is null || latest.Id == _lastBlockId) return null;
        _lastBlockId = latest.Id;
        return wasPrimed ? latest.Target : null;
    }
}

internal sealed record SessionEndedEvent(Guid SessionId);
internal sealed record ApplicationBlockedEvent(string Target, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool Strict);

/// <summary>
/// Watches the service from the host process itself, so session-end summaries, phase notices and the block screen keep
/// working while the window is hidden in the notification area. It only reads; it never changes a session.
/// </summary>
internal sealed class SessionEventMonitor(IFocusControlClient client, TimeProvider? timeProvider = null) : IDisposable
{
    internal static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan ActivityInterval = TimeSpan.FromSeconds(3);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly CyclePhaseTransitionTracker _phases = new();
    private readonly SessionClosureTracker _closures = new();
    private readonly ApplicationBlockTracker _blocks = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _lastActivityTicks;

    public event Action<SessionEndedEvent>? SessionEnded;
    public event Action<CyclePhaseTransition>? PhaseChanged;
    public event Action<ApplicationBlockedEvent>? ApplicationBlocked;
    public event Action<StatusResponse?>? StatusChanged;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Feeds one status reading through the trackers. Public for tests; the loop calls it on every poll.</summary>
    internal async Task ObserveAsync(StatusResponse status, CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke(status);
        if (_phases.Observe(status) is { } transition) PhaseChanged?.Invoke(transition);
        if (_closures.Observe(status) is { } ended) SessionEnded?.Invoke(new SessionEndedEvent(ended));

        var running = status.SessionState is "Active" && status.SessionId is { } && status.StartUtc is { } && status.EndUtc is { };
        var now = _time.GetTimestamp();
        if (!running || !status.EnforcementConnected
            || _lastActivityTicks != 0 && _time.GetElapsedTime(_lastActivityTicks, now) < ActivityInterval) return;
        _lastActivityTicks = now;
        var activity = await client.GetActivityAsync(200, cancellationToken).ConfigureAwait(false);
        if (_blocks.Observe(status.SessionId!.Value, status.StartUtc!.Value, activity.Events) is { } target)
            ApplicationBlocked?.Invoke(new ApplicationBlockedEvent(target, status.StartUtc.Value, status.EndUtc!.Value, status.StrictMode));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                await ObserveAsync(status, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidDataException
                or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
            {
                // A lost connection is not a session end: report "unknown" and keep the last trackers untouched.
                StatusChanged?.Invoke(null);
            }

            try { await Task.Delay(StatusInterval, _time, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
