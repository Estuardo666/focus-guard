using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;

namespace FocusGuard.Service.Tests;

public sealed class StrictPolicyPersistenceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"focusguard-fg105-{Guid.NewGuid():N}");
    private readonly byte[] _key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task Coordinator_RecoversUnexpiredStrictPolicyAcrossInstanceRestart()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(30)), CancellationToken.None);

        var coordinator = new StrictPolicyPersistenceCoordinator(
            store,
            new ManualTimeProvider(now.AddMinutes(1)),
            () => "boot-after");
        var result = await coordinator.RecoverAsync(CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Active, result.Status);
        Assert.Equal("boot-before", coordinator.ActiveSnapshot!.BootId);
    }

    [Fact]
    public async Task Store_ExpiresPolicyWithoutDesktop()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(1)), CancellationToken.None);

        var result = await store.LoadAsync(now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Expired, result.Status);
        Assert.False(File.Exists(store.ActivePath));
    }

    [Fact]
    public async Task Store_FailsOpenAndQuarantinesCorruptPolicy()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);
        var json = await File.ReadAllTextAsync(store.ActivePath);
        await File.WriteAllTextAsync(store.ActivePath, json.Replace("blocked.exe", "allowed.exe"));

        var result = await store.LoadAsync(now, CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.FailedOpen, result.Status);
        Assert.Null(result.Snapshot);
        Assert.False(File.Exists(store.ActivePath));
        Assert.Single(Directory.GetFiles(_directory, "failed-open-*.json"));
    }

    [Fact]
    public async Task Coordinator_BackwardWallClockCannotExtendPolicy()
    {
        var now = Utc(12, 0);
        var clock = new ManualTimeProvider(now);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);
        var coordinator = new StrictPolicyPersistenceCoordinator(store, clock, () => "boot");
        await coordinator.RecoverAsync(CancellationToken.None);

        clock.SetUtcNow(now.AddHours(-1));
        clock.AdvanceTimestamp(TimeSpan.FromMinutes(10));
        var status = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Expired, status);
        Assert.False(File.Exists(store.ActivePath));
    }

    [Fact]
    public async Task Coordinator_ForwardWallClockExpiresPolicy()
    {
        var now = Utc(12, 0);
        var clock = new ManualTimeProvider(now);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);
        var coordinator = new StrictPolicyPersistenceCoordinator(store, clock, () => "boot");
        await coordinator.RecoverAsync(CancellationToken.None);

        clock.SetUtcNow(now.AddHours(1));
        var status = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Expired, status);
    }

    [Fact]
    public async Task Store_RejectsNormalOrOverlongPolicy()
    {
        var now = Utc(12, 0);
        var normal = Snapshot(now, now.AddMinutes(10)) with
        {
            Policy = Snapshot(now, now.AddMinutes(10)).Policy with { IsStrict = false }
        };
        var overlong = Snapshot(now, now.AddDays(31));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Store().SaveAsync(normal, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Store().SaveAsync(overlong, CancellationToken.None));
    }

    [Fact]
    public async Task Save_AtomicallyReplacesSnapshotAndLeavesNoTemporaryFile()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);
        var replacement = Snapshot(now, now.AddMinutes(20));

        await store.SaveAsync(replacement, CancellationToken.None);
        var result = await store.LoadAsync(now, CancellationToken.None);

        Assert.Equal(replacement.Policy.SessionId, result.Snapshot!.Policy.SessionId);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Store_AllowsConcurrentReadsDuringAtomicReplacement()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);

        var saves = Enumerable.Range(0, 100).Select(index =>
            store.SaveAsync(
                Snapshot(now.AddSeconds(index), now.AddMinutes(10).AddSeconds(index)),
                CancellationToken.None));
        var loads = Enumerable.Range(0, 100).Select(_ =>
            store.LoadAsync(now, CancellationToken.None));

        await Task.WhenAll(saves.Concat<Task>(loads));
        Assert.True(File.Exists(store.ActivePath));
        Assert.Empty(Directory.GetFiles(_directory, "failed-open-*.json"));
    }

    [Fact]
    public async Task Save_RetriesTransientWindowsReplacementLock()
    {
        var now = Utc(12, 0);
        var store = Store();
        await store.SaveAsync(Snapshot(now, now.AddMinutes(10)), CancellationToken.None);
        using var locked = new FileStream(
            store.ActivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var save = store.SaveAsync(
            Snapshot(now.AddSeconds(1), now.AddMinutes(11)),
            CancellationToken.None);
        await Task.Delay(75);
        locked.Dispose();
        await save;

        Assert.Equal(
            StrictPolicyLoadStatus.Active,
            (await store.LoadAsync(now, CancellationToken.None)).Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private StrictPolicySnapshotStore Store() => new(_key, _directory);

    private static StrictPolicySnapshot Snapshot(DateTimeOffset start, DateTimeOffset end)
    {
        var policy = new EnforcementPolicy(
            Guid.NewGuid(),
            new ProfileId(Guid.NewGuid()),
            start,
            end,
            true,
            [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "blocked.exe")],
            [new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "example.com")]);
        return new StrictPolicySnapshot(
            1,
            policy,
            start,
            "boot-before",
            RecoveryCodeService.Generate().Credential,
            TimeSpan.FromMinutes(15));
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2030, 1, 1, hour, minute, 0, TimeSpan.Zero);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long GetTimestamp() => _timestamp;

        public void SetUtcNow(DateTimeOffset value) => utcNow = value;

        public void AdvanceTimestamp(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
