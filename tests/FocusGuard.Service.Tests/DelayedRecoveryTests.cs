using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;

namespace FocusGuard.Service.Tests;

public sealed class DelayedRecoveryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"focusguard-fg106-{Guid.NewGuid():N}");
    private readonly byte[] _key = Enumerable.Range(33, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void Generate_ProducesUniqueHighEntropyCodesAndOnlyHashValidates()
    {
        var first = RecoveryCodeService.Generate();
        var second = RecoveryCodeService.Generate();

        Assert.NotEqual(first.DisplayCode, second.DisplayCode);
        Assert.Equal(47, first.DisplayCode.Length);
        Assert.DoesNotContain(first.DisplayCode.Replace("-", string.Empty), first.Credential.Hash);
        Assert.True(RecoveryCodeService.Validate(first.DisplayCode, first.Credential));
        Assert.True(RecoveryCodeService.Validate(first.DisplayCode.ToLowerInvariant(), first.Credential));
        Assert.False(RecoveryCodeService.Validate(second.DisplayCode, first.Credential));
        Assert.False(RecoveryCodeService.Validate("not-a-code", first.Credential));
    }

    [Fact]
    public async Task WrongCode_DoesNotCreateRequest()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        var store = RequestStore();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => store.RequestAsync(snapshot, new string('0', 40), Utc(12, 1), CancellationToken.None));

        Assert.False(File.Exists(store.RequestPath));
    }

    [Fact]
    public async Task Request_UsesDefaultFifteenMinuteDelayAndIsIdempotent()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        var store = RequestStore();

        var first = await store.RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 1),
            CancellationToken.None);
        var second = await store.RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 2),
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(15), first.EligibleUtc - first.RequestedUtc);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Coordinator_DoesNotReleaseBeforeDelay()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        var policyStore = PolicyStore();
        var requestStore = RequestStore();
        await policyStore.SaveAsync(snapshot, CancellationToken.None);
        await requestStore.RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 1),
            CancellationToken.None);
        var clock = new ManualTimeProvider(Utc(12, 15));
        var coordinator = new StrictPolicyPersistenceCoordinator(
            policyStore,
            clock,
            recoveryStore: requestStore);
        await coordinator.RecoverAsync(CancellationToken.None);

        var status = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Active, status);
        Assert.True(File.Exists(policyStore.ActivePath));
    }

    [Fact]
    public async Task Coordinator_ReleasesAfterDelayAndWritesSanitizedAudit()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        var policyStore = PolicyStore();
        var requestStore = RequestStore();
        await policyStore.SaveAsync(snapshot, CancellationToken.None);
        await requestStore.RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 1),
            CancellationToken.None);
        var clock = new ManualTimeProvider(Utc(12, 16));
        var coordinator = new StrictPolicyPersistenceCoordinator(
            policyStore,
            clock,
            recoveryStore: requestStore,
            auditLog: new RecoveryAuditLog(_directory));
        await coordinator.RecoverAsync(CancellationToken.None);

        var status = await coordinator.ReconcileAsync(CancellationToken.None);

        Assert.Equal(StrictPolicyLoadStatus.Recovered, status);
        Assert.False(File.Exists(policyStore.ActivePath));
        Assert.False(File.Exists(requestStore.RequestPath));
        var audit = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "audit"), "*.json"));
        var text = await File.ReadAllTextAsync(audit);
        Assert.Contains("RecoveryReleased", text, StringComparison.Ordinal);
        Assert.DoesNotContain(generated.DisplayCode, text, StringComparison.Ordinal);
        Assert.DoesNotContain(generated.Credential.Hash, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticatedRequest_SurvivesCoordinatorRestart()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        await PolicyStore().SaveAsync(snapshot, CancellationToken.None);
        await RequestStore().RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 1),
            CancellationToken.None);

        var restarted = new StrictPolicyPersistenceCoordinator(
            PolicyStore(),
            new ManualTimeProvider(Utc(12, 16)),
            recoveryStore: RequestStore());
        await restarted.RecoverAsync(CancellationToken.None);

        Assert.Equal(
            StrictPolicyLoadStatus.Recovered,
            await restarted.ReconcileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TamperedRequest_DoesNotReleasePolicy()
    {
        var generated = RecoveryCodeService.Generate();
        var snapshot = Snapshot(generated.Credential);
        var policyStore = PolicyStore();
        var requestStore = RequestStore();
        await policyStore.SaveAsync(snapshot, CancellationToken.None);
        await requestStore.RequestAsync(
            snapshot,
            generated.DisplayCode,
            Utc(12, 1),
            CancellationToken.None);
        var json = await File.ReadAllTextAsync(requestStore.RequestPath);
        await File.WriteAllTextAsync(
            requestStore.RequestPath,
            json.Replace("12:16:00", "12:01:00", StringComparison.Ordinal));
        var coordinator = new StrictPolicyPersistenceCoordinator(
            policyStore,
            new ManualTimeProvider(Utc(12, 20)),
            recoveryStore: requestStore);
        await coordinator.RecoverAsync(CancellationToken.None);

        Assert.Equal(
            StrictPolicyLoadStatus.Active,
            await coordinator.ReconcileAsync(CancellationToken.None));
        Assert.True(File.Exists(policyStore.ActivePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private StrictPolicySnapshotStore PolicyStore() => new(_key, _directory);

    private DelayedRecoveryRequestStore RequestStore() => new(_key, _directory);

    private static StrictPolicySnapshot Snapshot(RecoveryCredential credential)
    {
        var start = Utc(12, 0);
        var policy = new EnforcementPolicy(
            Guid.NewGuid(),
            new ProfileId(Guid.NewGuid()),
            start,
            start.AddHours(1),
            true,
            Array.Empty<ApplicationRule>(),
            Array.Empty<WebsiteRule>());
        return new StrictPolicySnapshot(
            1,
            policy,
            start,
            "boot",
            credential,
            TimeSpan.FromMinutes(15));
    }

    private static DateTimeOffset Utc(int hour, int minute) =>
        new(2030, 1, 1, hour, minute, 0, TimeSpan.Zero);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long GetTimestamp() => 0;
    }
}
