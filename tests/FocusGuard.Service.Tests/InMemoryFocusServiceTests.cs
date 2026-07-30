using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service.Tests;

public sealed class InMemoryFocusServiceTests
{
    [Fact]
    public async Task StartsInactiveAndReady()
    {
        var service = new InMemoryFocusService();

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.ServiceReady);
        Assert.Equal("Inactive", status.SessionState);
        Assert.Equal(0, status.RemainingSeconds);
        Assert.False(status.EnforcementConnected);
    }

    [Fact]
    public async Task ActivateOwnsActiveSessionAndRejectsSecondActivation()
    {
        var service = new InMemoryFocusService();
        var request = NormalRequest(3600);

        var first = await service.ActivateAsync(request, CancellationToken.None);
        var second = await service.ActivateAsync(request, CancellationToken.None);
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(first.Value!.Accepted);
        Assert.False(second.Value!.Accepted);
        Assert.NotNull(first.Value.SessionId);
        Assert.Equal("Active", status.SessionState);
    }

    [Fact]
    public async Task StatusExpiresSessionAndAllowsNextActivation()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var service = new InMemoryFocusService(clock);
        var request = NormalRequest(60);
        await service.ActivateAsync(request, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(1));
        var status = await service.GetStatusAsync(CancellationToken.None);
        var next = await service.ActivateAsync(request, CancellationToken.None);

        Assert.Equal("Expired", status.SessionState);
        Assert.True(next.Value!.Accepted);
    }

    [Fact]
    public async Task NormalCanStopButStrictCannot()
    {
        var normal = new InMemoryFocusService();
        await normal.ActivateAsync(NormalRequest(60), CancellationToken.None);
        var normalStop = await normal.StopNormalSessionAsync(CancellationToken.None);

        var strictStore = new RecordingStrictSessionStore();
        var strict = new InMemoryFocusService(strictSessionStore: strictStore);
        var generated = RecoveryCodeService.Generate();
        await strict.ActivateAsync(
            new ActivateSessionRequest(Guid.NewGuid(), 60, true, 900, generated.Credential),
            CancellationToken.None);
        var strictStop = await strict.StopNormalSessionAsync(CancellationToken.None);
        var strictStatus = await strict.GetStatusAsync(CancellationToken.None);

        Assert.True(normalStop.Accepted);
        Assert.Equal("Stopped", (await normal.GetStatusAsync(CancellationToken.None)).SessionState);
        Assert.False(strictStop.Accepted);
        Assert.Equal("Active", strictStatus.SessionState);
        Assert.True(strictStore.Activated);
    }

    [Fact]
    public async Task InvalidActivationDoesNotChangeState()
    {
        var service = new InMemoryFocusService();

        var result = await service.ActivateAsync(
            new ActivateSessionRequest(Guid.Empty, 30, false, 0),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(
            "Inactive",
            (await service.GetStatusAsync(CancellationToken.None)).SessionState);
    }

    [Fact]
    public async Task ActivationCopiesRulesIntoImmutablePolicySnapshot()
    {
        var service = new InMemoryFocusService();
        var applicationRules = new[]
        {
            new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "chat.exe")
        };
        var request = new ActivateSessionRequest(
            Guid.NewGuid(),
            60,
            false,
            0,
            ApplicationRules: applicationRules,
            WebsiteRules: [new WebsitePolicyRule(Guid.NewGuid(), "Allow", "example.com")]);

        var result = await service.ActivateAsync(request, CancellationToken.None);
        applicationRules[0] = applicationRules[0] with { Value = "mutated.exe" };
        var policy = service.GetActivePolicySnapshot();

        Assert.True(result.Value!.Accepted);
        Assert.Equal("chat.exe", Assert.Single(policy!.ApplicationRules).Value);
        Assert.Equal("example.com", Assert.Single(policy.WebsiteRules).Pattern);
        Assert.False((policy.ApplicationRules as ApplicationRule[]) is not null);
    }

    private static ActivateSessionRequest NormalRequest(int durationSeconds) =>
        new(Guid.NewGuid(), durationSeconds, false, 0);

    private sealed class RecordingStrictSessionStore : IStrictSessionStore
    {
        private StrictPolicySnapshot? _snapshot;

        public bool Activated { get; private set; }

        public ValueTask ActivateAsync(
            Session session,
            EnforcementPolicy policy,
            RecoveryCredential recoveryCredential,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Activated = true;
            _snapshot = new StrictPolicySnapshot(
                StrictPolicySnapshotStore.CurrentSchemaVersion,
                policy,
                session.StartUtc,
                "test-boot",
                recoveryCredential,
                session.StrictMode.RecoveryDelay);
            return ValueTask.CompletedTask;
        }

        public ValueTask<StrictPolicyLoadResult> LoadAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot is null
                ? new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent")
                : new StrictPolicyLoadResult(StrictPolicyLoadStatus.Active, _snapshot, "Active"));
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
