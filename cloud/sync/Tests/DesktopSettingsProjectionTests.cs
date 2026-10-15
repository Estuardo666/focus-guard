using FocusGuard.Client.Preferences;
using FocusGuard.Domain;
using FocusGuard.Sync;
using Xunit;

public sealed class DesktopSettingsProjectionTests
{
    [Fact]
    public void ApplyingGlobalValuesPreservesAllLocalPolicyAndProfilePreferences()
    {
        var local = new DesktopSettings(EnforcementEnabled: true, ReduceMotion: true, CompactTopmost: true,
            CyclePhaseNotifications: false, LastProfileId: Guid.NewGuid(), LastCyclePresetId: Guid.NewGuid(),
            LastSessionMode: SessionMode.SimpleTimer,
            ProfileThemes: new Dictionary<Guid, AppTheme> { [Guid.NewGuid()] = AppTheme.Light });
        var applied = DesktopSettingsProjection.Apply(local, new(25, "light", "en"), new());
        Assert.Equal(local with { Theme = AppTheme.Light, UiLanguage = "en", LastDurationMinutes = 25 }, applied);
        Assert.Same(local.ProfileThemes, applied.ProfileThemes);
        Assert.True(applied.EnforcementEnabled); Assert.True(applied.ReduceMotion);
    }

    [Fact]
    public void OverridesAffectOnlyLocalProjectionAndDoNotChangeGlobalDocument()
    {
        var global = new GlobalSettings(25, "light", "en");
        var local = DesktopSettingsProjection.Apply(DesktopSettings.Default, global, new(Theme: "dark", DefaultDurationMinutes: 90));
        Assert.Equal(AppTheme.Dark, local.Theme); Assert.Equal(90, local.LastDurationMinutes); Assert.Equal("en", local.UiLanguage);
        Assert.Equal(new GlobalSettings(25, "light", "en"), global);
    }

    [Fact]
    public void ExtractIncludesOnlyTheThreeAllowedFields()
    {
        var local = new DesktopSettings(EnforcementEnabled: true, CompactTopmost: true, Theme: AppTheme.Light,
            UiLanguage: "en", LastDurationMinutes: 90);
        Assert.Equal(new GlobalSettings(90, "light", "en"), DesktopSettingsProjection.Extract(local));
    }

    [Fact]
    public void AcknowledgingEarlierUploadCannotRevertTheVisibleNewerDraft()
    {
        var state = new SettingsOutbox(new(3, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" })
            .Prepare(_ => [1]);
        var intent = state.InFlight!.Intent;
        state = state.Edit(state.Draft! with { UiLanguage = "en", DefaultDurationMinutes = 25 });
        var acknowledged = state.Acknowledge(intent.OperationId, 4, intent.Values);
        var displayed = DesktopSettingsProjection.ApplyOutbox(DesktopSettings.Default, acknowledged, new());
        Assert.Equal("en", displayed.UiLanguage); Assert.Equal(25, displayed.LastDurationMinutes);
        Assert.Equal(AppTheme.Light, displayed.Theme); Assert.True(acknowledged.Pending);
    }

    [Fact]
    public void ConflictedDraftRemainsVisibleWithoutPublishingIt()
    {
        var state = new SettingsOutbox(new(3, GlobalSettings.Default)).Edit(GlobalSettings.Default with { DefaultDurationMinutes = 25 });
        var conflict = state.Receive(new(4, GlobalSettings.Default with { DefaultDurationMinutes = 90 }));
        Assert.Equal(25, DesktopSettingsProjection.ApplyOutbox(DesktopSettings.Default, conflict, new()).LastDurationMinutes);
        Assert.Equal(SettingsFields.Duration, conflict.Conflicts); Assert.Null(conflict.InFlight);
    }

    [Fact]
    public void InvalidLocalValueIsNotPublishedAsDefault()
    {
        Assert.Throws<ArgumentException>(() => DesktopSettingsProjection.Extract(DesktopSettings.Default with { Theme = (AppTheme)99 }));
        Assert.Throws<ArgumentException>(() => DesktopSettingsProjection.Extract(DesktopSettings.Default with { LastDurationMinutes = 0 }));
    }
}
