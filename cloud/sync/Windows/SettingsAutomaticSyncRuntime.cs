namespace FocusGuard.Sync;

/// <summary>Creates a worker tick with both the in-memory runtime gate and persisted consent check.</summary>
public delegate Func<CancellationToken, Task<SettingsSyncStatus>> SettingsSyncTickFactory(
    Func<bool> runtimeEnabled, Func<CancellationToken, Task<bool>> persistedConsent);

/// <summary>
/// Explicit, caller-owned composition for automatic settings reconciliation. Constructing this
/// object never starts work or enables consent. RunAsync starts the loop only when called by its
/// owner; previously saved consent is resumed, while a new opt-in must use EnableAsync.
/// </summary>
public sealed class SettingsAutomaticSyncRuntime : IAsyncDisposable
{
    private readonly SettingsConsentStore consent;
    private readonly SettingsConsentSession session;
    private readonly SettingsSyncScheduler scheduler;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object sync = new();
    private TaskCompletionSource? loopFinished;
    private Task? disposeTask;
    private int enabled;
    private int runOwner;
    private int disposed;

    public SettingsAutomaticSyncRuntime(SettingsConsentStore consent,
        SettingsSyncTickFactory createTick,
        SettingsRetryCheckpointStore? checkpointStore = null,
        TimeProvider? timeProvider = null,
        Func<double>? jitter = null,
        Func<SettingsRetryResult, CancellationToken, Task>? report = null)
    {
        this.consent = consent ?? throw new ArgumentNullException(nameof(consent));
        ArgumentNullException.ThrowIfNull(createTick);
        var active = (Func<bool>)(() => Volatile.Read(ref enabled) != 0);
        var tick = createTick(active, consent.IsEnabledAsync) ??
            throw new InvalidOperationException("The settings tick factory returned no worker.");
        var clock = timeProvider ?? TimeProvider.System;
        session = new SettingsConsentSession(consent, tick);
        var retry = new SettingsRetryController(session.RunAsync, active, clock, jitter, checkpointStore);
        scheduler = new SettingsSyncScheduler(retry, clock, report);
    }

    /// <summary>Persist a deliberate opt-in before allowing the scheduler to run.</summary>
    public async Task EnableAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(token);
        try
        {
            ThrowIfDisposed();
            await consent.SetAsync(true, ownerConfirmed, token);
            await session.ResumeIfConsentedAsync(token);
            Volatile.Write(ref enabled, 1);
            scheduler.NotifyConsentChanged();
        }
        finally { lifecycle.Release(); }
    }

    /// <summary>Durably revoke consent, cancel an in-flight tick, and preserve local drafts.</summary>
    public async Task DisableAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(token);
        try
        {
            ThrowIfDisposed();
            Volatile.Write(ref enabled, 0);
            try { await session.DisableAsync(token); }
            finally { scheduler.NotifyConsentChanged(); }
        }
        finally { lifecycle.Release(); }
    }

    /// <summary>Start the loop explicitly for this owner lifetime; does not enable consent.</summary>
    public async Task RunAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref runOwner, 1, 0) != 0)
            throw new InvalidOperationException("The automatic settings runtime already has an owner.");
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync) loopFinished = finished;
        try
        {
            await lifecycle.WaitAsync(token);
            try
            {
                ThrowIfDisposed();
                if (await consent.IsEnabledAsync(token))
                {
                    await session.ResumeIfConsentedAsync(token);
                    Volatile.Write(ref enabled, 1);
                }
                else Volatile.Write(ref enabled, 0);
            }
            finally { lifecycle.Release(); }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            await scheduler.RunAsync(linked.Token);
        }
        finally
        {
            finished.TrySetResult();
            Volatile.Write(ref runOwner, 0);
        }
    }

    public bool NotifyLocalSettingsChanged()
    {
        ThrowIfDisposed();
        return scheduler.NotifyLocalSettingsChanged();
    }

    public bool NotifyConnectivityRestored()
    {
        ThrowIfDisposed();
        return scheduler.NotifyConnectivityRestored();
    }

    /// <summary>Call after the user has saved an explicit resolution of a settings conflict.</summary>
    public bool NotifyConflictResolved()
    {
        ThrowIfDisposed();
        return scheduler.NotifyConflictResolved();
    }

    /// <summary>Only after deliberate local recovery or re-authentication.</summary>
    public Task ResumeAfterRecoveryAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        return scheduler.ResumeAfterRecoveryAsync(token);
    }

    /// <summary>Recover a corrupt retry throttle after owner confirmation, then wake the existing consented loop.</summary>
    public async Task RecoverStorageCheckpointAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        ThrowIfDisposed();
        await lifecycle.WaitAsync(token);
        try
        {
            ThrowIfDisposed();
            await scheduler.RecoverStorageCheckpointAsync(ownerConfirmed, token);
        }
        finally { lifecycle.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposeTask is null)
            {
                Volatile.Write(ref disposed, 1);
                disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Exception? cancellationFailure = null;
        try { lifetime.Cancel(); }
        catch (AggregateException error) { cancellationFailure = error; }

        await lifecycle.WaitAsync();
        try { await session.DisposeAsync(); }
        finally { lifecycle.Release(); }

        Task? running;
        lock (sync) running = loopFinished?.Task;
        if (running is not null) await running;
        lifecycle.Dispose();
        lifetime.Dispose();
        if (cancellationFailure is not null) throw cancellationFailure;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
}
