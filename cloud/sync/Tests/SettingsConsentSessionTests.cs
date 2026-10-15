using FocusGuard.Sync;
using Xunit;

public sealed class SettingsConsentSessionTests
{
    [Fact]
    public async Task ThrowingCancellationCallbackDoesNotSkipDurableDisable()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-ConsentFault-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), new("https://identity.example.invalid/realms/test",
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")));
            await vault.SaveAsync(new(new(0, GlobalSettings.Default), SyncConsentEnabled: true));
            var consent = new SettingsConsentStore(vault); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var session = new SettingsConsentSession(consent, async token =>
            {
                using var faulty = token.Register(() => throw new InvalidOperationException("Synthetic cancellation callback fault"));
                entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return SettingsSyncStatus.Idle;
            });
            await session.ResumeIfConsentedAsync(); var running = session.RunAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<AggregateException>(() => session.DisableAsync());
            Assert.False(await consent.IsEnabledAsync());
            Assert.Equal(SettingsSyncStatus.Disabled, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SettingsSyncStatus.Disabled, await session.RunAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DisableCancelsInFlightOperationAndPreventsNewRunsWithoutDiscardingDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-ConsentSession-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test", Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
            await vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" }));
            var store = new SettingsConsentStore(vault);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
            await using var session = new SettingsConsentSession(store, async token =>
            {
                calls++; entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return SettingsSyncStatus.Idle;
            });
            Assert.Equal(SettingsSyncStatus.Disabled, await session.RunAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResumeIfConsentedAsync());
            await store.SetAsync(true, true); await session.ResumeIfConsentedAsync();
            var running = session.RunAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.DisableAsync();
            Assert.Equal(SettingsSyncStatus.Disabled, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(SettingsSyncStatus.Disabled, await session.RunAsync()); Assert.Equal(1, calls);
            Assert.False(await store.IsEnabledAsync()); Assert.Equal("light", (await vault.LoadAsync())!.Draft!.Theme);
            await store.SetAsync(true, true); await session.ResumeIfConsentedAsync();
            var restarted = session.RunAsync();
            await session.DisposeAsync();
            Assert.Equal(SettingsSyncStatus.Disabled, await restarted.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RunAsync());
        }
        finally { Directory.Delete(root, true); }
    }
}
