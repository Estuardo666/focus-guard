using FocusGuard.Sync;
using Xunit;

public sealed class SettingsAutomaticSyncRuntimeTests
{
    [Fact]
    public async Task OptInStartsTheLoopAndRevocationCancelsWorkWithoutDeletingDraft()
    {
        var root = NewRoot();
        try
        {
            var vault = await NewVaultAsync(root, consent: false);
            var store = new SettingsConsentStore(vault);
            var entered = NewSignal();
            var canceled = NewSignal();
            var calls = 0;
            await using var runtime = new SettingsAutomaticSyncRuntime(store, (enabled, persisted) => async token =>
            {
                if (!enabled() || !await persisted(token)) return SettingsSyncStatus.Disabled;
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
                return SettingsSyncStatus.Idle;
            });
            using var stop = new CancellationTokenSource();

            var running = runtime.RunAsync(stop.Token);
            Assert.True(runtime.NotifyLocalSettingsChanged());
            await Task.Delay(100);
            Assert.Equal(0, calls);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.EnableAsync(ownerConfirmed: false));
            Assert.False(await store.IsEnabledAsync());

            await runtime.EnableAsync(ownerConfirmed: true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await runtime.DisableAsync();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            Assert.False(await store.IsEnabledAsync());
            Assert.Equal(1, calls);
            Assert.Equal("light", (await vault.LoadAsync())!.Draft!.Theme);

            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ExplicitRunResumesPreviouslySavedOptInButConstructionDoesNotStartWork()
    {
        var root = NewRoot();
        try
        {
            var store = new SettingsConsentStore(await NewVaultAsync(root, consent: true));
            var entered = NewSignal();
            var calls = 0;
            await using var runtime = new SettingsAutomaticSyncRuntime(store, (enabled, persisted) => token =>
            {
                Assert.True(enabled());
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                return Task.FromResult(SettingsSyncStatus.Idle);
            });

            await Task.Delay(100);
            Assert.Equal(0, calls);
            using var stop = new CancellationTokenSource();
            var running = runtime.RunAsync(stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await store.IsEnabledAsync());
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DisposeCancelsOwnerLoopButPreservesDurableOptIn()
    {
        var root = NewRoot();
        try
        {
            var store = new SettingsConsentStore(await NewVaultAsync(root, consent: true));
            var entered = NewSignal();
            var runtime = new SettingsAutomaticSyncRuntime(store, (_, _) => async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return SettingsSyncStatus.Idle;
            });
            var running = runtime.RunAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await runtime.DisposeAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.True(await store.IsEnabledAsync());
            await runtime.DisposeAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-AutomaticSync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<SettingsOutboxVault> NewVaultAsync(string root, bool consent)
    {
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test", Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        var state = new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: consent)
            .Edit(GlobalSettings.Default with { Theme = "light" });
        await vault.SaveAsync(state);
        return vault;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
