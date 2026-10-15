namespace FocusGuard.Sync;

/// <summary>Explicit local lifecycle; starts suspended even if stored consent exists. No timers or auto-start.</summary>
public sealed class SettingsConsentSession(SettingsConsentStore consent,
    Func<CancellationToken, Task<SettingsSyncStatus>> tick) : IDisposable, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly List<CancellationTokenSource> generations = new();
    private CancellationTokenSource? active;
    private bool disposed;
    private int runs;
    private TaskCompletionSource drained = CompletedDrain();

    private static TaskCompletionSource CompletedDrain()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult(); return result;
    }

    public async Task ResumeIfConsentedAsync(CancellationToken token = default)
    {
        await changes.WaitAsync(token);
        try
        {
            if (!await consent.IsEnabledAsync(token)) throw new InvalidOperationException("Stored local consent is required.");
            Task completion;
            lock (sync) completion = active is null ? drained.Task : Task.CompletedTask;
            await completion.WaitAsync(token);
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (active is null)
                {
                    foreach (var previous in generations) previous.Dispose();
                    generations.Clear();
                    active = new CancellationTokenSource(); generations.Add(active);
                }
            }
        }
        finally { changes.Release(); }
    }

    public async Task<SettingsSyncStatus> RunAsync(CancellationToken token = default)
    {
        CancellationToken generation;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active is null) return SettingsSyncStatus.Disabled;
            generation = active.Token;
            if (runs++ == 0) drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, generation);
            if (!await consent.IsEnabledAsync(linked.Token)) return SettingsSyncStatus.Disabled;
            linked.Token.ThrowIfCancellationRequested();
            return await tick(linked.Token);
        }
        catch (OperationCanceledException) when (generation.IsCancellationRequested && !token.IsCancellationRequested)
        { return SettingsSyncStatus.Disabled; }
        finally { lock (sync) { if (--runs == 0) drained.TrySetResult(); } }
    }

    public async Task DisableAsync(CancellationToken token = default)
    {
        await changes.WaitAsync(token);
        try
        {
            CancellationTokenSource? previous;
            lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); previous = active; active = null; }
            try { previous?.Cancel(); } // Outside lock: callbacks must not deadlock on the lifecycle lock.
            finally
            {
                // A throwing cancellation callback must not skip the durable revocation.
                await consent.SetAsync(false, false, token);
            }
            Task completion;
            lock (sync) completion = drained.Task;
            await completion.WaitAsync(token);
            lock (sync)
            {
                if (previous is not null) { generations.Remove(previous); previous.Dispose(); }
            }
        }
        finally { changes.Release(); }
    }

    public void Dispose()
    {
        CancellationTokenSource[] pending;
        lock (sync) { if (disposed) return; disposed = true; active = null; pending = generations.ToArray(); }
        foreach (var source in pending) source.Cancel();
        // Async disposal drains runs before freeing sources. Synchronous disposal only suspends/cancels.
    }

    public async ValueTask DisposeAsync()
    {
        await changes.WaitAsync();
        try
        {
            Dispose();
            Task completion;
            lock (sync) completion = drained.Task;
            await completion;
            lock (sync)
            {
                foreach (var source in generations) source.Dispose();
                generations.Clear();
            }
        }
        finally { changes.Release(); }
    }
}
