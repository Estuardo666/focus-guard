using FocusGuard.Client.Workflows;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class ProfileWorkflowTests
{
    [Fact]
    public async Task CreateRenameAndDeleteUseExistingSqliteWithoutChangingRuleIds()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var app = ApplicationRule.Create(RuleAction.Allow, ApplicationMatchKind.FileName, "work.exe").Value!;
        var site = WebsiteRule.Create(RuleAction.Block, "social.example").Value!;

        var created = await workflow.CreateAsync("  Writing  ", [app], [site], "#89B4FA");
        Assert.True(created.IsValid);
        var profile = created.Value!;
        Assert.Equal("Writing", profile.Name);
        var renamed = await workflow.RenameAsync(profile, "  Reading  ");
        Assert.True(renamed.IsValid);
        var persisted = (await new SqliteProfileStore(database.Path).LoadProfileAsync(profile.Id))!;
        Assert.Equal("Reading", persisted.Name);
        Assert.Equal(app, Assert.Single(persisted.ApplicationRules));
        Assert.Equal(site, Assert.Single(persisted.WebsiteRules));
        Assert.Equal("#89B4FA", persisted.AccentHex);
        Assert.True(await workflow.DeleteProfileAsync(profile.Id));
        Assert.Null(await workflow.LoadProfileAsync(profile.Id));
    }

    [Fact]
    public async Task DuplicateCopiesIdentityAndCycleSettingsWithFreshIdsAndIndependentStorage()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var original = new StoredProfile(ProfileId.New(), "Writing",
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
            [new(Guid.NewGuid(), RuleAction.Allow, "social.example/work/*")], "Description", "#89B4FA");
        await workflow.SaveProfileAsync(original);
        var settings = new ProfileCycleSettings(original.Id, BuiltInCyclePresets.ClassicId,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "video.exe")],
            [new(Guid.NewGuid(), RuleAction.Block, "music.example")]);
        await workflow.SaveProfileCycleSettingsAsync(settings);

        var duplicate = await workflow.DuplicateAsync(original, "Copy of {0}", " ({0})");
        var duplicateSettings = (await workflow.LoadProfileCycleSettingsAsync(duplicate.Id))!;

        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Copy of Writing", duplicate.Name);
        Assert.Equal(original.Description, duplicate.Description);
        Assert.Equal(original.AccentHex, duplicate.AccentHex);
        var copiedApp = Assert.Single(duplicate.ApplicationRules);
        Assert.NotEqual(original.ApplicationRules[0].Id, copiedApp.Id);
        Assert.Equal(original.ApplicationRules[0] with { Id = copiedApp.Id }, copiedApp);
        var copiedSite = Assert.Single(duplicate.WebsiteRules);
        Assert.NotEqual(original.WebsiteRules[0].Id, copiedSite.Id);
        Assert.Equal(original.WebsiteRules[0] with { Id = copiedSite.Id }, copiedSite);
        Assert.Equal(settings.DefaultPresetId, duplicateSettings.DefaultPresetId);
        Assert.Equal(duplicate.Id, duplicateSettings.ProfileId);
        var copiedBreak = Assert.Single(duplicateSettings.ApplicationRules);
        Assert.NotEqual(settings.ApplicationRules[0].Id, copiedBreak.Id);
        Assert.Equal(settings.ApplicationRules[0] with { Id = copiedBreak.Id }, copiedBreak);
        var copiedBreakSite = Assert.Single(duplicateSettings.WebsiteRules);
        Assert.NotEqual(settings.WebsiteRules[0].Id, copiedBreakSite.Id);
        Assert.Equal(settings.WebsiteRules[0] with { Id = copiedBreakSite.Id }, copiedBreakSite);
        await workflow.RemoveApplicationRuleAsync(duplicate, copiedApp.Id);
        Assert.Single((await workflow.LoadProfileAsync(original.Id))!.ApplicationRules);
        var sourceSettings = (await workflow.LoadProfileCycleSettingsAsync(original.Id))!;
        Assert.Equal(settings.ProfileId, sourceSettings.ProfileId);
        Assert.Equal(settings.DefaultPresetId, sourceSettings.DefaultPresetId);
        Assert.Equal(settings.ApplicationRules, sourceSettings.ApplicationRules);
        Assert.Equal(settings.WebsiteRules, sourceSettings.WebsiteRules);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.SaveProfileCycleSettingsAsync(
            sourceSettings with { WebsiteRules = [new(Guid.NewGuid(), RuleAction.Allow, "music.example")] }));
    }

    [Fact]
    public async Task DuplicateUsesBoundedLocalizedUniqueNames()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var original = new StoredProfile(ProfileId.New(), new string('A', 80), [], []);
        await workflow.SaveProfileAsync(original);

        var first = await workflow.DuplicateAsync(original, "Copia de {0}", " ({0})");
        var second = await workflow.DuplicateAsync(original, "Copia de {0}", " ({0})");

        Assert.Equal(80, first.Name.Length);
        Assert.Equal(80, second.Name.Length);
        Assert.StartsWith("Copia de ", first.Name);
        Assert.EndsWith(" (2)", second.Name);
        Assert.NotEqual(first.Name, second.Name);
        Assert.True(ProfileWorkflow.HasNameConflict(await workflow.ListProfilesAsync(), first.Name.ToLowerInvariant()));
        Assert.False(ProfileWorkflow.HasNameConflict([first], first.Name, first.Id));
    }

    [Fact]
    public async Task InvalidEditsDoNotWriteProfileOrInventRules()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var invalid = await workflow.CreateAsync(" ", [], [], null);
        Assert.False(invalid.IsValid);
        var created = (await workflow.CreateAsync("Writing", [], [], null)).Value!;
        Assert.False((await workflow.RenameAsync(created, new string('A', 81))).IsValid);
        Assert.False((await workflow.AddApplicationRuleAsync(created, RuleAction.Block, ApplicationMatchKind.FileName, "")).IsValid);
        Assert.False((await workflow.AddWebsiteRuleAsync(created, RuleAction.Block, "https://example.com/?secret=x")).IsValid);
        Assert.False(await workflow.RemoveApplicationRuleAsync(created, Guid.NewGuid()));
        var persisted = (await workflow.LoadProfileAsync(created.Id))!;
        Assert.Equal("Writing", persisted.Name);
        Assert.Empty(persisted.ApplicationRules);
        Assert.Empty(persisted.WebsiteRules);
    }

    [Fact]
    public async Task AddAndRemoveRulesPreserveActionAndNormalizedValue()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var profile = (await workflow.CreateAsync("Work", [], [], null)).Value!;
        var appResult = await workflow.AddApplicationRuleAsync(profile, RuleAction.Allow, ApplicationMatchKind.FileName, "work.exe");
        profile = appResult.Value!;
        var siteResult = await workflow.AddWebsiteRuleAsync(profile, RuleAction.Block, "Example.COM");
        profile = siteResult.Value!;
        Assert.Equal(RuleAction.Allow, profile.ApplicationRules[0].Action);
        Assert.Equal("example.com", profile.WebsiteRules[0].Pattern);
        var id = profile.WebsiteRules[0].Id;
        Assert.True(await workflow.RemoveWebsiteRuleAsync(profile, id));
        var saved = (await workflow.LoadProfileAsync(profile.Id))!;
        Assert.Empty(saved.WebsiteRules);
        Assert.Equal(profile.ApplicationRules[0], Assert.Single(saved.ApplicationRules));
    }

    [Fact]
    public async Task PresetBoundaryRetainsBuiltInProtectionAndCustomRoundTrip()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        var builtIns = await workflow.ListCyclePresetsAsync();
        Assert.Equal(3, builtIns.Count);
        Assert.False(await workflow.DeleteCyclePresetAsync(BuiltInCyclePresets.ClassicId));
        var original = (await workflow.LoadCyclePresetAsync(BuiltInCyclePresets.ClassicId))!;
        var attempted = original with { Name = "Changed", IsBuiltIn = false };
        await workflow.SaveCyclePresetAsync(attempted);
        Assert.Equal(original.Name, (await workflow.LoadCyclePresetAsync(original.Id))!.Name);
        var custom = new StoredCyclePreset(Guid.NewGuid(), "Writing", original.Configuration, false);
        await workflow.SaveCyclePresetAsync(custom);
        Assert.Equal(custom, await workflow.LoadCyclePresetAsync(custom.Id));
        Assert.True(await workflow.DeleteCyclePresetAsync(custom.Id));
        Assert.Null(await workflow.LoadCyclePresetAsync(custom.Id));
    }

    [Fact]
    public async Task CancellationDoesNotPartiallyCreateAProfile()
    {
        using var database = new TestProfiles();
        var workflow = new ProfileWorkflow(database.Store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            workflow.CreateAsync("Work", [], [], null, cancellation.Token));
        Assert.False(File.Exists(database.Path));
    }

    private sealed class TestProfiles : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg167-profiles-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Store { get; }
        public TestProfiles() => Store = new(Path);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
