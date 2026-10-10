using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class SavedNormalCycleWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPipeReviewUsesSavedPresetOrCustomTruncatedPlanWithoutActivation(bool custom)
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        var clock = new Clock(); var service = new InMemoryFocusService(clock);
        var pipe = "FocusGuard.Tests.Fg193." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe); using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Store);
            var config = FocusCycleConfiguration.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), 2,
                CycleTerminationMode.TotalDuration, totalDuration: TimeSpan.FromSeconds(150)).Value!;
            var review = await workflow.PrepareSavedNormalCycleAsync(custom
                ? new(profile.Id.Value, null, config) : new(profile.Id.Value, BuiltInCyclePresets.ClassicId, null), cancellation.Token);
            Assert.True(review.Preparation.Accepted); Assert.Equal(profile.Name, review.ProfileName);
            Assert.False(review.EnforcementEnabled); Assert.Equal(SessionMode.FocusCycle, review.Preparation.Mode);
            Assert.Equal(clock.GetUtcNow().AddMinutes(2), review.Preparation.ExpiresUtc);
            Assert.Equal("Inactive", (await workflow.GetStatusAsync(cancellation.Token)).SessionState);
            if (custom) { Assert.Null(review.PresetName); Assert.Equal(clock.GetUtcNow().AddSeconds(150), review.Preparation.EndUtc); Assert.Equal(30, (review.Preparation.Phases.Last().EndUtc - review.Preparation.Phases.Last().StartUtc).TotalSeconds); }
            else { Assert.Equal("Pomodoro clásico", review.PresetName); Assert.Equal(7, review.Preparation.Phases.Count); Assert.Equal(SessionPhaseKind.Focus, review.Preparation.Phases.Last().Kind); }
            await data.Store.SaveProfileAsync(profile with { Name = "After review", ApplicationRules = [], WebsiteRules = [] });
            clock.Advance(TimeSpan.FromSeconds(10));
            var activated = await workflow.ActivatePreparedAsync(new(review.Preparation.PreparationId!.Value), cancellation.Token);
            Assert.True(activated.Accepted); Assert.Equal(review.Preparation.EndUtc, activated.EndUtc);
            Assert.False((await workflow.ActivatePreparedAsync(new(review.Preparation.PreparationId.Value), cancellation.Token)).Accepted);
            var reopened = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Store);
            var status = await reopened.GetStatusAsync(cancellation.Token);
            Assert.Equal(activated.SessionId, status.SessionId); Assert.Equal(SessionMode.FocusCycle, status.Mode); Assert.False(status.StrictMode); Assert.NotNull(status.CurrentPhase);
            await reopened.StopNormalAsync(cancellation.Token);
            var expiring = await workflow.PrepareSavedNormalCycleAsync(new(profile.Id.Value, BuiltInCyclePresets.ClassicId, null), cancellation.Token);
            clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False((await workflow.ActivatePreparedAsync(new(expiring.Preparation.PreparationId!.Value), cancellation.Token)).Accepted);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task MissingProfileAndPresetNeverPrepare()
    {
        using var data = new Data(); var client = new Client(); var workflow = new SessionWorkflow(client, data.Store);
        Assert.Equal("ProfileMissing", (await workflow.PrepareSavedNormalCycleAsync(new(Guid.NewGuid(), BuiltInCyclePresets.ClassicId, null), CancellationToken.None)).Preparation.Reason);
        var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        Assert.Equal("PresetMissing", (await workflow.PrepareSavedNormalCycleAsync(new(profile.Id.Value, Guid.NewGuid(), null), CancellationToken.None)).Preparation.Reason);
        Assert.Equal(0, client.PrepareCalls);
    }

    [Fact]
    public async Task MalformedPlanAndLostResponseAreNotActivatedOrRetried()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        var client = new Client(); var workflow = new SessionWorkflow(client, data.Store);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await workflow.PrepareSavedNormalCycleAsync(new(profile.Id.Value, BuiltInCyclePresets.ClassicId, null), CancellationToken.None));
        Assert.Equal(1, client.PrepareCalls);
        client.Failure = new TimeoutException();
        await Assert.ThrowsAsync<TimeoutException>(async () => await workflow.PrepareSavedNormalCycleAsync(new(profile.Id.Value, BuiltInCyclePresets.ClassicId, null), CancellationToken.None));
        Assert.Equal(2, client.PrepareCalls);
    }

    [Fact]
    public async Task InvalidIdentityOrSelectionAndCancellationDoNotOpenStorage()
    {
        using var data = new Data(); var client = new Client(); var workflow = new SessionWorkflow(client, data.Store);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await workflow.PrepareSavedNormalCycleAsync(new(Guid.Empty, BuiltInCyclePresets.ClassicId, null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await workflow.PrepareSavedNormalCycleAsync(new(Guid.NewGuid(), null, null), CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await workflow.PrepareSavedNormalCycleAsync(new(Guid.NewGuid(), BuiltInCyclePresets.ClassicId, null), cancellation.Token));
        Assert.False(File.Exists(data.Path)); Assert.Equal(0, client.PrepareCalls);
    }
    private static StoredProfile Profile() => new(ProfileId.New(), "Reviewed",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")], [new(Guid.NewGuid(), RuleAction.Allow, "work.example")]);
    private sealed class Clock : TimeProvider { private DateTimeOffset _now = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan value) => _now += value; }
    private sealed class Data : IDisposable { private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg193-" + Guid.NewGuid()); public string Path => System.IO.Path.Combine(_directory, "profiles.db"); public SqliteProfileStore Store { get; } public Data() => Store = new(Path); public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } }
    private sealed class Client : IFocusControlClient
    {
        public int PrepareCalls { get; private set; }
        public Exception? Failure { get; set; }
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) { PrepareCalls++; if (Failure is not null) throw Failure; var now = DateTimeOffset.UtcNow; return ValueTask.FromResult(new PrepareSessionResponse(true, "Invalid test plan", Guid.NewGuid(), now, now.AddMinutes(25), now.AddMinutes(2), SessionMode.FocusCycle, [])); }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
