using FocusGuard.Domain;

namespace FocusGuard.Storage.Tests;

public sealed class RevisionedCyclePresetStoreTests
{
    [Fact]
    public async Task IndependentReadersCannotOverwriteWinnerAndStaleDeletionCannotRemoveIt()
    {
        using var data = new Data(); var first = data.Store; var second = new SqliteProfileStore(data.Path);
        var preset = Preset(); Assert.Equal(CyclePresetMutationStatus.Saved, (await first.TrySaveCyclePresetAsync(preset, null)).Status);
        var snapshot = (await second.LoadCyclePresetAsync(preset.Id))!; var revision = CyclePresetRevision.Compute(snapshot);
        var winner = preset with { Name = "Winner", Configuration = preset.Configuration with { FocusDuration = TimeSpan.FromMinutes(2) } };
        Assert.Equal(CyclePresetMutationStatus.Saved, (await first.TrySaveCyclePresetAsync(winner, revision)).Status);
        Assert.Equal(CyclePresetMutationStatus.Conflict, (await second.TrySaveCyclePresetAsync(snapshot with { Name = "Stale" }, revision)).Status);
        Assert.Equal(CyclePresetMutationStatus.Conflict, (await second.TryDeleteCyclePresetAsync(preset.Id, revision)).Status);
        Assert.Equal(winner, await second.LoadCyclePresetAsync(preset.Id));
        Assert.Equal(CyclePresetMutationStatus.Saved, (await second.TryDeleteCyclePresetAsync(preset.Id, CyclePresetRevision.Compute(winner))).Status);
        Assert.Equal(CyclePresetMutationStatus.Missing, (await first.TrySaveCyclePresetAsync(snapshot, revision)).Status);
    }

    [Fact]
    public async Task BuiltinsCannotChangeOrDeleteButCanBeCopiedAsCustom()
    {
        using var data = new Data(); var original = (await data.Store.LoadCyclePresetAsync(BuiltInCyclePresets.ClassicId))!;
        var revision = CyclePresetRevision.Compute(original);
        Assert.Equal(CyclePresetMutationStatus.BuiltIn, (await data.Store.TrySaveCyclePresetAsync(original with { Name = "Tampered", IsBuiltIn = false }, revision)).Status);
        Assert.Equal(CyclePresetMutationStatus.BuiltIn, (await data.Store.TryDeleteCyclePresetAsync(original.Id, revision)).Status);
        var copy = await data.Store.TryDuplicateCyclePresetAsync(original.Id, revision, "My classic");
        Assert.Equal(CyclePresetMutationStatus.Saved, copy.Status); Assert.False(copy.Preset!.IsBuiltIn);
        Assert.NotEqual(original.Id, copy.Preset.Id); Assert.Equal(original.Configuration, copy.Preset.Configuration);
        Assert.Equal(original, await data.Store.LoadCyclePresetAsync(original.Id));
    }

    [Fact]
    public async Task ConcurrentWritersWithSameRevisionHaveExactlyOneWinner()
    {
        using var data = new Data(); var preset = Preset(); await data.Store.TrySaveCyclePresetAsync(preset, null);
        var revision = CyclePresetRevision.Compute(preset); var other = new SqliteProfileStore(data.Path);
        var results = await Task.WhenAll(data.Store.TrySaveCyclePresetAsync(preset with { Name = "Window A" }, revision),
            other.TrySaveCyclePresetAsync(preset with { Name = "Window B" }, revision));
        Assert.Single(results, result => result.Status == CyclePresetMutationStatus.Saved);
        Assert.Single(results, result => result.Status == CyclePresetMutationStatus.Conflict);
        Assert.Equal(results.Single(result => result.Status == CyclePresetMutationStatus.Saved).Preset, await other.LoadCyclePresetAsync(preset.Id));
    }

    [Fact]
    public async Task ReferencedDeletionPreservesPresetAndProfileDefault()
    {
        using var data = new Data(); var preset = Preset(); await data.Store.TrySaveCyclePresetAsync(preset, null);
        var profile = new StoredProfile(ProfileId.New(), "Uses preset", [], []); await data.Store.SaveProfileAsync(profile);
        await data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, preset.Id, [], []));
        var deleted = await data.Store.TryDeleteCyclePresetAsync(preset.Id, CyclePresetRevision.Compute(preset));
        Assert.Equal(CyclePresetMutationStatus.Referenced, deleted.Status);
        Assert.Equal(preset.Id, (await data.Store.LoadProfileCycleSettingsAsync(profile.Id))!.DefaultPresetId);
        Assert.Equal(preset, await data.Store.LoadCyclePresetAsync(preset.Id));
    }

    [Fact]
    public async Task DuplicateNamesAndCreationCollisionsNeverReplaceExistingPreset()
    {
        using var data = new Data(); var preset = Preset(); await data.Store.TrySaveCyclePresetAsync(preset, null);
        Assert.Equal(CyclePresetMutationStatus.Conflict, (await data.Store.TrySaveCyclePresetAsync(preset with { Name = "Other" }, null)).Status);
        Assert.Equal(CyclePresetMutationStatus.NameConflict, (await data.Store.TrySaveCyclePresetAsync(preset with { Id = Guid.NewGuid(), Name = "  custom  " }, null)).Status);
        Assert.Equal(CyclePresetMutationStatus.NameConflict, (await data.Store.TryDuplicateCyclePresetAsync(preset.Id, CyclePresetRevision.Compute(preset), "CUSTOM")).Status);
        Assert.Equal(preset, await data.Store.LoadCyclePresetAsync(preset.Id));
    }

    [Fact]
    public async Task InvalidFractionalOrExcessivePlanAndCancelledWritesDoNotCreatePresets()
    {
        using var data = new Data(); var preset = Preset();
        var fractional = preset with { Configuration = preset.Configuration with { FocusDuration = TimeSpan.FromSeconds(60.5) } };
        Assert.Equal(CyclePresetMutationStatus.InvalidInput, (await data.Store.TrySaveCyclePresetAsync(fractional, null)).Status);
        var excessive = preset with { Configuration = preset.Configuration with { TerminationMode = CycleTerminationMode.TotalDuration, CycleCount = null, TotalDuration = TimeSpan.FromDays(30) } };
        Assert.Equal(CyclePresetMutationStatus.InvalidInput, (await data.Store.TrySaveCyclePresetAsync(excessive, null)).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => data.Store.TrySaveCyclePresetAsync(preset, null, cancellation.Token));
        Assert.False(File.Exists(data.Path));
    }

    private static StoredCyclePreset Preset() => new(Guid.NewGuid(), "Custom", FocusCycleConfiguration.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 2, CycleTerminationMode.CycleCount, 2).Value!, false);
    private sealed class Data : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg200-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
