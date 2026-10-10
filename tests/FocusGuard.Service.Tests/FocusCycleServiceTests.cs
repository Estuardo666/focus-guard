using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class FocusCycleServiceTests
{
    [Theory]
    [InlineData(30, 0, 0)]
    [InlineData(60, 1, 0)]
    [InlineData(90, 1, 0)]
    [InlineData(120, 1, 1)]
    [InlineData(179, 1, 1)]
    [InlineData(180, 2, 1)]
    public async Task SessionSummaryPersistsOnlyFullyCompletedCyclePhases(int elapsedSeconds, int focusCycles, int breaks)
    {
        var directory = Directory.CreateTempSubdirectory("FocusGuard-cycle-history-");
        try
        {
            var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var clock = new TestTimeProvider(start);
            var history = new SqliteSessionHistoryStore(Path.Combine(directory.FullName, "history.db"));
            var service = new InMemoryFocusService(clock, historyStore: history);
            var prepared = await service.PrepareSessionAsync(CycleRequest(), CancellationToken.None);
            Assert.True(prepared.IsValid);
            await service.ActivatePreparedSessionAsync(
                new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
                CancellationToken.None);

            clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));
            var sessionId = (await service.GetStatusAsync(CancellationToken.None)).SessionId!.Value;
            if (elapsedSeconds >= 180)
                await service.GetStatusAsync(CancellationToken.None);
            else
                await service.StopNormalSessionAsync(CancellationToken.None);

            var summary = await service.GetSessionSummaryAsync(new(sessionId), CancellationToken.None);
            Assert.Equal(new CycleHistoryMetrics(focusCycles, breaks), summary.CycleMetrics);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PrepareAndActivate_ExposeAuthoritativeCurrentAndNextPhase()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var service = new InMemoryFocusService(clock);
        var prepared = await service.PrepareSessionAsync(CycleRequest(), CancellationToken.None);

        Assert.True(prepared.IsValid);
        Assert.Equal(3, prepared.Value!.Phases.Count);
        var activated = await service.ActivatePreparedSessionAsync(
            new ActivatePreparedSessionRequest(prepared.Value.PreparationId!.Value),
            CancellationToken.None);
        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(activated.Value!.Accepted);
        Assert.Equal(SessionMode.FocusCycle, status.Mode);
        Assert.Equal(SessionPhaseKind.Focus, status.CurrentPhase!.Kind);
        Assert.Equal(SessionPhaseKind.ShortBreak, status.NextPhase!.Kind);
        Assert.Equal(prepared.Value.EndUtc, status.FinalEndUtc);
    }

    [Fact]
    public async Task NormalSkipBreak_AdvancesEndAndRepeatedRequestIsIdempotent()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var service = new InMemoryFocusService(clock);
        var prepared = await service.PrepareSessionAsync(CycleRequest(), CancellationToken.None);
        await service.ActivatePreparedSessionAsync(
            new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
            CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(1));
        var first = await service.SkipBreakAsync(new SkipBreakRequest(1), CancellationToken.None);
        var repeated = await service.SkipBreakAsync(new SkipBreakRequest(1), CancellationToken.None);

        Assert.True(first.Value!.Accepted);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Equal(SessionPhaseKind.Focus, first.Value.CurrentPhase!.Kind);
        Assert.Equal(clock.GetUtcNow().AddMinutes(1), first.Value.SessionEndUtc);
    }

    [Fact]
    public async Task StrictCycle_RejectsSkippingABreak()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var strictStore = new RecordingStrictSessionStore();
        var service = new InMemoryFocusService(clock, strictSessionStore: strictStore);
        var generated = RecoveryCodeService.Generate();
        var request = CycleRequest() with
        {
            StrictMode = true,
            RecoveryDelaySeconds = 900
        };
        var prepared = await service.PrepareSessionAsync(request, CancellationToken.None);
        await service.ActivatePreparedSessionAsync(
            new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value, generated.Credential),
            CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(1));
        var skipped = await service.SkipBreakAsync(new SkipBreakRequest(1), CancellationToken.None);

        Assert.False(skipped.Value!.Accepted);
        Assert.Contains("Strict", skipped.Value.Reason, StringComparison.Ordinal);
        var sessionId = (await service.GetStatusAsync(CancellationToken.None)).SessionId!.Value;
        Assert.False((await service.StopReviewedAsync(new(sessionId), CancellationToken.None)).Accepted);
        Assert.False((await service.SkipReviewedAsync(new(sessionId, 1), CancellationToken.None)).Value!.Accepted);
    }

    // The recording store above accepts anything, which hid that the real store rejected Block-only break exceptions.
    [Fact]
    public async Task StrictCycle_WithBlockBreakExceptions_ActivatesThroughTheRealStrictStore()
    {
        var directory = Directory.CreateTempSubdirectory("FocusGuard-strict-break-");
        try
        {
            var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
            var service = new InMemoryFocusService(clock, strictSessionStore: new StrictSessionStore(directory.FullName, () => "boot-test"));
            var request = CycleRequest() with
            {
                StrictMode = true,
                RecoveryDelaySeconds = 900,
                BreakApplicationRules = [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "chat.exe")],
                BreakWebsiteRules = [new WebsitePolicyRule(Guid.NewGuid(), "Block", "social.example")],
            };
            var prepared = await service.PrepareSessionAsync(request, CancellationToken.None);
            Assert.True(prepared.IsValid, string.Join("; ", prepared.Errors));

            var activated = await service.ActivatePreparedSessionAsync(
                new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value, RecoveryCodeService.Generate().Credential),
                CancellationToken.None);

            Assert.True(activated.IsValid, string.Join("; ", activated.Errors));
            Assert.True(activated.Value!.Accepted, activated.Value.Reason);
            var status = await service.GetStatusAsync(CancellationToken.None);
            Assert.NotEqual("Failed", status.SessionState);
            Assert.True(status.StrictMode);
            Assert.NotEmpty(Directory.GetFiles(directory.FullName, "*", SearchOption.AllDirectories));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Preparation_ExpiresAfterTwoMinutesAndCannotBeActivated()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var service = new InMemoryFocusService(clock);
        var prepared = await service.PrepareSessionAsync(CycleRequest(), CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(2));
        var activated = await service.ActivatePreparedSessionAsync(
            new ActivatePreparedSessionRequest(prepared.Value!.PreparationId!.Value),
            CancellationToken.None);

        Assert.False(activated.Value!.Accepted);
        Assert.Equal("Inactive", (await service.GetStatusAsync(CancellationToken.None)).SessionState);
    }

    private static PrepareSessionRequest CycleRequest() =>
        new(
            Guid.NewGuid(),
            "Cycles",
            SessionMode.FocusCycle,
            0,
            new FocusCycleConfigurationContract(
                60,
                60,
                60,
                2,
                CycleTerminationMode.CycleCount,
                2,
                null),
            false,
            0,
            ApplicationRules: [],
            WebsiteRules: []);

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class RecordingStrictSessionStore : IStrictSessionStore
    {
        public ValueTask ActivateAsync(
            Session session,
            EnforcementPolicy policy,
            RecoveryCredential recoveryCredential,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<StrictPolicyLoadResult> LoadAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent"));
    }
}
