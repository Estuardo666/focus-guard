using FocusGuard.Client.Workflows;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class ProfileEditorWorkflowTests
{
    [Fact]
    public async Task NormalizedCreateRenameAccentAndRulesPreserveUnrelatedFields()
    {
        using var data = new Data();
        var editor = new ProfileEditorWorkflow(data.Store);
        var created = await editor.CreateAsync("  Work  ", "#aabbcc");
        Assert.Equal(ProfileMutationStatus.Saved, created.Status);
        var profile = created.Profile!;
        Assert.Equal("Work", profile.Name);
        Assert.Equal("#AABBCC", profile.AccentHex);
        var app = await editor.AddApplicationAsync(profile.Id, ProfileRevision.Compute(profile), RuleAction.Allow,
            ApplicationMatchKind.FileName, " work.exe ");
        profile = app.Profile!;
        var site = await editor.AddWebsiteAsync(profile.Id, ProfileRevision.Compute(profile), RuleAction.Block, " *.Example.COM/* ");
        profile = site.Profile!;
        Assert.Equal("*.example.com/*", Assert.Single(profile.WebsiteRules).Pattern);
        var renamed = await editor.RenameAsync(profile.Id, ProfileRevision.Compute(profile), " Reading ");
        profile = renamed.Profile!;
        Assert.Equal("Reading", profile.Name);
        Assert.Equal(RuleAction.Allow, Assert.Single(profile.ApplicationRules).Action);
        Assert.Equal("work.exe", profile.ApplicationRules[0].Value);
        var colour = await editor.SetAccentAsync(profile.Id, ProfileRevision.Compute(profile), null);
        profile = colour.Profile!;
        Assert.Null(profile.AccentHex);
        var removed = await editor.RemoveWebsiteAsync(profile.Id, ProfileRevision.Compute(profile), profile.WebsiteRules[0].Id);
        profile = removed.Profile!;
        Assert.Empty(profile.WebsiteRules);
        Assert.Single(profile.ApplicationRules);
        Assert.Equal(ProfileMutationStatus.RuleMissing, (await editor.RemoveApplicationAsync(profile.Id, ProfileRevision.Compute(profile), Guid.NewGuid())).Status);
        Assert.Equal(ProfileRevision.Compute(profile), ProfileRevision.Compute((await data.Store.LoadProfileAsync(profile.Id))!));
    }

    [Theory]
    [InlineData(ApplicationMatchKind.ExactPath, RuleAction.Allow)]
    [InlineData(ApplicationMatchKind.ExactPath, RuleAction.Block)]
    [InlineData(ApplicationMatchKind.Folder, RuleAction.Allow)]
    [InlineData(ApplicationMatchKind.Folder, RuleAction.Block)]
    [InlineData(ApplicationMatchKind.Sha256, RuleAction.Allow)]
    [InlineData(ApplicationMatchKind.Sha256, RuleAction.Block)]
    [InlineData(ApplicationMatchKind.FileName, RuleAction.Allow)]
    [InlineData(ApplicationMatchKind.FileName, RuleAction.Block)]
    public async Task AllExistingApplicationMatchingModesAndActionsRoundTrip(ApplicationMatchKind kind, RuleAction action)
    {
        using var data = new Data();
        var editor = new ProfileEditorWorkflow(data.Store);
        var profile = (await editor.CreateAsync("Work", null)).Profile!;
        var value = kind == ApplicationMatchKind.Sha256 ? new string('A', 64) : kind == ApplicationMatchKind.FileName ? "work.exe" : @"C:\Apps\Work";
        var result = await editor.AddApplicationAsync(profile.Id, ProfileRevision.Compute(profile), action, kind, value);
        Assert.Equal(ProfileMutationStatus.Saved, result.Status);
        var rule = Assert.Single((await data.Store.LoadProfileAsync(profile.Id))!.ApplicationRules);
        Assert.Equal(kind, rule.MatchKind); Assert.Equal(action, rule.Action); Assert.Equal(value, rule.Value);
    }

    [Fact]
    public async Task StaleFrontendRevisionCannotApplyAnEditToAReplacedSnapshot()
    {
        using var data = new Data();
        var editor = new ProfileEditorWorkflow(data.Store);
        var original = (await editor.CreateAsync("Work", null)).Profile!;
        await data.Store.SaveProfileAsync(original with { Description = "Another window", Name = "Newer" });
        var result = await editor.AddWebsiteAsync(original.Id, ProfileRevision.Compute(original), RuleAction.Block, "chat.example");
        Assert.Equal(ProfileMutationStatus.Conflict, result.Status);
        var persisted = (await data.Store.LoadProfileAsync(original.Id))!;
        Assert.Equal("Newer", persisted.Name); Assert.Equal("Another window", persisted.Description); Assert.Empty(persisted.WebsiteRules);
    }

    [Fact]
    public async Task InvalidInputsLeaveOriginalProfileUnchanged()
    {
        using var data = new Data();
        var editor = new ProfileEditorWorkflow(data.Store);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.CreateAsync("", null)).Status);
        var original = (await editor.CreateAsync("Work", null)).Profile!;
        var revision = ProfileRevision.Compute(original);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.RenameAsync(original.Id, revision, new string('x', 81))).Status);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.SetAccentAsync(original.Id, revision, "red")).Status);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.AddWebsiteAsync(original.Id, revision, RuleAction.Allow, "example.com?secret=1")).Status);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.AddApplicationAsync(original.Id, revision, (RuleAction)99, ApplicationMatchKind.FileName, "work.exe")).Status);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await editor.DuplicateAsync(original.Id, revision, "arbitrary format")).Status);
        Assert.Equal(revision, ProfileRevision.Compute((await data.Store.LoadProfileAsync(original.Id))!));
    }

    [Fact]
    public async Task DuplicateIsLocalizedAndIndependentThenDeletionRetainsAnotherProfile()
    {
        using var data = new Data();
        var editor = new ProfileEditorWorkflow(data.Store);
        var original = (await editor.CreateAsync("Work", null)).Profile!;
        Assert.Equal(ProfileMutationStatus.LastProfile, (await editor.DeleteAsync(original.Id, ProfileRevision.Compute(original))).Status);
        var copy = (await editor.DuplicateAsync(original.Id, ProfileRevision.Compute(original), "es")).Profile!;
        Assert.Equal("Copia de Work", copy.Name);
        Assert.Equal(ProfileMutationStatus.Saved, (await editor.DeleteAsync(copy.Id, ProfileRevision.Compute(copy))).Status);
        Assert.NotNull(await data.Store.LoadProfileAsync(original.Id));
    }

    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg187-client-" + Guid.NewGuid());
        public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path.Combine(_directory, "profiles.db"));
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
