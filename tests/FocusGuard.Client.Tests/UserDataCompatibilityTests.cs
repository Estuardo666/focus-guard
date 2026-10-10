using System.Text.Json;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Preferences;
using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class UserDataCompatibilityTests
{
    [Fact]
    public void LegacyPreferencesKeepEnumNumbersDefaultsAndOptInAcrossReload()
    {
        using var data = new UserData();
        File.WriteAllText(data.SettingsPath, "{\"sessionEnd\":3,\"theme\":1,\"reduceMotion\":true,\"enforcementEnabled\":true,\"lastDurationMinutes\":47,\"compactTopmost\":true}");
        var store = new DesktopSettingsStore(data.SettingsPath);
        var settings = store.Load();
        Assert.Equal(SessionEndPresentation.None, settings.SessionEnd);
        Assert.Equal(AppTheme.Light, settings.Theme);
        Assert.True(settings.EnforcementEnabled);
        Assert.True(settings.ReduceMotion);
        Assert.True(settings.CompactTopmost);
        Assert.Equal(47, settings.LastDurationMinutes);
        Assert.Equal(SessionMode.SimpleTimer, settings.LastSessionMode);
        store.Save(settings);
        using var json = JsonDocument.Parse(File.ReadAllText(data.SettingsPath));
        Assert.Equal(3, json.RootElement.GetProperty("sessionEnd").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("theme").GetInt32());
        Assert.True(new DesktopSettingsStore(data.SettingsPath).Load().EnforcementEnabled);
    }

    [Fact]
    public void LegacyJournalLoadsWithoutClrTypeNamesAndPreservesReflections()
    {
        using var data = new UserData();
        var sessionId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        File.WriteAllText(data.JournalPath,
            "[{\"sessionId\":\"" + sessionId + "\",\"profileId\":\"" + profileId + "\",\"profileName\":\"Work\",\"durationMinutes\":50,\"mode\":0,\"cyclePresetId\":null,\"cycleConfiguration\":null,\"strict\":true,\"objective\":\"Read\",\"reflection\":\"Done\"}]");
        var workflow = data.Workflow();
        Assert.Equal("Read", workflow.Get(sessionId)!.Objective);
        Assert.True(workflow.Get(sessionId)!.Strict);
        workflow.SaveReflection(sessionId, new string('r', 1100));
        var restored = data.Workflow().Get(sessionId)!;
        Assert.Equal(profileId, restored.ProfileId);
        Assert.Equal("Read", restored.Objective);
        Assert.Equal(1000, restored.Reflection.Length);
        Assert.DoesNotContain("$type", File.ReadAllText(data.JournalPath));
        Assert.False(File.Exists(data.JournalPath + ".tmp"));
    }

    [Fact]
    public void ReflectionUpdateUsesLatestJournalEntryAndPreservesItsObjective()
    {
        using var data = new UserData();
        var sessionId = Guid.NewGuid();
        var intention = new SessionIntention(sessionId, Guid.NewGuid(), "Work", 50,
            SessionMode.SimpleTimer, null, null, false, "Read the design brief");
        new SessionJournalStore(data.JournalPath).Save(intention);

        Assert.True(new SessionJournalStore(data.JournalPath).SaveReflection(sessionId, "  Ship the draft  "));
        var restored = new SessionJournalStore(data.JournalPath).GetLatest(sessionId)!;

        Assert.Equal(intention.Objective, restored.Objective);
        Assert.Equal("Ship the draft", restored.Reflection);
    }

    [Fact]
    public void AcceptedCycleConfigurationPersistsWithoutChangingUnrelatedPreferences()
    {
        using var data = new UserData();
        var current = new DesktopSettings(SessionEnd: SessionEndPresentation.None, Theme: AppTheme.Light,
            ReduceMotion: true, EnforcementEnabled: true, CompactTopmost: true);
        var configuration = BuiltInCyclePresets.ClassicId;
        var cycle = BuiltInCyclePresets.All[0].Configuration;
        var intention = new SessionIntention(Guid.NewGuid(), Guid.NewGuid(), "Work", 900,
            SessionMode.FocusCycle, configuration, cycle, true, new string('o', 300));

        var settings = data.Workflow().RememberAccepted(intention, current);

        Assert.Equal(current.Theme, settings.Theme);
        Assert.Equal(current.SessionEnd, settings.SessionEnd);
        Assert.True(settings.EnforcementEnabled);
        Assert.True(settings.ReduceMotion);
        Assert.True(settings.CompactTopmost);
        var loadedSettings = new DesktopSettingsStore(data.SettingsPath).Load();
        Assert.Equal(intention.ProfileId, loadedSettings.LastProfileId);
        Assert.Equal(configuration, loadedSettings.LastCyclePresetId);
        Assert.Equal(720, loadedSettings.LastDurationMinutes);
        var reloaded = data.Workflow().Get(intention.SessionId)!;
        Assert.Equal(cycle, reloaded.CycleConfiguration);
        Assert.Equal(240, reloaded.Objective.Length);
        Assert.Equal(720, reloaded.DurationMinutes);
        Assert.True(reloaded.Strict);
    }

    [Fact]
    public void SimpleAcceptedSessionClearsOnlyRememberedCyclePreset()
    {
        using var data = new UserData();
        var current = new DesktopSettings(LastSessionMode: SessionMode.FocusCycle, LastCyclePresetId: Guid.NewGuid());
        var intention = new SessionIntention(Guid.NewGuid(), Guid.NewGuid(), "Work", 25, SessionMode.SimpleTimer,
            null, null, false, "");
        var updated = data.Workflow().RememberAccepted(intention, current);
        Assert.Null(updated.LastCyclePresetId);
        Assert.Equal(SessionMode.SimpleTimer, updated.LastSessionMode);
        Assert.Equal(25, updated.LastDurationMinutes);
    }

    [Fact]
    public void ObservedStatusNeverOverwritesAnExistingIntentionOrChangesAcceptedPreferences()
    {
        using var data = new UserData();
        var workflow = data.Workflow();
        var profile = new StoredProfile(ProfileId.New(), "Work", [], []);
        var now = DateTimeOffset.UtcNow;
        var status = new StatusResponse(true, true, "Active", 60, Guid.NewGuid(), profile.Id.Value,
            now, now.AddMinutes(1), true, false);
        workflow.RecordObservedSession(status, profile, isActive: false);
        Assert.Null(workflow.Get(status.SessionId));
        workflow.RecordObservedSession(status, profile, isActive: true);
        var entry = workflow.Get(status.SessionId)!;
        workflow.SaveReflection(status.SessionId, "Keep this");
        workflow.RecordObservedSession(status with { EndUtc = now.AddDays(1) }, profile, isActive: true);
        Assert.Equal(entry.DurationMinutes, workflow.Get(status.SessionId)!.DurationMinutes);
        Assert.Equal("Keep this", workflow.Get(status.SessionId)!.Reflection);
        Assert.False(File.Exists(data.SettingsPath));
    }

    [Fact]
    public void ObservedScheduledSessionRecordsConfiguredDurationAndUpgradesUnknownConsent()
    {
        using var data = new UserData();
        var workflow = data.Workflow();
        var profile = new StoredProfile(ProfileId.New(), "Work", [], []);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var unknown = new SessionIntention(id, profile.Id.Value, profile.Name, 1,
            SessionMode.SimpleTimer, null, null, false, "Local note", "Reflection", EnforcementEnabled: null);
        new SessionJournalStore(data.JournalPath).Save(unknown);
        var scheduled = new ScheduledSessionIntent(profile.Name, 1, EnforcementEnabled: false);
        var status = new StatusResponse(true, true, "Active", 45, id, profile.Id.Value,
            now, now.AddSeconds(45), false, false, ScheduledIntent: scheduled);

        workflow.RecordObservedSession(status, profile, isActive: true);

        var persisted = new SessionJournalStore(data.JournalPath).GetLatest(id)!;
        Assert.Equal(1, persisted.DurationMinutes);
        Assert.False(persisted.EnforcementEnabled);
        Assert.Equal("Local note", persisted.Objective);
        Assert.Equal("Reflection", persisted.Reflection);
        Assert.False(File.Exists(data.SettingsPath));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StaleObserverCannotReplaceAnotherHostsAcceptedConsent(bool enforcementEnabled)
    {
        using var data = new UserData();
        var observer = data.Workflow();
        var profile = new StoredProfile(ProfileId.New(), "Work", [], []);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.Null(observer.Get(id)); // Classic cached the journal before React accepted.
        var accepted = new SessionIntention(id, profile.Id.Value, profile.Name, 2,
            SessionMode.SimpleTimer, null, null, false, "Accepted objective", EnforcementEnabled: enforcementEnabled);
        new SessionJournalStore(data.JournalPath).Save(accepted);
        observer.RecordObservedSession(new StatusResponse(true, true, "Active", 60, id,
            profile.Id.Value, now, now.AddMinutes(2), false, true), profile, isActive: true);
        var persisted = new SessionJournalStore(data.JournalPath).GetLatest(id)!;
        Assert.Equal(enforcementEnabled, persisted.EnforcementEnabled);
        Assert.Equal(accepted.Objective, persisted.Objective);
    }

    [Fact]
    public void OptionalCorruptJournalAndReflectionWithoutIntentionRemainNonAuthoritative()
    {
        using var data = new UserData();
        File.WriteAllText(data.JournalPath, "invalid");
        var workflow = data.Workflow();
        Assert.Null(workflow.Get(Guid.NewGuid()));
        var id = Guid.NewGuid();
        workflow.SaveReflection(id, "Notes");
        Assert.Equal(Guid.Empty, workflow.Get(id)!.ProfileId);
        Assert.Equal("Notes", data.Workflow().Get(id)!.Reflection);
        Assert.False(File.Exists(data.SettingsPath));
    }

    [Fact]
    public void SharedUserDataHasNoUiOrPlatformHostDependencies()
    {
        Assert.DoesNotContain(typeof(DesktopSettingsStore).Assembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.StartsWith("Avalonia", StringComparison.Ordinal)
            || assembly.Name is "FocusGuard.Desktop" or "FocusGuard.Service" or "FocusGuard.Infrastructure.Windows");
        Assert.Equal(typeof(SessionJournalStore).Assembly, typeof(ProfileWorkflow).Assembly);
    }

    private sealed class UserData : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg167-userdata-" + Guid.NewGuid());
        public string SettingsPath => Path.Combine(_directory, "settings.json");
        public string JournalPath => Path.Combine(_directory, "journal.json");
        public UserData() => Directory.CreateDirectory(_directory);
        public SessionJournalWorkflow Workflow() => new(new SessionJournalStore(JournalPath), new DesktopSettingsStore(SettingsPath));
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
