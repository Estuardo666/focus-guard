using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Sync;
using Xunit;

public sealed class LocalSettingsCoordinatorTests
{
    [Fact]
    public async Task SharedResetAndRemoteTombstonePreservePerDeviceOverride()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await fixture.Vault().UpdateAsync(state => state! with { SyncConsentEnabled = true });
        var local = fixture.Coordinator();
        await local.UpdateOverridesAsync(3, new(), new(DefaultDurationMinutes: 90));
        var reset = await local.ResetSharedAsync(3, GlobalSettings.Default, true, new());
        var state = await fixture.Vault().LoadAsync();
        Assert.True(state!.DraftDeleted);
        Assert.Equal(GlobalSettings.Default, state.Draft);
        Assert.Equal(90, reset.Preferences.LastDurationMinutes);
        Assert.Equal(90, state.LocalOverrides.DefaultDurationMinutes);
        await fixture.Vault().UpdateAsync(current => current! with
        {
            Confirmed = new(4, GlobalSettings.Default, Deleted: true),
            Draft = null,
            DraftDeleted = false
        });
        var projected = await local.ReapplyAsync(new());
        Assert.Equal(90, projected.Preferences.LastDurationMinutes);
        Assert.True(projected.Preferences.EnforcementEnabled);
        Assert.False(projected.Pending);
        await local.UpdateOverridesAsync(4, state.LocalOverrides, new());
        Assert.Equal(GlobalSettings.Default.DefaultDurationMinutes,
            (await new DesktopSettingsSyncStore(fixture.Preferences).LoadAsync()).LastDurationMinutes);
    }

    [Fact]
    public async Task ReviewedImportRejectsConcurrentEditWithoutChangingDurableState()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await fixture.Vault().UpdateAsync(state => state! with { SyncConsentEnabled = true });
        var preview = new SettingsImportPreview(GlobalSettings.Default,
            GlobalSettings.Default with { Theme = "light" }, SettingsFields.Theme);
        await fixture.Coordinator().EditSelectedAsync(GlobalSettings.Default with { UiLanguage = "en" },
            SettingsFields.Language, new());
        var before = await fixture.Vault().LoadAsync();
        var preferencesBefore = await File.ReadAllBytesAsync(fixture.Preferences);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator().ImportReviewedAsync(preview, new()));
        Assert.Equal(before, await fixture.Vault().LoadAsync());
        Assert.Equal(preferencesBefore, await File.ReadAllBytesAsync(fixture.Preferences));
    }

    [Fact]
    public async Task ReviewedImportProjectsLocalOverridesAndPublishesOnlySharedSelection()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await fixture.Vault().UpdateAsync(state => state! with { SyncConsentEnabled = true });
        await fixture.Coordinator().UpdateOverridesAsync(3, new(), new(Theme: "light"));
        var baseline = GlobalSettings.Default with { Theme = "light" };
        var chosen = baseline with { Theme = "dark", UiLanguage = "en" };
        var result = await fixture.Coordinator().ImportReviewedAsync(
            new(baseline, chosen, SettingsFields.Theme | SettingsFields.Language), new());
        var state = await fixture.Vault().LoadAsync();
        Assert.Equal("dark", state!.LocalOverrides.Theme);
        Assert.Equal(GlobalSettings.Default with { UiLanguage = "en" }, state.Draft);
        Assert.Equal(chosen, DesktopSettingsProjection.Extract(result.Preferences));
        Assert.True(result.Preferences.EnforcementEnabled);
        Assert.True(result.SyncWakeRequired);
        var localOnly = await fixture.Coordinator().ImportReviewedAsync(
            new(chosen, chosen with { Theme = "light" }, SettingsFields.Theme), new());
        Assert.False(localOnly.SyncWakeRequired);
        Assert.Equal(state.Draft, (await fixture.Vault().LoadAsync())!.Draft);
        Assert.Equal("light", DesktopSettingsProjection.Extract(localOnly.Preferences).Theme);
        await fixture.Vault().UpdateAsync(current => current! with { SyncConsentEnabled = false });
        var disabled = await fixture.Vault().LoadAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator().ImportReviewedAsync(
            new(chosen, chosen with { DefaultDurationMinutes = 90 }, SettingsFields.Duration), new()));
        Assert.Equal(disabled, await fixture.Vault().LoadAsync());
    }

    [Fact]
    public async Task EditIsDurableBeforeProjectionAndRecoversAcrossInstances()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var chosen = new GlobalSettings(25, "light", "en");
        var result = await fixture.Coordinator().EditAsync(chosen, new());
        Assert.True(result.Pending); Assert.True(result.Preferences.EnforcementEnabled);
        Assert.Equal(chosen, DesktopSettingsProjection.Extract(result.Preferences));
        var replay = await fixture.Coordinator().ReapplyAsync(new());
        Assert.Equal(result.Preferences, replay.Preferences); Assert.True(replay.Pending);
        Assert.Equal(chosen, (await fixture.Vault().LoadAsync())!.Draft);
    }

    [Fact]
    public async Task PreferenceFailureDoesNotReportSuccessOrDiscardPersistedDraft()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await File.WriteAllTextAsync(fixture.Preferences, "broken");
        var chosen = new GlobalSettings(25, "light", "en");
        await Assert.ThrowsAnyAsync<JsonException>(() => fixture.Coordinator().EditAsync(chosen, new()));
        Assert.Equal(chosen, (await fixture.Vault().LoadAsync())!.Draft);
        Assert.Equal("broken", await File.ReadAllTextAsync(fixture.Preferences));
        await fixture.WritePreferences();
        Assert.Equal(chosen, DesktopSettingsProjection.Extract((await fixture.Coordinator().ReapplyAsync(new())).Preferences));
    }

    [Fact]
    public async Task TwoCoordinatorsLeavePreferencesMatchingTheFinalDurableDraft()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await Task.WhenAll(
            Task.Run(() => fixture.Coordinator().EditAsync(new(25, "light", "en"), new())),
            Task.Run(() => fixture.Coordinator().EditAsync(new(90, "dark", "es"), new())));
        var durable = await fixture.Vault().LoadAsync();
        var preferences = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllTextAsync(fixture.Preferences),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(durable!.Draft, DesktopSettingsProjection.Extract(preferences));
        Assert.True(preferences.EnforcementEnabled);
    }

    [Fact]
    public async Task IndependentSelectedEditsAreMergedFromFreshStateUnderSharedLease()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await Task.WhenAll(
            Task.Run(() => fixture.Coordinator().EditSelectedAsync(GlobalSettings.Default with { Theme = "light" }, SettingsFields.Theme, new())),
            Task.Run(() => fixture.Coordinator().EditSelectedAsync(GlobalSettings.Default with { UiLanguage = "en" }, SettingsFields.Language, new())));
        var state = await fixture.Vault().LoadAsync();
        Assert.Equal(new GlobalSettings(50, "light", "en"), state!.Draft);
        var preferences = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllTextAsync(fixture.Preferences),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(state.Draft, DesktopSettingsProjection.Extract(preferences));
    }

    [Fact]
    public async Task EmptySelectionCannotAlterOutboxOrPreferences()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var before = await fixture.Vault().LoadAsync(); var original = await File.ReadAllBytesAsync(fixture.Preferences);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Coordinator().EditSelectedAsync(new(25, "light", "en"), SettingsFields.None, new()));
        Assert.Equal(before, await fixture.Vault().LoadAsync()); Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Preferences));
    }

    [Fact]
    public async Task UninitializedAccountCannotCreateAnImplicitBaseline()
    {
        using var fixture = new Fixture(); await fixture.WritePreferences();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator().EditAsync(GlobalSettings.Default, new()));
        Assert.Null(await fixture.Vault().LoadAsync());
    }

    [Fact]
    public async Task DurableOverridesSurviveRestartFollowRemoteValuesAndClearToLatestGlobal()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var initial = await fixture.Vault().LoadAsync();
        var local = new LocalSettingsOverrides(Theme: "light", UiLanguage: "en", DefaultDurationMinutes: 90);
        var saved = await fixture.Coordinator().UpdateOverridesAsync(3, initial!.LocalOverrides, local);
        Assert.False(saved.SyncWakeRequired);
        Assert.False(saved.Pending);
        Assert.False((await fixture.Vault().LoadAsync())!.SyncConsentEnabled);

        var global = new GlobalSettings(25, "dark", "es");
        await fixture.Vault().UpdateAsync(state => state!.Receive(new(4, global)));
        var restarted = fixture.Coordinator();
        var afterRemote = await restarted.ReapplyAsync(new());
        Assert.Equal(AppTheme.Light, afterRemote.Preferences.Theme);
        Assert.Equal("en", afterRemote.Preferences.UiLanguage);
        Assert.Equal(90, afterRemote.Preferences.LastDurationMinutes);
        Assert.Equal(local, (await fixture.Vault().LoadAsync())!.LocalOverrides);

        var clearTheme = local with { Theme = null };
        var cleared = await restarted.UpdateOverridesAsync(4, local, clearTheme);
        Assert.False(cleared.SyncWakeRequired);
        Assert.Equal(AppTheme.Dark, cleared.Preferences.Theme); // Latest verified global value, not the original baseline.
        Assert.Equal("en", cleared.Preferences.UiLanguage);
        Assert.Equal(90, cleared.Preferences.LastDurationMinutes);
        Assert.Equal(global, (await fixture.Vault().LoadAsync())!.Confirmed.EffectiveValues);
    }

    [Fact]
    public async Task SelectedEditsUpdateOverriddenFieldsLocallyAndPublishOnlyUnoverriddenFields()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var coordinator = fixture.Coordinator();
        var empty = new LocalSettingsOverrides();
        await coordinator.UpdateOverridesAsync(3, empty, new(Theme: "dark"));

        var result = await coordinator.EditSelectedAsync(new(50, "light", "en"),
            SettingsFields.Theme | SettingsFields.Language, new());
        var state = await fixture.Vault().LoadAsync();
        Assert.Equal(new(50, "dark", "en"), state!.Draft); // Theme remains local-only.
        Assert.Equal("light", state.LocalOverrides.Theme);
        Assert.True(result.SyncWakeRequired); // Language is a shared edit.
        Assert.Equal(AppTheme.Light, result.Preferences.Theme);
        Assert.Equal("en", result.Preferences.UiLanguage);

        var bothLocal = new LocalSettingsOverrides(Theme: "light", UiLanguage: "en");
        await coordinator.UpdateOverridesAsync(3, state.LocalOverrides, bothLocal);
        var before = await fixture.Vault().LoadAsync();
        var localOnly = await coordinator.EditSelectedAsync(new(50, "dark", "es"),
            SettingsFields.Theme | SettingsFields.Language, new());
        var after = await fixture.Vault().LoadAsync();
        Assert.Equal(before!.Draft, after!.Draft);
        Assert.Equal("es", after.LocalOverrides.UiLanguage);
        Assert.Equal("dark", after.LocalOverrides.Theme);
        Assert.False(localOnly.SyncWakeRequired);
    }

    [Fact]
    public async Task OverrideUpdateRejectsStaleReviewAndDoesNotCreateCloudIntent()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var local = fixture.Coordinator();
        await local.UpdateOverridesAsync(3, new(), new(Theme: "light"));
        await fixture.Vault().UpdateAsync(state => state! with { Confirmed = new(4, GlobalSettings.Default) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => local.UpdateOverridesAsync(3, new(), new(Theme: "dark")));
        var state = await fixture.Vault().LoadAsync();
        Assert.Null(state!.Draft);
        Assert.Null(state.InFlight);
        Assert.False(state.SyncConsentEnabled);
        Assert.Equal("light", state.LocalOverrides.Theme);
    }

    [Fact]
    public async Task OverrideUpdateRejectsDraftChangeAtSameConfirmedRevision()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var local = fixture.Coordinator();
        var oldOverrides = new LocalSettingsOverrides(Theme: "light");
        await local.UpdateOverridesAsync(3, new(), oldOverrides);
        var reviewedEffective = oldOverrides.Apply(GlobalSettings.Default);
        var newDraft = GlobalSettings.Default with { UiLanguage = "en" };
        await fixture.Vault().UpdateAsync(state => state!.Edit(newDraft));

        await Assert.ThrowsAsync<InvalidOperationException>(() => local.UpdateOverridesAsync(3,
            oldOverrides, new(Theme: "dark"), expectedEffective: reviewedEffective));

        var persisted = await fixture.Vault().LoadAsync();
        Assert.Equal(oldOverrides, persisted!.LocalOverrides);
        Assert.Equal(newDraft, persisted.Draft);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "FocusGuard-Coordinator-" + Guid.NewGuid().ToString("N"));
        private readonly SettingsSyncScope scope = new("https://identity.example.invalid/realms/test", Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        public string Preferences => Path.Combine(root, "settings.json");
        public Fixture() => Directory.CreateDirectory(root);
        public SettingsOutboxVault Vault() => new(Path.Combine(root, "outbox.dpapi"), scope);
        public LocalSettingsCoordinator Coordinator() => new(Vault(), new(Preferences));
        public async Task Initialize() { await WritePreferences(); await Vault().SaveAsync(new(new(3, GlobalSettings.Default))); }
        public Task WritePreferences() => File.WriteAllTextAsync(Preferences,
            JsonSerializer.Serialize(new DesktopSettings(EnforcementEnabled: true), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        public void Dispose() => Directory.Delete(root, true);
    }
}
