using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service;

public interface IFocusService
{
    ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopNormalSessionAsync(CancellationToken cancellationToken);

    ValueTask<ActivityResponse> GetActivityAsync(
        ActivityRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes down an urge raised on a block screen. Only meaningful while a session is
    /// running: outside one there is nothing to defer the thought until.
    /// </summary>
    ValueTask<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The end-of-session picture: what was achieved, and what was set aside. Returns the
    /// most recently finished session when no ID is given.
    /// </summary>
    ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        SessionSummaryRequest request,
        CancellationToken cancellationToken);

    ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
        ResolveCapturesRequest request,
        CancellationToken cancellationToken);
}

public sealed class InMemoryFocusService : IFocusService
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly IStrictSessionStore? _strictSessionStore;
    private readonly Func<bool> _extensionReadyProvider;
    private readonly Func<bool> _processReadyProvider;
    private readonly PolicyDistributionCoordinator _policyDistribution;
    private readonly IFocusAuditStore _auditStore;
    private readonly ICaptureStore? _captureStore;
    private readonly ISessionHistoryStore? _historyStore;
    private Session? _activeSession;
    private EnforcementPolicy? _activePolicy;
    private string _activeProfileName = string.Empty;

    // A session ends inside a lock but has to be recorded with async I/O, so the closing
    // entry is parked here and flushed by whichever call observed the transition.
    private SessionHistoryEntry? _pendingHistory;
    private Guid _recordedSessionId;

    public InMemoryFocusService(
        TimeProvider? timeProvider = null,
        IStrictSessionStore? strictSessionStore = null,
        Func<bool>? extensionReadyProvider = null,
        Func<bool>? processReadyProvider = null,
        PolicyDistributionCoordinator? policyDistribution = null,
        IFocusAuditStore? auditStore = null,
        ICaptureStore? captureStore = null,
        ISessionHistoryStore? historyStore = null)
    {
        _captureStore = captureStore;
        _historyStore = historyStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _strictSessionStore = strictSessionStore;
        _extensionReadyProvider = extensionReadyProvider ?? (() => true);
        _processReadyProvider = processReadyProvider ?? (() => true);
        _policyDistribution = policyDistribution ?? new PolicyDistributionCoordinator(
            new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver(timeProvider),
            timeProvider);
        _auditStore = auditStore ?? new EmptyFocusAuditStore();
    }

    public async ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Session session;
        lock (_gate)
        {
            var requestValidation = IpcProtocol.ValidateActivateSessionRequest(request);
            if (!requestValidation.IsValid)
            {
                return IpcCodecResult<ActivateSessionResponse>.Failure([.. requestValidation.Errors]);
            }

            RefreshNoLock();
            if (_activeSession?.State is SessionState.Activating or SessionState.Active)
            {
                return IpcCodecResult<ActivateSessionResponse>.Success(
                    new ActivateSessionResponse(
                        false,
                        "A focus session is already active.",
                        _activeSession.Id,
                        _activeSession.StartUtc,
                        _activeSession.EndUtc));
            }

            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var sessionResult = Session.Create(
                new ProfileId(request.ProfileId),
                nowUtc,
                TimeSpan.FromSeconds(request.DurationSeconds),
                new StrictModeOptions(request.StrictMode, TimeSpan.FromSeconds(request.RecoveryDelaySeconds)));

            if (!sessionResult.IsValid)
            {
                return IpcCodecResult<ActivateSessionResponse>.Failure(
                    sessionResult.Errors.Select(error => error.Message).ToArray());
            }

            _activeSession = sessionResult.Value!.TransitionTo(SessionState.Activating);
            session = _activeSession;
            _activePolicy = CreatePolicy(session, request);
            _activeProfileName = string.IsNullOrWhiteSpace(request.ProfileName)
                ? "Focus"
                : request.ProfileName.Trim();
        }

        if (_activePolicy!.EnforcementEnabled)
        {
            await _policyDistribution.DistributeAsync(_activePolicy, cancellationToken)
                .ConfigureAwait(false);
        }

        if (session.StrictMode.Enabled)
        {
            if (_strictSessionStore is null || request.RecoveryCredential is null)
            {
                lock (_gate)
                {
                    _activeSession = session with { State = SessionState.Failed };
                }

                return IpcCodecResult<ActivateSessionResponse>.Failure(
                    "Strict persistence is unavailable.");
            }

            try
            {
                await _strictSessionStore.ActivateAsync(
                    session,
                    _activePolicy!,
                    request.RecoveryCredential,
                    cancellationToken);
            }
            catch
            {
                lock (_gate)
                {
                    _activeSession = session with { State = SessionState.Failed };
                    _activePolicy = null;
                }

                throw;
            }
        }

        return IpcCodecResult<ActivateSessionResponse>.Success(
            new ActivateSessionResponse(
                true,
                request.EnforcementEnabled
                    ? "The service accepted a finite session and is awaiting each requested policy channel."
                    : "The service accepted a finite timer with enforcement explicitly disabled.",
                session.Id,
                session.StartUtc,
                session.EndUtc));
    }

    internal EnforcementPolicy? GetActivePolicySnapshot()
    {
        lock (_gate)
        {
            return _activePolicy;
        }
    }

    public async ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        EnforcementPolicy? recoveredPolicy = null;
        if (_strictSessionStore is not null)
        {
            var persisted = await _strictSessionStore.LoadAsync(
                _timeProvider.GetUtcNow().ToUniversalTime(),
                cancellationToken);
            lock (_gate)
            {
                if (persisted.Status == StrictPolicyLoadStatus.Active
                    && persisted.Snapshot is not null
                    && _activeSession is null)
                {
                    var policy = persisted.Snapshot.Policy;
                    _activeSession = new Session(
                        policy.SessionId,
                        policy.ProfileId,
                        policy.StartUtc,
                        policy.EndUtc,
                        SessionState.Activating,
                        new StrictModeOptions(true, persisted.Snapshot.RecoveryDelay));
                    _activePolicy = policy;
                    recoveredPolicy = policy;
                }
                else if (_activeSession?.StrictMode.Enabled == true
                    && _activeSession.State == SessionState.Active
                    && persisted.Status is StrictPolicyLoadStatus.Expired
                        or StrictPolicyLoadStatus.Recovered
                        or StrictPolicyLoadStatus.FailedOpen
                        or StrictPolicyLoadStatus.Absent)
                {
                    _activeSession = _activeSession with
                    {
                        State = persisted.Status == StrictPolicyLoadStatus.Expired
                            ? SessionState.Expired
                            : SessionState.Recovered
                    };
                }
            }
        }

        if (recoveredPolicy is not null)
        {
            await _policyDistribution.DistributeAsync(recoveredPolicy, cancellationToken)
                .ConfigureAwait(false);
        }

        var extensionReady = _extensionReadyProvider();
        var processReady = _processReadyProvider();
        _policyDistribution.ReconcileBrowserHealth(extensionReady);
        _policyDistribution.ReconcileProcessHealth(processReady);

        StatusResponse status;
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession?.State == SessionState.Activating
                && (_activePolicy?.EnforcementEnabled == false
                    || _policyDistribution.HasConnectedRequestedChannel))
            {
                _activeSession = _activeSession.TransitionTo(SessionState.Active);
            }

            if (_activeSession is null)
            {
                status = InactiveStatus();
            }
            else
            {
                var remainingSeconds = _activeSession.State is SessionState.Activating or SessionState.Active
                    ? Math.Max(0, (long)Math.Ceiling((_activeSession.EndUtc - _timeProvider.GetUtcNow().ToUniversalTime()).TotalSeconds))
                    : 0;

                status = new StatusResponse(
                    true,
                    extensionReady,
                    _activeSession.State.ToString(),
                    remainingSeconds,
                    _activeSession.Id,
                    _activeSession.ProfileId.Value,
                    _activeSession.StartUtc,
                    _activeSession.EndUtc,
                    _activeSession.StrictMode.Enabled,
                    EnforcementConnected: _activeSession.State == SessionState.Active
                        && _activePolicy?.EnforcementEnabled == true
                        && _policyDistribution.IsConnected);
            }
        }

        // The timer expiring is the most common way a session ends and nothing else
        // observes it, so the status poll is what persists that closure. Flushing after
        // the lock — but before returning — means a caller that sees "Expired" can ask
        // for the summary on its very next call and find it written.
        await FlushHistoryAsync(cancellationToken).ConfigureAwait(false);
        return status;
    }

    public ValueTask<StopSessionResponse> StopNormalSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession is null
                || _activeSession.State is not (SessionState.Activating or SessionState.Active)
                || _activeSession.StrictMode.Enabled)
            {
                return ValueTask.FromResult(new StopSessionResponse(
                    false,
                    "Only an active normal session can be stopped."));
            }

            _activeSession = _activeSession.TransitionTo(SessionState.Stopped);
            RecordClosureNoLock();
            _activePolicy = null;
        }

        return StopAndClearAsync(cancellationToken);
    }

    public async ValueTask<ActivityResponse> GetActivityAsync(
        ActivityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ActivityResponse(
            await _auditStore.ReadRecentAsync(request.MaximumCount, cancellationToken)
                .ConfigureAwait(false));
    }

    public async ValueTask<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = IpcProtocol.ValidateCaptureRequest(request);
        if (!validation.IsValid)
        {
            return new CaptureResponse(false, string.Join(" ", validation.Errors), null);
        }

        if (_captureStore is null)
        {
            return new CaptureResponse(false, "Capture storage is unavailable.", null);
        }

        Guid sessionId;
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession is null
                || _activeSession.State is not (SessionState.Activating or SessionState.Active))
            {
                return new CaptureResponse(
                    false,
                    "Captures belong to a running session.",
                    null);
            }

            sessionId = _activeSession.Id;
        }

        if (await _captureStore.CountBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            >= CaptureProtocol.MaximumCapturesPerSession)
        {
            return new CaptureResponse(false, "This session already holds the maximum captures.", null);
        }

        var item = CapturedItem.Create(
            sessionId,
            request.Text,
            request.SourceKind == CaptureProtocol.WebsiteKind
                ? CaptureSourceKind.Website
                : CaptureSourceKind.Application,
            request.SourceValue,
            _timeProvider.GetUtcNow().ToUniversalTime());
        if (!item.IsValid)
        {
            return new CaptureResponse(
                false,
                string.Join(" ", item.Errors.Select(error => error.Message)),
                null);
        }

        await _captureStore.AddAsync(item.Value!, cancellationToken).ConfigureAwait(false);
        return new CaptureResponse(true, "The capture was saved for the end of the session.", item.Value!.Id);
    }

    public async ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        SessionSummaryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A session that just expired may not be on disk yet when the shell asks.
        await FlushHistoryAsync(cancellationToken).ConfigureAwait(false);

        if (_historyStore is null)
        {
            return UnavailableSummary();
        }

        var entry = request.SessionId is { } sessionId && sessionId != Guid.Empty
            ? await _historyStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            : await _historyStore.GetMostRecentAsync(cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return UnavailableSummary();
        }

        var captures = _captureStore is null
            ? Array.Empty<CapturedItem>()
            : await _captureStore.ListBySessionAsync(entry.SessionId, cancellationToken)
                .ConfigureAwait(false);

        // A session ended early is not a win, so it carries no streak. Reporting one here
        // would be the app congratulating the user for something that did not happen.
        var localNow = _timeProvider.GetLocalNow();
        var streak = entry.Completed
            ? await _historyStore.GetCompletedStreakDaysAsync(
                DateOnly.FromDateTime(localNow.DateTime),
                localNow.Offset,
                cancellationToken).ConfigureAwait(false)
            : 0;

        return new SessionSummaryResponse(
            Available: true,
            SessionId: entry.SessionId,
            Outcome: entry.Completed ? CaptureProtocol.CompletedOutcome : CaptureProtocol.StoppedOutcome,
            ProfileName: entry.ProfileName,
            PlannedMinutes: entry.PlannedMinutes,
            FocusedMinutes: entry.FocusedMinutes,
            BlockedAttempts: entry.BlockedAttempts,
            StreakDays: streak,
            EndedAtUtc: entry.EndedAtUtc,
            Captures: captures
                .Where(capture => capture.Resolution == CaptureResolution.Pending)
                .Select(capture => new CapturedItemView(
                    capture.Id,
                    capture.Text,
                    capture.SourceKind.ToString(),
                    capture.SourceValue,
                    capture.SourceLabel,
                    capture.CapturedAtUtc,
                    capture.Resolution.ToString()))
                .ToArray());
    }

    public async ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
        ResolveCapturesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_captureStore is null)
        {
            return new ResolveCapturesResponse(false, "Capture storage is unavailable.", 0);
        }

        var resolved = await _captureStore.ResolveAsync(
            request.CaptureIds,
            request.Resolution == CaptureProtocol.OpenedResolution
                ? CaptureResolution.Opened
                : CaptureResolution.Dismissed,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            cancellationToken).ConfigureAwait(false);
        return new ResolveCapturesResponse(true, "The captures were resolved.", resolved);
    }

    private static SessionSummaryResponse UnavailableSummary() =>
        new(
            Available: false,
            SessionId: null,
            Outcome: CaptureProtocol.StoppedOutcome,
            ProfileName: string.Empty,
            PlannedMinutes: 0,
            FocusedMinutes: 0,
            BlockedAttempts: 0,
            StreakDays: 0,
            EndedAtUtc: null,
            Captures: Array.Empty<CapturedItemView>());

    private StatusResponse InactiveStatus() =>
        new(
            true,
            _extensionReadyProvider(),
            "Inactive",
            0,
            null,
            null,
            null,
            null,
            StrictMode: false,
            EnforcementConnected: false);

    private void RefreshNoLock()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession = _activeSession.ExpireIfDue(_timeProvider.GetUtcNow().ToUniversalTime());
        if (_activeSession.State is SessionState.Expired
            or SessionState.Stopped
            or SessionState.Recovered
            or SessionState.Failed)
        {
            RecordClosureNoLock();
            _activePolicy = null;
        }
    }

    /// <summary>
    /// Parks the closing entry for the session that just finished, exactly once. The
    /// blocked-attempt count is filled in at flush time, where audit I/O is allowed.
    /// </summary>
    private void RecordClosureNoLock()
    {
        if (_activeSession is null || _recordedSessionId == _activeSession.Id)
        {
            return;
        }

        _recordedSessionId = _activeSession.Id;
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var planned = _activeSession.EndUtc - _activeSession.StartUtc;
        var focused = (nowUtc < _activeSession.EndUtc ? nowUtc : _activeSession.EndUtc)
            - _activeSession.StartUtc;
        _pendingHistory = new SessionHistoryEntry(
            _activeSession.Id,
            _activeSession.ProfileId,
            string.IsNullOrWhiteSpace(_activeProfileName) ? "Focus" : _activeProfileName,
            _activeSession.StartUtc,
            nowUtc,
            Completed: _activeSession.State == SessionState.Expired,
            PlannedMinutes: (int)Math.Round(planned.TotalMinutes, MidpointRounding.AwayFromZero),
            FocusedMinutes: (int)Math.Max(0, Math.Floor(focused.TotalMinutes)),
            BlockedAttempts: 0);
    }

    private async ValueTask FlushHistoryAsync(CancellationToken cancellationToken)
    {
        SessionHistoryEntry? pending;
        lock (_gate)
        {
            pending = _pendingHistory;
            _pendingHistory = null;
        }

        if (pending is null || _historyStore is null)
        {
            return;
        }

        try
        {
            var blocked = await CountBlockedAttemptsAsync(pending.SessionId, cancellationToken)
                .ConfigureAwait(false);
            await _historyStore.RecordAsync(
                pending with { BlockedAttempts = blocked },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            // History is a reporting nicety. Losing one entry must never take down the
            // pipe that the shell relies on to learn a session ended.
        }
    }

    private async ValueTask<int> CountBlockedAttemptsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var events = await _auditStore.ReadRecentAsync(1000, cancellationToken).ConfigureAwait(false);
        return events.Count(item => item.SessionId == sessionId
            && item.Kind is FocusAuditEventKind.WebsiteBlocked
                or FocusAuditEventKind.ApplicationBlocked);
    }

    private async ValueTask<StopSessionResponse> StopAndClearAsync(CancellationToken cancellationToken)
    {
        await _policyDistribution.ClearAsync(cancellationToken).ConfigureAwait(false);
        await FlushHistoryAsync(cancellationToken).ConfigureAwait(false);
        return new StopSessionResponse(true, "The normal session was stopped.");
    }

    private static EnforcementPolicy CreatePolicy(Session session, ActivateSessionRequest request)
    {
        var applicationRules = (request.ApplicationRules ?? [])
            .Select(rule => new ApplicationRule(
                rule.Id,
                Enum.Parse<RuleAction>(rule.Action, ignoreCase: false),
                Enum.Parse<ApplicationMatchKind>(rule.MatchKind, ignoreCase: false),
                rule.Value.Trim()))
            .ToArray();
        var websiteRules = (request.WebsiteRules ?? [])
            .Select(rule => new WebsiteRule(
                rule.Id,
                Enum.Parse<RuleAction>(rule.Action, ignoreCase: false),
                rule.Pattern.Trim().ToLowerInvariant()))
            .ToArray();
        return EnforcementPolicy.Create(
            session,
            applicationRules,
            websiteRules,
            request.EnforcementEnabled);
    }
}

internal sealed class EmptyFocusAuditStore : IFocusAuditStore
{
    public Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FocusAuditEvent>>(Array.Empty<FocusAuditEvent>());
}
