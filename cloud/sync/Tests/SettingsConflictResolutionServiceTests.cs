using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsConflictResolutionServiceTests
{
    [Fact]
    public async Task ResolutionMustMatchReviewedRevisionAndWakeOnlyAfterDurableCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Conflict-Resolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        var remote = new GlobalSettings(50, "dark", "es");
        var localDraft = new GlobalSettings(50, "light", "en");
        var chosen = remote; // Choose the remote value for Theme; keep the unrelated local language draft.
        var expectedResolvedDraft = new GlobalSettings(50, "dark", "en");
        var before = new SettingsOutbox(new(4, remote), Draft: localDraft, Conflicts: SettingsFields.Theme, SyncConsentEnabled: true);
        await vault.SaveAsync(before);
        var profileId = Guid.NewGuid();
        var preferencesPath = Path.Combine(root, "preferences.json");
        var preferences = new DesktopSettingsSyncStore(preferencesPath);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var originalPreferences = new DesktopSettings(Theme: AppTheme.Light, ReduceMotion: true,
            ProfileThemes: new Dictionary<Guid, AppTheme> { [profileId] = AppTheme.Light },
            EnforcementEnabled: true, UiLanguage: "en");
        await File.WriteAllBytesAsync(preferencesPath, JsonSerializer.SerializeToUtf8Bytes(originalPreferences, json));
        var originalPreferenceBytes = await File.ReadAllBytesAsync(preferencesPath);
        var consent = new SettingsConsentStore(vault);
        var conflictReached = NewSignal();
        var retryReached = NewSignal();
        var calls = 0;
        var runtime = new SettingsAutomaticSyncRuntime(consent, (enabled, persisted) => async token =>
        {
            if (!enabled() || !await persisted(token)) return SettingsSyncStatus.Disabled;
            if (Interlocked.Increment(ref calls) == 1)
            {
                conflictReached.TrySetResult();
                return SettingsSyncStatus.Conflict;
            }

            var stored = await vault.LoadAsync(token);
            Assert.Equal(SettingsFields.None, stored!.Conflicts);
            Assert.Equal(expectedResolvedDraft, stored.Draft);
            var projected = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(preferencesPath, token), json)!;
            Assert.Equal(AppTheme.Dark, projected.Theme);
            Assert.Equal("en", projected.UiLanguage);
            Assert.True(projected.EnforcementEnabled);
            Assert.True(projected.ReduceMotion);
            Assert.Equal(AppTheme.Light, projected.ProfileThemes![profileId]);
            retryReached.TrySetResult();
            return SettingsSyncStatus.Idle;
        });
        using var stop = new CancellationTokenSource();
        var running = runtime.RunAsync(stop.Token);
        try
        {
            await conflictReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var service = new SettingsConflictResolutionService(vault, preferences, new(), runtime);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResolveAsync(chosen, localDraft, 4, SettingsFields.Theme, ownerConfirmed: false));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResolveAsync(chosen, localDraft, 3, SettingsFields.Theme, ownerConfirmed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ResolveAsync(chosen, remote with { UiLanguage = "en" }, 4, SettingsFields.Theme, ownerConfirmed: true));
            Assert.Equal(before, await vault.LoadAsync());
            Assert.Equal(originalPreferenceBytes, await File.ReadAllBytesAsync(preferencesPath));
            Assert.Equal(1, calls);

            var resolved = await service.ResolveAsync(chosen, localDraft, 4, SettingsFields.Theme, ownerConfirmed: true);
            Assert.Equal(SettingsFields.None, resolved.Conflicts);
            Assert.Equal(expectedResolvedDraft, resolved.Draft);
            await retryReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, calls);
        }
        finally
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            await runtime.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsConflictResolutionCannotEraseAResetTombstone()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Conflict-Reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        var tombstone = new SettingsOutbox(new(4, GlobalSettings.Default), Draft: GlobalSettings.Default,
            Conflicts: SettingsFields.Theme, DraftDeleted: true);
        await vault.SaveAsync(tombstone);
        var preferencesPath = Path.Combine(root, "preferences.json");
        var preferences = new DesktopSettingsSyncStore(preferencesPath);
        var originalPreferences = JsonSerializer.SerializeToUtf8Bytes(new DesktopSettings(EnforcementEnabled: true),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllBytesAsync(preferencesPath, originalPreferences);
        var runtime = new SettingsAutomaticSyncRuntime(new SettingsConsentStore(vault), (_, _) =>
            _ => Task.FromResult(SettingsSyncStatus.Disabled));
        try
        {
            var service = new SettingsConflictResolutionService(vault, preferences, new(), runtime);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(
                GlobalSettings.Default, GlobalSettings.Default, 4, SettingsFields.Theme, ownerConfirmed: true));
            Assert.Equal(tombstone, await vault.LoadAsync());
            Assert.Equal(originalPreferences, await File.ReadAllBytesAsync(preferencesPath));
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetConflictRequiresFreshExplicitChoiceAndEitherRebasesOrCancels(bool keepThisDeviceReset)
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Reset-Conflict-Choice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        var remote = new GlobalSettings(25, "light", "en");
        var resetConflict = new SettingsOutbox(new(4, remote), Draft: GlobalSettings.Default,
            Conflicts: SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration, DraftDeleted: true,
            SyncConsentEnabled: true);
        await vault.SaveAsync(resetConflict);
        var preferencesPath = Path.Combine(root, "preferences.json");
        var preferences = new DesktopSettingsSyncStore(preferencesPath);
        var originalSettings = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllBytesAsync(preferencesPath, JsonSerializer.SerializeToUtf8Bytes(originalSettings, json));
        var originalBytes = await File.ReadAllBytesAsync(preferencesPath);
        var runtime = new SettingsAutomaticSyncRuntime(new SettingsConsentStore(vault), (_, _) =>
            _ => Task.FromResult(SettingsSyncStatus.Disabled));
        try
        {
            var service = new SettingsConflictResolutionService(vault, preferences, new(), runtime);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepThisDeviceReset, GlobalSettings.Default, 4, resetConflict.Conflicts, ownerConfirmed: false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepThisDeviceReset, GlobalSettings.Default, 3, resetConflict.Conflicts, ownerConfirmed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepThisDeviceReset, remote, 4, resetConflict.Conflicts, ownerConfirmed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepThisDeviceReset, GlobalSettings.Default, 4, SettingsFields.Theme, ownerConfirmed: true));
            Assert.Equal(resetConflict, await vault.LoadAsync());
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(preferencesPath));

            var result = await service.ResolveResetAsync(keepThisDeviceReset, GlobalSettings.Default, 4,
                resetConflict.Conflicts, ownerConfirmed: true);
            Assert.Equal(SettingsFields.None, result.Conflicts);
            Assert.Equal(4, result.Confirmed.Revision);
            Assert.Null(result.InFlight);
            if (keepThisDeviceReset)
            {
                Assert.True(result.DraftDeleted);
                Assert.Equal(GlobalSettings.Default, result.Draft);
                Assert.Equal(remote, result.Confirmed.Values);
                Assert.True(result.SyncConsentEnabled);
            }
            else
            {
                Assert.False(result.DraftDeleted);
                Assert.Null(result.Draft);
                Assert.Equal(remote, result.Confirmed.Values);
            }

            var projected = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(preferencesPath), json)!;
            Assert.Equal(keepThisDeviceReset ? AppTheme.Dark : AppTheme.Light, projected.Theme);
            Assert.Equal(keepThisDeviceReset ? "es" : "en", projected.UiLanguage);
            Assert.Equal(keepThisDeviceReset ? 50 : 25, projected.LastDurationMinutes);
            Assert.True(projected.ReduceMotion);
            Assert.True(projected.EnforcementEnabled);
            Assert.False(projected.CyclePhaseNotifications);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetConflictRecoveryRebasesDeletionOrAcceptsCurrentRemoteSnapshot(bool keepLocalReset)
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Reset-Conflict-Recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
        var remote = new GlobalSettings(35, "light", "en");
        var conflictMask = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
        var resetConflict = new SettingsOutbox(new(8, remote), Draft: GlobalSettings.Default,
            Conflicts: conflictMask, DraftDeleted: true, SyncConsentEnabled: true);
        await vault.SaveAsync(resetConflict);
        var preferencesPath = Path.Combine(root, "preferences.json");
        var preferences = new DesktopSettingsSyncStore(preferencesPath);
        var original = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllBytesAsync(preferencesPath, JsonSerializer.SerializeToUtf8Bytes(original, json));
        var runtime = new SettingsAutomaticSyncRuntime(new SettingsConsentStore(vault), (_, _) =>
            _ => Task.FromResult(SettingsSyncStatus.Disabled));
        try
        {
            var service = new SettingsConflictResolutionService(vault, preferences, new(), runtime);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepLocalReset, GlobalSettings.Default, 8, conflictMask, ownerConfirmed: false));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepLocalReset, GlobalSettings.Default, 7, conflictMask, ownerConfirmed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepLocalReset, remote, 8, conflictMask, ownerConfirmed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveResetAsync(
                keepLocalReset, GlobalSettings.Default, 8, SettingsFields.Theme, ownerConfirmed: true));
            Assert.Equal(resetConflict, await vault.LoadAsync());
            Assert.Equal(original, JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(preferencesPath), json));

            var resolved = await service.ResolveResetAsync(keepLocalReset, GlobalSettings.Default, 8,
                conflictMask, ownerConfirmed: true);
            Assert.Equal(SettingsFields.None, resolved.Conflicts);
            Assert.Equal(8, resolved.Confirmed.Revision);
            Assert.Null(resolved.InFlight);
            if (keepLocalReset)
            {
                Assert.True(resolved.DraftDeleted);
                Assert.Equal(GlobalSettings.Default, resolved.Draft);
                Assert.True(resolved.SyncConsentEnabled);
            }
            else
            {
                Assert.False(resolved.DraftDeleted);
                Assert.Null(resolved.Draft);
            }

            var projected = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(preferencesPath), json)!;
            Assert.Equal(keepLocalReset ? AppTheme.Dark : AppTheme.Light, projected.Theme);
            Assert.Equal(keepLocalReset ? "es" : "en", projected.UiLanguage);
            Assert.Equal(keepLocalReset ? 50 : 35, projected.LastDurationMinutes);
            Assert.True(projected.ReduceMotion);
            Assert.True(projected.EnforcementEnabled);
            Assert.False(projected.CyclePhaseNotifications);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
