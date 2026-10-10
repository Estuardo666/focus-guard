using FocusGuard.Domain;
using FocusGuard.Storage;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage.Tests;

public sealed class RevisionedProfileStoreTests
{
    [Fact]
    public void RevisionCanonicalizesStorageNormalizationAndRuleOrderButDetectsContentChanges()
    {
        var profile = Profile() with { Name = " Work ", Description = " Description ", AccentHex = "#aabbcc" };
        var extra = new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "work.exe");
        profile = profile with { ApplicationRules = [.. profile.ApplicationRules, extra] };
        var canonical = ProfileRevision.Canonical(profile);
        Assert.Equal(ProfileRevision.Compute(profile), ProfileRevision.Compute(canonical with { ApplicationRules = canonical.ApplicationRules.Reverse().ToArray() }));
        Assert.NotEqual(ProfileRevision.Compute(profile), ProfileRevision.Compute(profile with { Description = "Changed" }));
        Assert.NotEqual(ProfileRevision.Compute(profile), ProfileRevision.Compute(profile with { WebsiteRules = [] }));
    }

    [Fact]
    public async Task StaleSnapshotCannotOverwriteLegacyEditsEvenWhenUpdatedUtcDoesNotAdvance()
    {
        using var data = new Data();
        var clock = new FixedClock();
        var first = new SqliteProfileStore(data.Path, clock);
        var second = new SqliteProfileStore(data.Path, clock);
        var original = Profile();
        await first.SaveProfileAsync(original);
        var snapshot = (await first.LoadProfileAsync(original.Id))!;
        var newer = original with { Name = "Other window", Description = "Latest", ApplicationRules = [], AccentHex = "#112233" };
        await second.SaveProfileAsync(newer);
        var result = await first.TrySaveProfileAsync(snapshot with { Name = "Stale edit" }, ProfileRevision.Compute(snapshot));
        Assert.Equal(ProfileMutationStatus.Conflict, result.Status);
        var persisted = (await second.LoadProfileAsync(original.Id))!;
        Assert.Equal(ProfileRevision.Compute(newer), ProfileRevision.Compute(persisted));
    }

    [Fact]
    public async Task ConcurrentIndependentConnectionsHaveOneWinnerAndNoMixedFields()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        var revision = ProfileRevision.Compute(original);
        using var barrier = new Barrier(2);
        Task<ProfileMutationResult> Write(string name) => Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            return await new SqliteProfileStore(data.Path).TrySaveProfileAsync(
                original with { Name = name, Description = name, WebsiteRules = [] }, revision);
        });
        var results = await Task.WhenAll(Write("First writer"), Write("Second writer"));
        var saved = Assert.Single(results, result => result.Status == ProfileMutationStatus.Saved);
        Assert.Single(results, result => result.Status == ProfileMutationStatus.Conflict);
        var persisted = (await data.Store.LoadProfileAsync(original.Id))!;
        Assert.Equal(ProfileRevision.Compute(saved.Profile!), ProfileRevision.Compute(persisted));
        Assert.Equal(persisted.Name, persisted.Description);
    }

    [Fact]
    public async Task FailedRuleInsertRollsBackMetadataAndAllPreviousRules()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        var rule = new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "new.exe");
        await Assert.ThrowsAsync<SqliteException>(() => data.Store.TrySaveProfileAsync(
            original with { Name = "Should rollback", ApplicationRules = [rule, rule], WebsiteRules = [] }, ProfileRevision.Compute(original)));
        Assert.Equal(ProfileRevision.Compute(original), ProfileRevision.Compute((await data.Store.LoadProfileAsync(original.Id))!));
    }

    [Fact]
    public async Task NameConflictIsCheckedInsideTransactionAndExistingRowsAreNotUpsertedByCreate()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        Assert.Equal(ProfileMutationStatus.NameConflict, (await data.Store.TrySaveProfileAsync(Profile() with { Name = "work" }, null)).Status);
        Assert.Equal(ProfileMutationStatus.Conflict, (await data.Store.TrySaveProfileAsync(original with { Name = "Replacement" }, null)).Status);
        Assert.Single(await data.Store.ListProfilesAsync());
    }

    [Fact]
    public async Task DeletedSnapshotCannotBeRecreatedAndLastProfileCannotBeDeleted()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        var revision = ProfileRevision.Compute(original);
        Assert.Equal(ProfileMutationStatus.LastProfile, (await data.Store.TryDeleteProfileAsync(original.Id, revision)).Status);
        await data.Store.SaveProfileAsync(Profile() with { Name = "Remaining" });
        Assert.Equal(ProfileMutationStatus.Saved, (await data.Store.TryDeleteProfileAsync(original.Id, revision)).Status);
        Assert.Equal(ProfileMutationStatus.ProfileMissing, (await data.Store.TrySaveProfileAsync(original, revision)).Status);
        Assert.Null(await data.Store.LoadProfileAsync(original.Id));
    }

    [Fact]
    public async Task DeleteRejectsAChangedProfileInsteadOfDeletingUnreviewedRules()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        await data.Store.SaveProfileAsync(Profile() with { Name = "Remaining" });
        await data.Store.SaveProfileAsync(original with { WebsiteRules = [] });
        Assert.Equal(ProfileMutationStatus.Conflict, (await data.Store.TryDeleteProfileAsync(original.Id, ProfileRevision.Compute(original))).Status);
        Assert.NotNull(await data.Store.LoadProfileAsync(original.Id));
    }

    [Fact]
    public async Task DuplicateAtomicallyCopiesMetadataAndCycleSettingsWithFreshIndependentIds()
    {
        using var data = new Data();
        var original = Profile() with { Description = "Description", AccentHex = "#89B4FA" };
        await data.Store.SaveProfileAsync(original);
        await data.Store.SaveProfileCycleSettingsAsync(new(original.Id, BuiltInCyclePresets.ClassicId,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "break.exe")],
            [new(Guid.NewGuid(), RuleAction.Block, "break.example")]));
        var settings = (await data.Store.LoadProfileCycleSettingsAsync(original.Id))!;
        var result = await data.Store.TryDuplicateProfileAsync(original.Id, ProfileRevision.Compute(original), "Copy of");
        Assert.Equal(ProfileMutationStatus.Saved, result.Status);
        var duplicate = result.Profile!;
        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Copy of Work", duplicate.Name);
        Assert.Equal(original.Description, duplicate.Description);
        Assert.Equal(original.AccentHex, duplicate.AccentHex);
        var app = Assert.Single(duplicate.ApplicationRules);
        Assert.NotEqual(original.ApplicationRules[0].Id, app.Id);
        Assert.Equal(original.ApplicationRules[0] with { Id = app.Id }, app);
        var website = Assert.Single(duplicate.WebsiteRules);
        Assert.NotEqual(original.WebsiteRules[0].Id, website.Id);
        Assert.Equal(original.WebsiteRules[0] with { Id = website.Id }, website);
        var copiedSettings = (await data.Store.LoadProfileCycleSettingsAsync(duplicate.Id))!;
        Assert.Equal(settings.DefaultPresetId, copiedSettings.DefaultPresetId);
        Assert.NotEqual(settings.ApplicationRules[0].Id, copiedSettings.ApplicationRules[0].Id);
        Assert.NotEqual(settings.WebsiteRules[0].Id, copiedSettings.WebsiteRules[0].Id);
        Assert.Equal(settings.ApplicationRules[0] with { Id = copiedSettings.ApplicationRules[0].Id }, copiedSettings.ApplicationRules[0]);
        Assert.Equal(settings.WebsiteRules[0] with { Id = copiedSettings.WebsiteRules[0].Id }, copiedSettings.WebsiteRules[0]);
        Assert.Equal("Copy of Work (2)", (await data.Store.TryDuplicateProfileAsync(original.Id, ProfileRevision.Compute(original), "Copy of")).Profile!.Name);
        Assert.Equal(ProfileMutationStatus.Saved, (await data.Store.TryDeleteProfileAsync(duplicate.Id, ProfileRevision.Compute(duplicate))).Status);
        Assert.NotNull(await data.Store.LoadProfileCycleSettingsAsync(original.Id));
        Assert.Null(await data.Store.LoadProfileCycleSettingsAsync(duplicate.Id));
        Assert.Single((await data.Store.LoadProfileAsync(original.Id))!.ApplicationRules);
    }

    [Fact]
    public async Task DuplicateFailureAfterProfileInsertionRollsBackWholeCopy()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        await data.Store.SaveProfileCycleSettingsAsync(new(original.Id, BuiltInCyclePresets.ClassicId, [],
            [new(Guid.NewGuid(), RuleAction.Block, "break.example")]));
        await using (var connection = new SqliteConnection($"Data Source={data.Path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Trusted test-generated GUID literal; no input is interpolated into product SQL.
            command.CommandText = $"CREATE TRIGGER fail_copy BEFORE INSERT ON BreakWebsiteRules WHEN NEW.ProfileId <> '{original.Id.Value:D}' BEGIN SELECT RAISE(ABORT, 'isolated test failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => data.Store.TryDuplicateProfileAsync(original.Id, ProfileRevision.Compute(original), "Copy of"));
        Assert.Single(await data.Store.ListProfilesAsync());
        Assert.Equal(ProfileRevision.Compute(original), ProfileRevision.Compute((await data.Store.LoadProfileAsync(original.Id))!));
        Assert.Single((await data.Store.LoadProfileCycleSettingsAsync(original.Id))!.WebsiteRules);
    }

    [Fact]
    public async Task CancelledWritesDoNotChangeAnExistingDatabase()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => data.Store.TrySaveProfileAsync(original with { Name = "Cancelled" }, ProfileRevision.Compute(original), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => data.Store.TryDuplicateProfileAsync(original.Id, ProfileRevision.Compute(original), "Copy of", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => data.Store.TryDeleteProfileAsync(original.Id, ProfileRevision.Compute(original), cancellation.Token));
        Assert.Single(await data.Store.ListProfilesAsync());
        Assert.Equal(ProfileRevision.Compute(original), ProfileRevision.Compute((await data.Store.LoadProfileAsync(original.Id))!));
    }

    [Fact]
    public async Task InvalidRevisionAndStaleDuplicateReturnNoMutation()
    {
        using var data = new Data();
        var original = Profile();
        await data.Store.SaveProfileAsync(original);
        Assert.Equal(ProfileMutationStatus.InvalidInput, (await data.Store.TrySaveProfileAsync(original, "not a revision")).Status);
        await data.Store.SaveProfileAsync(original with { Name = "Changed" });
        Assert.Equal(ProfileMutationStatus.Conflict, (await data.Store.TryDuplicateProfileAsync(original.Id, ProfileRevision.Compute(original), "Copy of")).Status);
        Assert.Single(await data.Store.ListProfilesAsync());
    }

    private static StoredProfile Profile() => new(ProfileId.New(), "Work",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, @"C:\Apps\chat.exe")],
        [new(Guid.NewGuid(), RuleAction.Allow, "work.example/*")]);
    private sealed class FixedClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Data : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg187-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
