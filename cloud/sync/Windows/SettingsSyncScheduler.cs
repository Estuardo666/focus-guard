using System.Threading.Channels;

namespace FocusGuard.Sync;

/// <summary>
/// Runs caller-owned automatic reconciliation while the caller is active. It never enables
/// consent, persists data itself, or starts from application startup. The owner must start
/// this loop only after an explicit local opt-in has already been stored.
/// </summary>
public sealed class SettingsSyncScheduler
{
    private readonly SettingsRetryController retry;
    private readonly TimeProvider timeProvider;
    private readonly Func<SettingsRetryResult, CancellationToken, Task>? report;
    private readonly SemaphoreSlim controlGate = new(1, 1);
    private readonly Channel<byte> wakeups = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private int pendingReasons;
    private int running;

    public SettingsSyncScheduler(SettingsRetryController retry, TimeProvider? timeProvider = null,
        Func<SettingsRetryResult, CancellationToken, Task>? report = null)
    {
        this.retry = retry ?? throw new ArgumentNullException(nameof(retry));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.report = report;
    }

    /// <summary>Coalesced wake-up after a local settings edit.</summary>
    public bool NotifyLocalSettingsChanged() => Signal(SettingsSyncWakeReason.LocalSettingsChanged);

    /// <summary>Coalesced wake-up after connectivity returns. Retry-After remains enforced.</summary>
    public bool NotifyConnectivityRestored() => Signal(SettingsSyncWakeReason.ConnectivityRestored);

    /// <summary>Call after a deliberate local consent change.</summary>
    public bool NotifyConsentChanged() => Signal(SettingsSyncWakeReason.ConsentChanged);

    /// <summary>Call only after persisting an explicit resolution of a settings conflict.</summary>
    public bool NotifyConflictResolved() => Signal(SettingsSyncWakeReason.ConflictResolved);

    /// <summary>Compatibility shorthand for a local settings edit.</summary>
    public bool NotifyChanged() => NotifyLocalSettingsChanged();

    /// <summary>Only call after the user has completed re-authentication or other recovery.</summary>
    public async Task ResumeAfterRecoveryAsync(CancellationToken token = default)
    {
        await controlGate.WaitAsync(token);
        try
        {
            await retry.ResumeAfterRecoveryAsync(token);
            Signal(SettingsSyncWakeReason.RecoveryCompleted);
        }
        finally { controlGate.Release(); }
    }

    /// <summary>Requires a deliberate owner action and an active storage failure; clears only its retry checkpoint.</summary>
    public async Task RecoverStorageCheckpointAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        await controlGate.WaitAsync(token);
        try
        {
            await retry.ResetStorageCheckpointAfterOwnerRecoveryAsync(ownerConfirmed, token);
            await retry.ResumeAfterRecoveryAsync(token);
            Signal(SettingsSyncWakeReason.RecoveryCompleted);
        }
        finally { controlGate.Release(); }
    }

    /// <summary>Runs until canceled. A second concurrent owner is rejected.</summary>
    public async Task RunAsync(CancellationToken token = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("A settings sync scheduler is already running.");
        try
        {
            var reason = SettingsSyncWakeReason.Startup;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                SettingsRetryResult result;
                await controlGate.WaitAsync(token);
                try
                {
                    result = await retry.RunIfDueAsync(reason, token);
                    if (report is not null) await report(result, token);
                }
                finally { controlGate.Release(); }

                if (result.Status is SettingsRetryStatus.Disabled or SettingsRetryStatus.NeedsAttention)
                {
                    reason = await WaitForSignalAsync(token);
                    continue;
                }

                reason = result.Wait is { } wait
                    ? await WaitForSignalOrDelayAsync(wait, token)
                    : await WaitForSignalAsync(token);
            }
        }
        finally { Volatile.Write(ref running, 0); }
    }

    private async Task<SettingsSyncWakeReason> WaitForSignalAsync(CancellationToken token)
    {
        if (wakeups.Reader.TryRead(out _)) { DrainSignals(); return TakeReasons(); }
        if (!await wakeups.Reader.WaitToReadAsync(token)) throw new InvalidOperationException("Settings sync scheduler was closed.");
        DrainSignals();
        return TakeReasons();
    }

    private async Task<SettingsSyncWakeReason> WaitForSignalOrDelayAsync(TimeSpan delay, CancellationToken token)
    {
        if (wakeups.Reader.TryRead(out _)) { DrainSignals(); return TakeReasons(); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        var available = wakeups.Reader.WaitToReadAsync(linked.Token).AsTask();
        var timer = Task.Delay(delay, timeProvider, linked.Token);
        var completed = await Task.WhenAny(available, timer);
        linked.Cancel();
        if (completed == timer)
        {
            await timer;
            return SettingsSyncWakeReason.Scheduled;
        }
        if (!await available) throw new InvalidOperationException("Settings sync scheduler was closed.");
        DrainSignals();
        return TakeReasons();
    }

    private void DrainSignals()
    {
        while (wakeups.Reader.TryRead(out _)) { }
    }

    private bool Signal(SettingsSyncWakeReason reason)
    {
        Interlocked.Or(ref pendingReasons, (int)reason);
        _ = wakeups.Writer.TryWrite(0); // A queued signal already represents this wake-up.
        return true;
    }

    private SettingsSyncWakeReason TakeReasons()
    {
        return (SettingsSyncWakeReason)Interlocked.Exchange(ref pendingReasons, 0);
    }
}
