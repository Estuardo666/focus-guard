using FocusGuard.Sync;
using System.Collections.Concurrent;
using Xunit;

public sealed class SettingsSyncSchedulerTests
{
    [Fact]
    public async Task LocalEditsAndReconnectWakeTheLoopAndAreCoalesced()
    {
        var enteredFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var retry = new SettingsRetryController(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                enteredFirst.TrySetResult();
                await releaseFirst.Task;
            }
            else secondRun.TrySetResult();
            return SettingsSyncStatus.Idle;
        }, () => true, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();

        var running = scheduler.RunAsync(stop.Token);
        await enteredFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(scheduler.NotifyLocalSettingsChanged());
        Assert.True(scheduler.NotifyConnectivityRestored());
        Assert.True(scheduler.NotifyLocalSettingsChanged());
        releaseFirst.TrySetResult();
        await secondRun.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DisabledConsentSleepsUntilExplicitConsentChangeSignal()
    {
        var consent = false;
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new SettingsRetryController(_ =>
        {
            called.TrySetResult();
            return Task.FromResult(SettingsSyncStatus.Idle);
        }, () => consent, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();

        var running = scheduler.RunAsync(stop.Token);
        Assert.False(await Task.WhenAny(called.Task, Task.Delay(100)) == called.Task);
        consent = true;
        scheduler.NotifyConsentChanged();
        await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task CriticalFailureWaitsForDeliberateRecoveryInsteadOfPolling()
    {
        var calls = 0;
        var attention = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new SettingsRetryController(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                attention.TrySetResult();
                throw new SettingsTransportException(SettingsTransportFailure.Authentication);
            }
            recovered.TrySetResult();
            return Task.FromResult(SettingsSyncStatus.Idle);
        }, () => true, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();

        var running = scheduler.RunAsync(stop.Token);
        await attention.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Assert.Equal(1, calls);
        await scheduler.ResumeAfterRecoveryAsync();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CheckpointRecoverySerializesAttentionReportAndWakesTheConsentedLoop()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Scheduler-RetryRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "retry.bin");
        await File.WriteAllTextAsync(path, "damaged checkpoint");
        var attention = new TaskCompletionSource<SettingsRetryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<SettingsRetryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new ConcurrentQueue<SettingsRetryResult>();
        var calls = 0;
        var retry = new SettingsRetryController(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(SettingsSyncStatus.Idle);
        }, () => true, TimeProvider.System,
            checkpointStore: new SettingsRetryCheckpointStore(path, new SettingsSyncScope(
                "https://identity.example/realms/test", Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"))));
        var scheduler = new SettingsSyncScheduler(retry, report: (result, _) =>
        {
            reports.Enqueue(result);
            if (result.Status == SettingsRetryStatus.NeedsAttention) attention.TrySetResult(result);
            else recovered.TrySetResult(result);
            return Task.CompletedTask;
        });
        using var stop = new CancellationTokenSource();
        Task? running = null;

        try
        {
            scheduler.NotifyConsentChanged();
            running = scheduler.RunAsync(stop.Token);
            var firstAttention = await attention.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(SettingsTransportFailure.Storage, firstAttention.Failure);
            Assert.True(firstAttention.RetryCheckpointRecoveryAvailable);

            await scheduler.RecoverStorageCheckpointAsync(ownerConfirmed: true);
            var resumed = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(SettingsRetryStatus.Idle, resumed.Status);
            Assert.False(resumed.RetryCheckpointRecoveryAvailable);
            Assert.Equal(1, calls);
            Assert.False(File.Exists(path));
            Assert.DoesNotContain(reports.SkipWhile(result => result.Status == SettingsRetryStatus.NeedsAttention),
                result => result.Status == SettingsRetryStatus.NeedsAttention);

            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
        finally
        {
            stop.Cancel();
            if (running is not null)
            {
                try { await running; }
                catch (OperationCanceledException) { }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConnectivitySignalDoesNotBypassRetryAfter()
    {
        var calls = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new SettingsRetryController(_ =>
        {
            Interlocked.Increment(ref calls);
            failed.TrySetResult();
            throw new SettingsTransportException(SettingsTransportFailure.Throttled, TimeSpan.FromSeconds(5));
        }, () => true, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();

        var running = scheduler.RunAsync(stop.Token);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(scheduler.NotifyConnectivityRestored());
        Assert.True(scheduler.NotifyConflictResolved());
        await Task.Delay(150);
        Assert.Equal(1, calls);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ConflictResolutionSignalBypassesConflictPollButOrdinaryEditDoesNot()
    {
        var conflicted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var retry = new SettingsRetryController(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                conflicted.TrySetResult();
                return Task.FromResult(SettingsSyncStatus.Conflict);
            }
            resolved.TrySetResult();
            return Task.FromResult(SettingsSyncStatus.Idle);
        }, () => true, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();

        var running = scheduler.RunAsync(stop.Token);
        await conflicted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(scheduler.NotifyLocalSettingsChanged());
        await Task.Delay(100);
        Assert.Equal(1, calls);

        Assert.True(scheduler.NotifyConflictResolved());
        await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ConcurrentRunOwnersAreRejected()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new SettingsRetryController(_ =>
        {
            entered.TrySetResult();
            return Task.FromResult(SettingsSyncStatus.Idle);
        }, () => true, TimeProvider.System);
        var scheduler = new SettingsSyncScheduler(retry);
        using var stop = new CancellationTokenSource();
        var running = scheduler.RunAsync(stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RunAsync());
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }
}
