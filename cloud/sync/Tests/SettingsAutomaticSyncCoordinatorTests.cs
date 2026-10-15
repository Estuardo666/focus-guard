using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsAutomaticSyncCoordinatorTests
{
    [Fact]
    public async Task EditPersistsAndProjectsBeforeWakeAndConsentStillGatesWorker()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-EditWake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        await vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default)));
        var preferencesPath = Path.Combine(root, "preferences.json");
        await File.WriteAllBytesAsync(preferencesPath, JsonSerializer.SerializeToUtf8Bytes(
            new DesktopSettings(EnforcementEnabled: true), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var local = new LocalSettingsCoordinator(vault, new DesktopSettingsSyncStore(preferencesPath));
        var consent = new SettingsConsentStore(vault);
        var proposed = new GlobalSettings(25, "light", "en");
        var expected = new GlobalSettings(50, "light", "es");
        var tickStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runtime = new SettingsAutomaticSyncRuntime(consent, (enabled, persisted) => async token =>
        {
            if (!enabled() || !await persisted(token)) return SettingsSyncStatus.Disabled;
            Interlocked.Increment(ref calls);
            var state = await vault.LoadAsync(token);
            Assert.Equal(expected, state!.Draft);
            var storedPreferences = JsonSerializer.Deserialize<DesktopSettings>(
                await File.ReadAllBytesAsync(preferencesPath, token), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(expected, DesktopSettingsProjection.Extract(storedPreferences));
            Assert.True(storedPreferences.EnforcementEnabled);
            tickStarted.TrySetResult();
            return SettingsSyncStatus.Idle;
        });
        var coordinator = new SettingsAutomaticSyncCoordinator(local, runtime);
        using var stop = new CancellationTokenSource();
        var running = runtime.RunAsync(stop.Token);
        try
        {
            await Task.Delay(100);
            var result = await coordinator.EditSelectedAsync(proposed, SettingsFields.Theme, new());
            Assert.True(result.Pending);
            Assert.Equal(expected, (await vault.LoadAsync())!.Draft);
            await Task.Delay(100);
            Assert.Equal(0, calls); // Stored edit and wake do not bypass the off-by-default consent gate.

            await runtime.EnableAsync(ownerConfirmed: true);
            await tickStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, calls);
        }
        finally
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            await runtime.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
