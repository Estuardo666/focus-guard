using FocusGuard.Client.Workflows;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class ProfileCoexistenceTests
{
    [Theory]
    [InlineData("rename")]
    [InlineData("color")]
    [InlineData("addApp")]
    [InlineData("addSite")]
    [InlineData("removeApp")]
    [InlineData("removeSite")]
    [InlineData("copy")]
    [InlineData("delete")]
    public async Task ClassicStaleSnapshotCannotOverwriteOrDeleteReactEdits(string operation)
    {
        using var data = new Data(); var original = Profile(); await data.Store.SaveProfileAsync(original);
        await data.Store.SaveProfileAsync(Profile() with { Name = "Other profile" });
        var classic = new ProfileWorkflow(new SqliteProfileStore(data.Path));
        var stale = (await classic.LoadProfileAsync(original.Id))!;
        var react = new ProfileEditorWorkflow(new SqliteProfileStore(data.Path));
        var changed = await react.RenameAsync(original.Id, ProfileRevision.Compute(original), "React edit");
        Assert.Equal(ProfileMutationStatus.Saved, changed.Status);
        async Task Edit()
        {
            switch (operation)
            {
                case "rename": await classic.RenameAsync(stale, "Lost update"); break;
                case "color": await classic.SetAccentAsync(stale, "#AABBCC"); break;
                case "addApp": await classic.AddApplicationRuleAsync(stale, RuleAction.Allow, ApplicationMatchKind.FileName, "new.exe"); break;
                case "addSite": await classic.AddWebsiteRuleAsync(stale, RuleAction.Block, "new.example"); break;
                case "removeApp": await classic.RemoveApplicationRuleAsync(stale, stale.ApplicationRules[0].Id); break;
                case "removeSite": await classic.RemoveWebsiteRuleAsync(stale, stale.WebsiteRules[0].Id); break;
                case "copy": await classic.DuplicateAsync(stale, "Copy of {0}", " ({0})"); break;
                case "delete": await classic.DeleteReviewedAsync(stale); break;
            }
        }
        var failure = await Assert.ThrowsAsync<ProfileWriteConflictException>(Edit);
        Assert.Equal(ProfileMutationStatus.Conflict, failure.Status);
        Assert.Equal(ProfileRevision.Compute(changed.Profile!), ProfileRevision.Compute((await data.Store.LoadProfileAsync(original.Id))!));
        Assert.Equal(2, (await data.Store.ListProfilesAsync()).Count);
    }

    [Fact]
    public async Task ReactStaleRevisionCannotOverwriteClassicEditsAndFreshReloadCanContinue()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        var classic = new ProfileWorkflow(new SqliteProfileStore(data.Path)); var react = new ProfileEditorWorkflow(new SqliteProfileStore(data.Path));
        var changed = await classic.AddWebsiteRuleAsync(profile, RuleAction.Block, "added.example");
        Assert.True(changed.IsValid);
        Assert.Equal(ProfileMutationStatus.Conflict, (await react.SetAccentAsync(profile.Id, ProfileRevision.Compute(profile), "#AABBCC")).Status);
        var fresh = (await react.RenameAsync(profile.Id, ProfileRevision.Compute(changed.Value!), "After reload")).Profile!;
        Assert.Equal(2, fresh.WebsiteRules.Count); Assert.Single(fresh.ApplicationRules); Assert.Equal("After reload", fresh.Name);
    }

    [Fact]
    public async Task ClassicCopyUsesLocalizedFormatAndAtomicSettingsWithIndependentIds()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        await data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, BuiltInCyclePresets.ClassicId,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "break.exe")], []));
        var classic = new ProfileWorkflow(data.Store);
        var copied = await classic.DuplicateAsync(profile, "Copia de {0}", " ({0})");
        var second = await classic.DuplicateAsync(profile, "Copia de {0}", " ({0})");
        Assert.Equal("Copia de Work", copied.Name); Assert.Equal("Copia de Work (2)", second.Name);
        var copiedSettings = (await data.Store.LoadProfileCycleSettingsAsync(copied.Id))!;
        var originalSettings = (await data.Store.LoadProfileCycleSettingsAsync(profile.Id))!;
        Assert.Equal(originalSettings.DefaultPresetId, copiedSettings.DefaultPresetId);
        Assert.NotEqual(originalSettings.ApplicationRules[0].Id, copiedSettings.ApplicationRules[0].Id);
        Assert.NotEqual(profile.ApplicationRules[0].Id, copied.ApplicationRules[0].Id);
    }

    [Fact]
    public async Task ClassicReviewedDeleteRejectsLastProfileAndCreateChecksConcurrentNameConflict()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        var classic = new ProfileWorkflow(data.Store);
        var last = await Assert.ThrowsAsync<ProfileWriteConflictException>(() => classic.DeleteReviewedAsync(profile));
        Assert.Equal(ProfileMutationStatus.LastProfile, last.Status);
        var duplicate = await Assert.ThrowsAsync<ProfileWriteConflictException>(() => classic.CreateAsync("work", [], [], null));
        Assert.Equal(ProfileMutationStatus.NameConflict, duplicate.Status);
        Assert.Single(await data.Store.ListProfilesAsync());
    }
    private static StoredProfile Profile() => new(ProfileId.New(), "Work",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "work.exe")],
        [new(Guid.NewGuid(), RuleAction.Allow, "work.example")], "Description", "#89B4FA");
    private sealed class Data : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg191-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "profiles.db"); public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path); public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
