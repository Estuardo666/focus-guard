using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service;

public interface IFocusService
{
    ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateScheduledAsync(ActivateSessionRequest request, DateTimeOffset deadlineUtc, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IpcCodecResult<ActivateSessionResponse>.Failure("Scheduled activation unavailable."));
    ValueTask<IpcCodecResult<PrepareSessionResponse>> PrepareSessionAsync(
        PrepareSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<IpcCodecResult<ActivatePreparedSessionResponse>> ActivatePreparedSessionAsync(
        ActivatePreparedSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<IpcCodecResult<SkipBreakResponse>> SkipBreakAsync(
        SkipBreakRequest request,
        CancellationToken cancellationToken);

    ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopNormalSessionAsync(CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopReviewedAsync(ReviewedStopSessionRequest request, CancellationToken token) =>
        ValueTask.FromResult(new StopSessionResponse(false, "Reviewed control is unavailable."));

    ValueTask<IpcCodecResult<SkipBreakResponse>> SkipReviewedAsync(ReviewedSkipBreakRequest request, CancellationToken token) =>
        ValueTask.FromResult(IpcCodecResult<SkipBreakResponse>.Success(new(false, "Reviewed control is unavailable.", request.PhaseIndex, null, null)));

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

    /// <summary>
    /// Called while the service host is stopping. A normal session is not restored after a
    /// restart, so it is closed here as interrupted and recorded instead of vanishing.
    /// Strict sessions are left to their persisted snapshot.
    /// </summary>
    ValueTask InterruptNormalSessionForShutdownAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// The history entry the running normal session would get if it were interrupted now, or
    /// null when no normal session is running. Persisted periodically so that a crash or a
    /// killed process can still be recorded on the next start.
    /// </summary>
    SessionHistoryEntry? CreateInterruptionJournalEntry() => null;

    /// <summary>
    /// Records a journaled session that a previous service process never closed. Returns false
    /// when history already holds that session, so replaying a journal is harmless.
    /// </summary>
    ValueTask<bool> RecordOrphanedInterruptionAsync(SessionHistoryEntry entry, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
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
    private readonly SemaphoreSlim _strictRecoveryGate = new(1, 1);
    private int _strictRecoveryCompleted;
    private long _nextStrictReconciliationUtcTicks;
    private Session? _activeSession;
    private EnforcementPolicy? _activePolicy;
    private string _activeProfileName = string.Empty;
    private ScheduledSessionIntent? _activeScheduledIntent;
    private PreparedSessionData? _preparedSession;
    private SkipBreakResponse? _lastSkipResponse;

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

    public ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken) => ActivateCoreAsync(request, null, cancellationToken);

    public ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateScheduledAsync(
        ActivateSessionRequest request, DateTimeOffset deadlineUtc, CancellationToken cancellationToken) =>
        request.StrictMode || deadlineUtc.Offset != TimeSpan.Zero
            ? ValueTask.FromResult(IpcCodecResult<ActivateSessionResponse>.Failure("Schedules must be normal and finite."))
            : ActivateCoreAsync(request, deadlineUtc, cancellationToken);

    private async ValueTask<IpcCodecResult<ActivateSessionResponse>> ActivateCoreAsync(
        ActivateSessionRequest request, DateTimeOffset? deadlineUtc, CancellationToken cancellationToken)
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
            var duration = deadlineUtc is { } deadline
                ? TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(request.DurationSeconds).Ticks, (deadline - nowUtc).Ticks))
                : TimeSpan.FromSeconds(request.DurationSeconds);
            var strictMode = new StrictModeOptions(request.StrictMode, TimeSpan.FromSeconds(request.RecoveryDelaySeconds));
            var sessionResult = deadlineUtc is null
                ? Session.Create(new ProfileId(request.ProfileId), nowUtc, duration, strictMode)
                : Session.CreateScheduled(new ProfileId(request.ProfileId), nowUtc, duration, strictMode);

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
            _activeScheduledIntent = deadlineUtc is null ? null : new ScheduledSessionIntent(
                _activeProfileName, Math.Clamp((int)Math.Ceiling(request.DurationSeconds / 60d), 1, 720), request.EnforcementEnabled);
            _preparedSession = null;
            _lastSkipResponse = null;
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

    public ValueTask<IpcCodecResult<PrepareSessionResponse>> PrepareSessionAsync(
        PrepareSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = CycleIpcProtocol.ValidatePrepareRequest(request);
        if (!validation.IsValid)
        {
            return ValueTask.FromResult(
                IpcCodecResult<PrepareSessionResponse>.Failure([.. validation.Errors]));
        }

        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession?.State is SessionState.Activating or SessionState.Active)
            {
                return ValueTask.FromResult(IpcCodecResult<PrepareSessionResponse>.Success(
                    PreparationRejected("A focus session is already active.", request.Mode)));
            }

            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            FocusCyclePlan? cyclePlan = null;
            DateTimeOffset endUtc;
            if (request.Mode == SessionMode.FocusCycle)
            {
                CycleIpcProtocol.TryBuildConfiguration(
                    request.CycleConfiguration!,
                    out var configuration,
                    out _);
                var planResult = FocusCyclePlanBuilder.Build(configuration!, nowUtc);
                if (!planResult.IsValid)
                {
                    return ValueTask.FromResult(
                        IpcCodecResult<PrepareSessionResponse>.Failure(
                            planResult.Errors.Select(error => error.Message).ToArray()));
                }

                cyclePlan = planResult.Value!;
                endUtc = cyclePlan.EndUtc;
            }
            else
            {
                var durationResult = Session.Create(
                    new ProfileId(request.ProfileId),
                    nowUtc,
                    TimeSpan.FromSeconds(request.DurationSeconds),
                    new StrictModeOptions(request.StrictMode, TimeSpan.FromSeconds(request.RecoveryDelaySeconds)));
                if (!durationResult.IsValid)
                {
                    return ValueTask.FromResult(
                        IpcCodecResult<PrepareSessionResponse>.Failure(
                            durationResult.Errors.Select(error => error.Message).ToArray()));
                }

                endUtc = durationResult.Value!.EndUtc;
            }

            var prepared = new PreparedSessionData(
                Guid.NewGuid(),
                request with
                {
                    ApplicationRules = Array.AsReadOnly((request.ApplicationRules ?? []).ToArray()),
                    WebsiteRules = Array.AsReadOnly((request.WebsiteRules ?? []).ToArray())
                },
                nowUtc,
                endUtc,
                nowUtc.AddMinutes(2),
                cyclePlan);
            _preparedSession = prepared;
            return ValueTask.FromResult(IpcCodecResult<PrepareSessionResponse>.Success(
                new PrepareSessionResponse(
                    true,
                    "The session plan is prepared and awaits activation.",
                    prepared.Id,
                    prepared.StartUtc,
                    prepared.EndUtc,
                    prepared.ExpiresUtc,
                    request.Mode,
                    prepared.CyclePlan?.Phases.Select(ToPreparedPhase).ToArray() ?? [])));
        }
    }

    public async ValueTask<IpcCodecResult<ActivatePreparedSessionResponse>> ActivatePreparedSessionAsync(
        ActivatePreparedSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PreparedSessionData prepared;
        Session session;
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession?.State is SessionState.Activating or SessionState.Active)
            {
                return IpcCodecResult<ActivatePreparedSessionResponse>.Success(
                    ActivationPreparedRejected("A focus session is already active."));
            }

            if (_preparedSession is null || _preparedSession.Id != request.PreparationId)
            {
                return IpcCodecResult<ActivatePreparedSessionResponse>.Success(
                    ActivationPreparedRejected("The prepared session is missing or has been cancelled."));
            }

            prepared = _preparedSession;
            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            if (nowUtc >= prepared.ExpiresUtc || nowUtc >= prepared.EndUtc)
            {
                _preparedSession = null;
                return IpcCodecResult<ActivatePreparedSessionResponse>.Success(
                    ActivationPreparedRejected("The prepared session has expired."));
            }

            var sessionResult = Session.Create(
                new ProfileId(prepared.Request.ProfileId),
                prepared.StartUtc,
                prepared.EndUtc - prepared.StartUtc,
                new StrictModeOptions(
                    prepared.Request.StrictMode,
                    TimeSpan.FromSeconds(prepared.Request.RecoveryDelaySeconds)));
            if (!sessionResult.IsValid)
            {
                _preparedSession = null;
                return IpcCodecResult<ActivatePreparedSessionResponse>.Failure(
                    sessionResult.Errors.Select(error => error.Message).ToArray());
            }

            session = sessionResult.Value!.TransitionTo(SessionState.Activating);
            if (prepared.CyclePlan is not null)
            {
                session = session with { CyclePlan = prepared.CyclePlan };
            }

            _activeSession = session;
            _activePolicy = CreatePolicy(session, prepared.Request);
            _activeProfileName = string.IsNullOrWhiteSpace(prepared.Request.ProfileName)
                ? "Focus"
                : prepared.Request.ProfileName.Trim();
            _activeScheduledIntent = null;
            AuthorizeCurrentPhaseNoLock(prepared.StartUtc);
            _preparedSession = null;
            _lastSkipResponse = null;
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
                    _activePolicy = null;
                }

                return IpcCodecResult<ActivatePreparedSessionResponse>.Failure(
                    "Strict persistence is unavailable.");
            }

            try
            {
                await _strictSessionStore.ActivateAsync(
                    session,
                    _activePolicy!,
                    request.RecoveryCredential,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The response only says "Strict persistence failed"; record the exception type (never its message) for diagnosis.
                await ServiceFailureLog.WriteAsync(nameof(ActivatePreparedSessionAsync), exception).ConfigureAwait(false);
                lock (_gate)
                {
                    _activeSession = session with { State = SessionState.Failed };
                    _activePolicy = null;
                }

                await _policyDistribution.ClearAsync(cancellationToken).ConfigureAwait(false);
                return IpcCodecResult<ActivatePreparedSessionResponse>.Failure(
                    "Strict persistence failed.");
            }
        }

        lock (_gate)
        {
            if (_activeSession?.State == SessionState.Activating
                && (!_activePolicy!.EnforcementEnabled
                    || _policyDistribution.HasConnectedRequestedChannel))
            {
                _activeSession = _activeSession.TransitionTo(SessionState.Active);
            }

            return IpcCodecResult<ActivatePreparedSessionResponse>.Success(
                new ActivatePreparedSessionResponse(
                    true,
                    "The prepared session was activated.",
                    session.Id,
                    session.StartUtc,
                    session.EndUtc,
                    session.Mode));
        }
    }

    public ValueTask<IpcCodecResult<SkipBreakResponse>> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) =>
        SkipBreakCoreAsync(request, null, token);

    public ValueTask<IpcCodecResult<SkipBreakResponse>> SkipReviewedAsync(ReviewedSkipBreakRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SkipBreakCoreAsync(new(request.PhaseIndex), request.SessionId, token);
    }

    private async ValueTask<IpcCodecResult<SkipBreakResponse>> SkipBreakCoreAsync(
        SkipBreakRequest request,
        Guid? expectedSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnforcementPolicy? updatedPolicy = null;
        SkipBreakResponse response;
        lock (_gate)
        {
            RefreshNoLock();
            // Identity must precede the phase cache: a stale window cannot act on a replacement session.
            if (expectedSessionId is { } expected && (expected == Guid.Empty || _activeSession?.Id != expected))
                return IpcCodecResult<SkipBreakResponse>.Success(new(false, "SessionChanged", request.PhaseIndex, null, null));
            if (_lastSkipResponse is not null && _lastSkipResponse.PhaseIndex == request.PhaseIndex)
            {
                return IpcCodecResult<SkipBreakResponse>.Success(_lastSkipResponse);
            }

            if (_activeSession?.State is not (SessionState.Activating or SessionState.Active)
                || _activeSession.CyclePlan is null)
            {
                return IpcCodecResult<SkipBreakResponse>.Success(
                    new SkipBreakResponse(false, "No focus-cycle session is active.", request.PhaseIndex, null, null));
            }

            if (_activeSession.StrictMode.Enabled)
            {
                return IpcCodecResult<SkipBreakResponse>.Success(
                    new SkipBreakResponse(false, "Strict sessions cannot skip phases.", request.PhaseIndex, null, _activeSession.EndUtc));
            }

            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var current = FindCurrentPhase(_activeSession.CyclePlan, nowUtc);
            if (current is null || current.Index != request.PhaseIndex)
            {
                return IpcCodecResult<SkipBreakResponse>.Success(
                    new SkipBreakResponse(false, "Only the current break can be skipped.", request.PhaseIndex, ToPhaseStatus(current, nowUtc), _activeSession.EndUtc));
            }

            if (current.Kind is not (SessionPhaseKind.ShortBreak or SessionPhaseKind.LongBreak))
            {
                return IpcCodecResult<SkipBreakResponse>.Success(
                    new SkipBreakResponse(false, "The current phase is not a break.", request.PhaseIndex, ToPhaseStatus(current, nowUtc), _activeSession.EndUtc));
            }

            var skippedDuration = current.EndUtc - nowUtc;
            var shiftedPhases = _activeSession.CyclePlan.Phases
                .Where(phase => phase.Index != current.Index || nowUtc > current.StartUtc)
                .Select(phase => phase.Index == current.Index ? phase with { EndUtc = nowUtc } : phase.Index > current.Index
                    ? phase with
                    {
                        StartUtc = phase.StartUtc - skippedDuration,
                        EndUtc = phase.EndUtc - skippedDuration
                    }
                    : phase)
                .ToArray();
            var shiftedPlan = _activeSession.CyclePlan with
            {
                Phases = Array.AsReadOnly(shiftedPhases),
                EndUtc = _activeSession.CyclePlan.EndUtc - skippedDuration
            };
            _activeSession = _activeSession with
            {
                CyclePlan = shiftedPlan,
                EndUtc = shiftedPlan.EndUtc
            };
            if (_activePolicy is not null)
            {
                _activePolicy = _activePolicy with { EndUtc = shiftedPlan.EndUtc };
                updatedPolicy = _activePolicy;
            }

            var authorizedPolicy = AuthorizeCurrentPhaseNoLock(nowUtc);
            if (authorizedPolicy is not null)
            {
                updatedPolicy = authorizedPolicy;
            }

            response = new SkipBreakResponse(
                true,
                "The break was skipped and the next focus phase starts now.",
                request.PhaseIndex,
                ToPhaseStatus(FindCurrentPhase(shiftedPlan, nowUtc), nowUtc),
                shiftedPlan.EndUtc);
            _lastSkipResponse = response;
        }

        if (updatedPolicy is not null && updatedPolicy.EnforcementEnabled)
        {
            await _policyDistribution.DistributeAsync(updatedPolicy, cancellationToken)
                .ConfigureAwait(false);
        }

        return IpcCodecResult<SkipBreakResponse>.Success(response);
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
        await RecoverPersistedStrictSessionOnceAsync(cancellationToken).ConfigureAwait(false);

        var extensionReady = _extensionReadyProvider();
        var processReady = _processReadyProvider();
        _policyDistribution.ReconcileBrowserHealth(extensionReady);
        _policyDistribution.ReconcileProcessHealth(processReady);

        StatusResponse status;
        EnforcementPolicy? phasePolicy = null;
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession?.State == SessionState.Activating
                && (_activePolicy?.EnforcementEnabled == false
                    || _policyDistribution.HasConnectedRequestedChannel))
            {
                _activeSession = _activeSession.TransitionTo(SessionState.Active);
            }

            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            phasePolicy = AuthorizeCurrentPhaseNoLock(nowUtc);

            if (_activeSession is null)
            {
                status = InactiveStatus();
            }
            else
            {
                var remainingSeconds = _activeSession.State is SessionState.Activating or SessionState.Active
                    ? Math.Max(0, (long)Math.Ceiling((_activeSession.EndUtc - nowUtc).TotalSeconds))
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
                    && _policyDistribution.IsConnected,
                    Mode: _activeSession.Mode,
                    CurrentPhase: ToPhaseStatus(FindCurrentPhase(_activeSession.CyclePlan, nowUtc), nowUtc),
                    NextPhase: ToPhaseStatus(FindNextPhase(_activeSession.CyclePlan, nowUtc), nowUtc),
                    FinalEndUtc: _activeSession.CyclePlan?.EndUtc,
                    ScheduledIntent: _activeScheduledIntent);
            }
        }

        if (phasePolicy is { EnforcementEnabled: true })
        {
            await _policyDistribution.DistributeAsync(phasePolicy, cancellationToken)
                .ConfigureAwait(false);
        }

        // The timer expiring is the most common way a session ends and nothing else
        // observes it, so the status poll is what persists that closure. Flushing after
        // the lock — but before returning — means a caller that sees "Expired" can ask
        // for the summary on its very next call and find it written.
        await FlushHistoryAsync(cancellationToken).ConfigureAwait(false);
        return status;
    }

    private async ValueTask RecoverPersistedStrictSessionOnceAsync(
        CancellationToken cancellationToken)
    {
        if (_strictSessionStore is null || !NeedsStrictPersistenceCheck())
        {
            return;
        }

        await _strictRecoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!NeedsStrictPersistenceCheck())
            {
                return;
            }

            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var persisted = await _strictSessionStore.LoadAsync(
                nowUtc,
                cancellationToken).ConfigureAwait(false);
            EnforcementPolicy? recoveredPolicy = null;
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
                        new StrictModeOptions(true, persisted.Snapshot.RecoveryDelay))
                    {
                        CyclePlan = policy.CyclePlan
                    };
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

            if (recoveredPolicy is not null)
            {
                await _policyDistribution.DistributeAsync(recoveredPolicy, cancellationToken)
                    .ConfigureAwait(false);
            }

            Volatile.Write(ref _strictRecoveryCompleted, 1);
            Volatile.Write(
                ref _nextStrictReconciliationUtcTicks,
                nowUtc.AddSeconds(1).UtcTicks);
        }
        finally
        {
            _strictRecoveryGate.Release();
        }
    }

    private bool NeedsStrictPersistenceCheck()
    {
        if (Volatile.Read(ref _strictRecoveryCompleted) == 0)
        {
            return true;
        }

        lock (_gate)
        {
            return _activeSession?.StrictMode.Enabled == true
                && _activeSession.State is SessionState.Activating or SessionState.Active
                && _timeProvider.GetUtcNow().ToUniversalTime().UtcTicks
                    >= Volatile.Read(ref _nextStrictReconciliationUtcTicks);
        }
    }

    public ValueTask<StopSessionResponse> StopNormalSessionAsync(CancellationToken cancellationToken) =>
        StopNormalCoreAsync(null, cancellationToken);

    public ValueTask<StopSessionResponse> StopReviewedAsync(ReviewedStopSessionRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StopNormalCoreAsync(request.SessionId, token);
    }

    private ValueTask<StopSessionResponse> StopNormalCoreAsync(Guid? expectedSessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RefreshNoLock();
            if (expectedSessionId is { } expected && (expected == Guid.Empty || _activeSession?.Id != expected))
                return ValueTask.FromResult(new StopSessionResponse(false, "SessionChanged"));
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

    public async ValueTask InterruptNormalSessionForShutdownAsync(CancellationToken cancellationToken)
    {
        Guid sessionId;
        lock (_gate)
        {
            RefreshNoLock();
            if (_activeSession is null
                || _activeSession.State is not (SessionState.Activating or SessionState.Active)
                || _activeSession.StrictMode.Enabled)
            {
                return;
            }

            sessionId = _activeSession.Id;
            _activeSession = _activeSession.TransitionTo(SessionState.Stopped);
            RecordClosureNoLock();
            _activePolicy = null;
        }

        await FlushHistoryAsync(cancellationToken).ConfigureAwait(false);
        await WriteInterruptionAuditAsync(sessionId, "Stopped", cancellationToken).ConfigureAwait(false);
        await _policyDistribution.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public SessionHistoryEntry? CreateInterruptionJournalEntry()
    {
        lock (_gate)
        {
            if (_activeSession is null
                || _activeSession.State is not (SessionState.Activating or SessionState.Active)
                || _activeSession.StrictMode.Enabled)
            {
                return null;
            }

            // A wall clock moved outside the session (rollback or a large jump) must not produce
            // an entry that ends before it starts or long after it was due to end.
            var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var clamped = nowUtc < _activeSession.StartUtc ? _activeSession.StartUtc
                : nowUtc > _activeSession.EndUtc ? _activeSession.EndUtc
                : nowUtc;
            return BuildClosureEntryNoLock(clamped, completed: false);
        }
    }

    public async ValueTask<bool> RecordOrphanedInterruptionAsync(
        SessionHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_historyStore is null
            || await _historyStore.GetAsync(entry.SessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        var blocked = await CountBlockedAttemptsAsync(entry.SessionId, cancellationToken).ConfigureAwait(false);
        await _historyStore.RecordAsync(
            entry with { Completed = false, BlockedAttempts = blocked },
            cancellationToken).ConfigureAwait(false);
        await WriteInterruptionAuditAsync(entry.SessionId, "ServiceTerminated", cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask WriteInterruptionAuditAsync(Guid sessionId, string outcome, CancellationToken cancellationToken)
    {
        try
        {
            await _auditStore.WriteAsync(
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    _timeProvider.GetUtcNow().ToUniversalTime(),
                    FocusAuditEventKind.SessionInterrupted,
                    "FocusGuard service",
                    outcome,
                    sessionId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // History is the primary record; a failed audit write must not block start or stop.
        }
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
                .ToArray(),
            CycleMetrics: entry.CycleMetrics,
            ProfileId: entry.ProfileId.Value,
            ScheduledIntent: entry.ScheduledIntent);
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
            _lastSkipResponse = null;
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
        _pendingHistory = BuildClosureEntryNoLock(
            _timeProvider.GetUtcNow().ToUniversalTime(),
            completed: _activeSession.State == SessionState.Expired);
    }

    private SessionHistoryEntry BuildClosureEntryNoLock(DateTimeOffset nowUtc, bool completed)
    {
        var session = _activeSession!;
        var planned = session.EndUtc - session.StartUtc;
        var actualEnd = nowUtc < session.EndUtc ? nowUtc : session.EndUtc;
        var cycleMetrics = session.CyclePlan?.CompletedMetricsAt(actualEnd);
        var phases = session.CyclePlan is { } plan
            ? plan.Phases.Where(p => p.StartUtc < actualEnd).Select(p => new SessionHistoryPhase(p.Kind, p.StartUtc, p.EndUtc < actualEnd ? p.EndUtc : actualEnd)).ToArray()
            : [new SessionHistoryPhase(SessionPhaseKind.Focus, session.StartUtc, actualEnd)];
        var focused = TimeSpan.FromTicks(phases.Where(p => p.Kind == SessionPhaseKind.Focus).Sum(p => Math.Max(0, (p.EndUtc - p.StartUtc).Ticks)));
        return new SessionHistoryEntry(
            session.Id,
            session.ProfileId,
            string.IsNullOrWhiteSpace(_activeProfileName) ? "Focus" : _activeProfileName,
            session.StartUtc,
            nowUtc,
            Completed: completed,
            PlannedMinutes: Math.Max(1, (int)Math.Round(planned.TotalMinutes, MidpointRounding.AwayFromZero)),
            FocusedMinutes: (int)Math.Max(0, Math.Floor(focused.TotalMinutes)),
            BlockedAttempts: 0,
            Phases: phases,
            CycleMetrics: cycleMetrics,
            ScheduledIntent: _activeScheduledIntent);
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

    private static EnforcementPolicy CreatePolicy(Session session, PrepareSessionRequest request) =>
        EnforcementPolicy.Create(
            session,
            ParseApplicationRules(request.ApplicationRules),
            ParseWebsiteRules(request.WebsiteRules),
            request.EnforcementEnabled,
            ParseApplicationRules(request.BreakApplicationRules),
            ParseWebsiteRules(request.BreakWebsiteRules));

    private static IReadOnlyList<ApplicationRule> ParseApplicationRules(
        IReadOnlyList<ApplicationPolicyRule>? rules) =>
        (rules ?? [])
            .Select(rule => new ApplicationRule(
                rule.Id,
                Enum.Parse<RuleAction>(rule.Action, ignoreCase: false),
                Enum.Parse<ApplicationMatchKind>(rule.MatchKind, ignoreCase: false),
                rule.Value.Trim()))
            .ToArray();

    private static IReadOnlyList<WebsiteRule> ParseWebsiteRules(
        IReadOnlyList<WebsitePolicyRule>? rules) =>
        (rules ?? [])
            .Select(rule => new WebsiteRule(
                rule.Id,
                Enum.Parse<RuleAction>(rule.Action, ignoreCase: false),
                rule.Pattern.Trim().ToLowerInvariant()))
            .ToArray();

    private static PhaseStatus? ToPhaseStatus(SessionPhase? phase, DateTimeOffset nowUtc) =>
        phase is null
            ? null
            : new PhaseStatus(
                phase.Index,
                phase.Kind,
                phase.FocusCycleNumber,
                Math.Max(0, (long)Math.Ceiling((phase.EndUtc - nowUtc).TotalSeconds)),
                phase.EndUtc,
                phase.StartUtc);

    private static SessionPhase? FindCurrentPhase(FocusCyclePlan? plan, DateTimeOffset nowUtc) =>
        plan?.Phases.FirstOrDefault(phase => nowUtc >= phase.StartUtc && nowUtc < phase.EndUtc);

    private static SessionPhase? FindNextPhase(FocusCyclePlan? plan, DateTimeOffset nowUtc) =>
        plan?.Phases.FirstOrDefault(phase => phase.StartUtc > nowUtc);

    private EnforcementPolicy? AuthorizeCurrentPhaseNoLock(DateTimeOffset nowUtc)
    {
        if (_activePolicy is null || _activeSession?.CyclePlan is null)
        {
            return null;
        }

        var phase = FindCurrentPhase(_activeSession.CyclePlan, nowUtc);
        var phaseIndex = phase?.Index ?? -1;
        var phaseEndUtc = phase?.EndUtc;
        if (_activePolicy.AuthorizedPhaseIndex == phaseIndex
            && _activePolicy.AuthorizedPhaseEndUtc == phaseEndUtc)
        {
            return null;
        }

        _activePolicy = _activePolicy with
        {
            CyclePlan = _activeSession.CyclePlan,
            AuthorizedPhaseIndex = phaseIndex,
            AuthorizedPhaseEndUtc = phaseEndUtc
        };
        return _activePolicy;
    }

    private static PreparedSessionPhase ToPreparedPhase(SessionPhase phase) =>
        new(phase.Index, phase.Kind, phase.StartUtc, phase.EndUtc, phase.FocusCycleNumber);

    private static PrepareSessionResponse PreparationRejected(string reason, SessionMode mode) =>
        new(false, reason, null, null, null, null, mode, []);

    private static ActivatePreparedSessionResponse ActivationPreparedRejected(string reason) =>
        new(false, reason, null, null, null, SessionMode.SimpleTimer);

    private sealed record PreparedSessionData(
        Guid Id,
        PrepareSessionRequest Request,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        DateTimeOffset ExpiresUtc,
        FocusCyclePlan? CyclePlan);
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
