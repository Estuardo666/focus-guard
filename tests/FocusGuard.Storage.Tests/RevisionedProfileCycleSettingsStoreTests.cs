using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Storage.Tests;

public sealed class RevisionedProfileCycleSettingsStoreTests
{
    [Fact]
    public async Task ConcurrentWriterConflictsWithoutReplacingTheWinningBreakRules()
    {
        using var data = new Database();
        var first = new SqliteProfileStore(data.Path);
        var second = new SqliteProfileStore(data.Path);
        var profile = new StoredProfile(ProfileId.New(), "Focus", [], []);
        await first.SaveProfileAsync(profile);

        var firstSnapshot = await first.LoadRevisionedProfileCycleSettingsAsync(profile.Id);
        var staleSnapshot = await second.LoadRevisionedProfileCycleSettingsAsync(profile.Id);
        Assert.NotNull(firstSnapshot);
        Assert.Equal(firstSnapshot!.Revision, staleSnapshot!.Revision);

        var applicationRule = new BreakApplicationRule(Guid.NewGuid(), RuleAction.Block,
            ApplicationMatchKind.FileName, "  game.exe  ");
        var saved = await first.TrySaveProfileCycleSettingsAsync(
            firstSnapshot.Settings with { ApplicationRules = [applicationRule] }, firstSnapshot.Revision);
        Assert.Equal(ProfileCycleSettingsMutationStatus.Saved, saved.Status);
        Assert.Equal("game.exe", Assert.Single(saved.Snapshot!.Settings.ApplicationRules).Value);

        var staleWrite = await second.TrySaveProfileCycleSettingsAsync(
            staleSnapshot.Settings with { WebsiteRules = [new(Guid.NewGuid(), RuleAction.Block, "games.example")] },
            staleSnapshot.Revision);
        Assert.Equal(ProfileCycleSettingsMutationStatus.Conflict, staleWrite.Status);

        var current = await second.LoadRevisionedProfileCycleSettingsAsync(profile.Id);
        Assert.Equal(saved.Snapshot.Revision, current!.Revision);
        Assert.Equal("game.exe", Assert.Single(current.Settings.ApplicationRules).Value);
        Assert.Empty(current.Settings.WebsiteRules);
    }

    [Fact]
    public async Task NewSettingsUseTheLoadedEmptyRevisionAndRejectMissingPresets()
    {
        using var data = new Database();
        var store = new SqliteProfileStore(data.Path);
        var profile = new StoredProfile(ProfileId.New(), "Focus", [], []);
        await store.SaveProfileAsync(profile);
        var initial = await store.LoadRevisionedProfileCycleSettingsAsync(profile.Id);
        Assert.NotNull(initial);
        Assert.Null(initial!.Settings.DefaultPresetId);

        var invalid = await store.TrySaveProfileCycleSettingsAsync(
            initial.Settings with { DefaultPresetId = Guid.NewGuid() }, initial.Revision);
        Assert.Equal(ProfileCycleSettingsMutationStatus.PresetMissing, invalid.Status);
        var unchanged = await store.LoadRevisionedProfileCycleSettingsAsync(profile.Id);
        Assert.Equal(initial.Revision, unchanged!.Revision);
    }

    [Fact]
    public async Task MissingProfileHasNoSettingsSnapshot()
    {
        using var data = new Database();
        var store = new SqliteProfileStore(data.Path);
        Assert.Null(await store.LoadRevisionedProfileCycleSettingsAsync(ProfileId.New()));
    }

    private sealed class Database : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "focusguard-cycle-revision-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "profiles.db");
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
