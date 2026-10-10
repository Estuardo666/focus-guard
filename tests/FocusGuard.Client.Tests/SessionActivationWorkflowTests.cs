using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class SessionActivationWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalTimerReviewResolvesSavedRulesAndExplicitOptInWithoutActivating(bool enforcement)
    {
        using var data = new TestData();
        var profile = Profile() with
        {
            WebsiteRules = [new(Guid.NewGuid(), RuleAction.Allow, "work.example")]
        };
        await data.Profiles.SaveProfileAsync(profile);
        var now = new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var client = new RecordingClient
        {
            Preparation = new(true, "Prepared", Guid.NewGuid(), now, now.AddMinutes(25),
                now.AddMinutes(2), SessionMode.SimpleTimer, [])
        };
        var review = await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
            new(profile.Id.Value, 1500, enforcement), CancellationToken.None);

        Assert.Same(client.Preparation, review.Preparation);
        Assert.Equal(profile.Name, review.ProfileName);
        Assert.Equal(1, review.ApplicationRuleCount);
        Assert.Equal(1, review.WebsiteRuleCount);
        Assert.Equal(1, review.BlockedApplicationCount);
        Assert.Equal(0, review.BlockedWebsiteCount);
        Assert.Equal(enforcement, review.EnforcementEnabled);
        var request = Assert.Single(client.Preparations);
        Assert.Equal(SessionMode.SimpleTimer, request.Mode);
        Assert.Equal(enforcement, request.EnforcementEnabled);
        Assert.False(request.StrictMode);
        Assert.Equal(0, request.RecoveryDelaySeconds);
        Assert.Null(request.CycleConfiguration);
        Assert.Null(request.BreakApplicationRules);
        Assert.Null(request.BreakWebsiteRules);
        Assert.Equal("chat.exe", Assert.Single(request.ApplicationRules!).Value);
        Assert.Equal("Allow", Assert.Single(request.WebsiteRules!).Action);
        Assert.Empty(client.TimerActivations);
        Assert.Empty(client.PreparedActivations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(43260)]
    public async Task NormalTimerReviewRejectsPilotBoundsBeforeStorageOrIpc(int seconds)
    {
        using var data = new TestData();
        var client = new RecordingClient();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
                new(Guid.NewGuid(), seconds), CancellationToken.None));
        Assert.Empty(client.Preparations);
        Assert.False(File.Exists(data.DatabasePath));
    }

    [Fact]
    public async Task MissingNormalTimerProfileDoesNotInventRulesOrPrepare()
    {
        using var data = new TestData();
        var client = new RecordingClient();
        var review = await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
            new(Guid.NewGuid(), 1500, true), CancellationToken.None);
        Assert.False(review.Preparation.Accepted);
        Assert.Equal(SessionWorkflow.ProfileMissingReason, review.Preparation.Reason);
        Assert.Null(review.ProfileName);
        Assert.Null(review.Preparation.PreparationId);
        Assert.Empty(client.Preparations);
    }

    [Fact]
    public async Task CancelledNormalTimerReviewDoesNotReadStorageOrSend()
    {
        using var data = new TestData();
        var client = new RecordingClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
                new(Guid.NewGuid(), 1500), cancellation.Token));
        Assert.False(File.Exists(data.DatabasePath));
        Assert.Empty(client.Preparations);
    }

    [Theory]
    [InlineData("missingId")]
    [InlineData("wrongMode")]
    [InlineData("wrongDuration")]
    [InlineData("longExpiry")]
    [InlineData("nonUtc")]
    [InlineData("rejectedMetadata")]
    public async Task InvalidNormalTimerPreparationCannotBeReviewed(string fault)
    {
        using var data = new TestData();
        var profile = Profile();
        await data.Profiles.SaveProfileAsync(profile);
        var now = new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var prepared = new PrepareSessionResponse(true, "Prepared", Guid.NewGuid(), now,
            now.AddMinutes(25), now.AddMinutes(2), SessionMode.SimpleTimer, []);
        prepared = fault switch
        {
            "missingId" => prepared with { PreparationId = Guid.Empty },
            "wrongMode" => prepared with { Mode = SessionMode.FocusCycle },
            "wrongDuration" => prepared with { EndUtc = now.AddMinutes(26) },
            "longExpiry" => prepared with { ExpiresUtc = now.AddMinutes(3) },
            "nonUtc" => prepared with { StartUtc = now.ToOffset(TimeSpan.FromHours(1)) },
            _ => prepared with { Accepted = false }
        };
        var client = new RecordingClient { Preparation = prepared };
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
                new(profile.Id.Value, 1500), CancellationToken.None));
        Assert.Single(client.Preparations);
        Assert.Empty(client.PreparedActivations);
    }

    [Fact]
    public async Task LostNormalTimerPreparationReplyIsNotRetriedOrActivated()
    {
        using var data = new TestData();
        var profile = Profile();
        await data.Profiles.SaveProfileAsync(profile);
        var failure = new TimeoutException("Lost preparation response");
        var client = new RecordingClient { PreparationFailure = failure };
        var actual = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await new SessionWorkflow(client, data.Profiles).PrepareNormalTimerAsync(
                new(profile.Id.Value, 1500), CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.Single(client.Preparations);
        Assert.Empty(client.PreparedActivations);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(43200)]
    public async Task NormalTimerReviewAcceptsPilotEndpointsUsingRealServiceDeadlines(int durationSeconds)
    {
        using var data = new TestData();
        var profile = Profile() with { ApplicationRules = [], WebsiteRules = [] };
        await data.Profiles.SaveProfileAsync(profile);
        var clock = new TestClock();
        var service = new InMemoryFocusService(clock);
        var pipe = "FocusGuard.Tests.Fg185.Bounds." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Profiles);
            var review = await workflow.PrepareNormalTimerAsync(
                new(profile.Id.Value, durationSeconds), cancellation.Token);
            Assert.True(review.Preparation.Accepted);
            Assert.False(review.EnforcementEnabled);
            Assert.Equal(0, review.ApplicationRuleCount);
            Assert.Equal(0, review.WebsiteRuleCount);
            Assert.Equal(clock.GetUtcNow().AddSeconds(durationSeconds), review.Preparation.EndUtc);
            Assert.Equal("Inactive", (await workflow.GetStatusAsync(cancellation.Token)).SessionState);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task NormalTimerReviewUsesServiceTtlExactEndAndSnapshotAcrossRealPipeReopen()
    {
        using var data = new TestData();
        var profile = Profile();
        await data.Profiles.SaveProfileAsync(profile);
        var clock = new TestClock();
        var target = new ReviewedPolicyTarget();
        var service = new InMemoryFocusService(clock,
            policyDistribution: new PolicyDistributionCoordinator(target, clock),
            historyStore: new SqliteSessionHistoryStore(Path.ChangeExtension(data.DatabasePath, "history.db")));
        var pipe = "FocusGuard.Tests.Fg185." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Profiles);
            var input = new StartNormalInput(profile.Id.Value, 1500, true);
            var expiredReview = await workflow.PrepareNormalTimerAsync(input, cancellation.Token);
            Assert.Equal(clock.GetUtcNow().AddMinutes(2), expiredReview.Preparation.ExpiresUtc);
            Assert.Equal("Inactive", (await workflow.GetStatusAsync(cancellation.Token)).SessionState);
            Assert.Null(target.Delivery);
            clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False((await workflow.ActivatePreparedAsync(
                new(expiredReview.Preparation.PreparationId!.Value), cancellation.Token)).Accepted);
            var review = await workflow.PrepareNormalTimerAsync(input, cancellation.Token);
            await data.Profiles.SaveProfileAsync(profile with
            { Name = "Edited after review", ApplicationRules = [], WebsiteRules = [] });
            clock.Advance(TimeSpan.FromSeconds(30));
            var activated = await workflow.ActivatePreparedAsync(
                new(review.Preparation.PreparationId!.Value), cancellation.Token);
            Assert.True(activated.Accepted);
            Assert.Equal(review.Preparation.StartUtc, activated.StartUtc);
            Assert.Equal(review.Preparation.EndUtc, activated.EndUtc);
            Assert.Single(target.Delivery!.Policy.ApplicationRules);
            Assert.Single(target.Delivery.Policy.WebsiteRules);
            Assert.False((await workflow.ActivatePreparedAsync(
                new(review.Preparation.PreparationId.Value), cancellation.Token)).Accepted);

            // Fresh client represents UI closure/reopen, not an installed-host proof.
            var reopened = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Profiles);
            var status = await reopened.GetStatusAsync(cancellation.Token);
            Assert.Equal(activated.SessionId, status.SessionId);
            Assert.Equal(activated.EndUtc, status.EndUtc);
            Assert.Equal(1470, status.RemainingSeconds);
            Assert.False(status.StrictMode);
            Assert.False(status.EnforcementConnected);
            Assert.False((await reopened.PrepareNormalTimerAsync(input, cancellation.Token)).Preparation.Accepted);
            Assert.True((await reopened.StopNormalAsync(cancellation.Token)).Accepted);
            var summary = await new NamedPipeFocusControlClient(pipe).GetSessionSummaryAsync(
                activated.SessionId, cancellation.Token);
            Assert.Equal(profile.Name, summary.ProfileName);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    private sealed class ReviewedPolicyTarget : IPolicyDistributionTarget
    {
        public PolicyDelivery? Delivery { get; private set; }
        public ValueTask<PolicyAcknowledgement> ReceiveAsync(PolicyDelivery delivery, CancellationToken token)
        { Delivery = delivery; return ValueTask.FromResult(new PolicyAcknowledgement(Guid.Empty, "Unconfirmed")); }
        public ValueTask ClearAsync(Guid policyId, CancellationToken token) => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationCopiesReviewedRulesAndConfigurationWithoutActivating(bool strict)
    {
        using var data = new TestData();
        var apps = new List<ApplicationRule> { new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe") };
        var profile = new StoredProfile(ProfileId.New(), "Reviewed", apps,
            [new(Guid.NewGuid(), RuleAction.Allow, "work.example")]);
        var breaks = new ProfileCycleSettings(profile.Id, BuiltInCyclePresets.ClassicId,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "video.exe")],
            [new(Guid.NewGuid(), RuleAction.Block, "social.example")]);
        var client = new RecordingClient();
        var workflow = new SessionWorkflow(client, data.Profiles);

        await workflow.PrepareCycleAsync(new(profile, Configuration(), breaks, strict, strict ? 900 : 0, true), CancellationToken.None);
        apps.Clear();

        var request = Assert.Single(client.Preparations);
        Assert.Equal("Reviewed", request.ProfileName);
        Assert.Equal(profile.Id.Value, request.ProfileId);
        Assert.Equal(strict, request.StrictMode);
        Assert.Equal(strict ? 900 : 0, request.RecoveryDelaySeconds);
        Assert.True(request.EnforcementEnabled);
        Assert.Equal(60, request.CycleConfiguration!.FocusSeconds);
        Assert.Equal(2, request.CycleConfiguration.CycleCount);
        Assert.Equal("chat.exe", Assert.Single(request.ApplicationRules!).Value);
        Assert.Equal("Allow", Assert.Single(request.WebsiteRules!).Action);
        Assert.Equal(breaks.ApplicationRules[0].Id, Assert.Single(request.BreakApplicationRules!).Id);
        Assert.Equal("Block", Assert.Single(request.BreakApplicationRules!).Action);
        Assert.Equal("Block", Assert.Single(request.BreakWebsiteRules!).Action);
        Assert.Empty(client.PreparedActivations);
        Assert.Empty(client.TimerActivations);
        Assert.False(File.Exists(data.DatabasePath));
    }

    [Fact]
    public async Task NormalCycleUsesExactlyOnePreparationAndItsIdWithoutCredential()
    {
        using var data = new TestData();
        var client = new RecordingClient();
        var workflow = new SessionWorkflow(client, data.Profiles);

        var result = await workflow.StartNormalCycleAsync(new(Profile(), Configuration()), CancellationToken.None);

        Assert.Same(client.Preparation, result.Preparation);
        Assert.Same(client.Activation, result.Activation);
        Assert.Single(client.Preparations);
        var activated = Assert.Single(client.PreparedActivations);
        Assert.Equal(client.Preparation.PreparationId, activated.PreparationId);
        Assert.Null(activated.RecoveryCredential);
    }

    [Fact]
    public async Task ExistingStoredBlockBreakRulesPassThroughPreparationUnchanged()
    {
        using var data = new TestData();
        var profile = Profile();
        await data.Profiles.SaveProfileAsync(profile);
        var settings = new ProfileCycleSettings(profile.Id, BuiltInCyclePresets.ClassicId,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "video.exe")], []);
        await data.Profiles.SaveProfileCycleSettingsAsync(settings);
        var persisted = await data.Profiles.LoadProfileCycleSettingsAsync(profile.Id);
        var client = new RecordingClient();

        await new SessionWorkflow(client, data.Profiles).PrepareCycleAsync(
            new(profile, Configuration(), persisted), CancellationToken.None);

        var request = Assert.Single(client.Preparations);
        Assert.Equal(RuleAction.Block, Assert.Single(persisted!.ApplicationRules).Action);
        Assert.Equal("Block", Assert.Single(request.BreakApplicationRules!).Action);
        Assert.Empty(client.PreparedActivations);
    }

    [Fact]
    public async Task RejectedPreparationDoesNotAttemptActivation()
    {
        using var data = new TestData();
        var client = new RecordingClient
        {
            Preparation = new(false, "Already active", null, null, null, null, SessionMode.FocusCycle, [])
        };
        var result = await new SessionWorkflow(client, data.Profiles).StartNormalCycleAsync(
            new(Profile(), Configuration()), CancellationToken.None);
        Assert.False(result.Preparation.Accepted);
        Assert.Null(result.Activation);
        Assert.Empty(client.PreparedActivations);
    }

    [Fact]
    public async Task OneStepCycleStartCannotActivateStrictWithoutSeparateReview()
    {
        using var data = new TestData();
        var client = new RecordingClient();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionWorkflow(client, data.Profiles).StartNormalCycleAsync(
                new(Profile(), Configuration(), StrictMode: true, RecoveryDelaySeconds: 900), CancellationToken.None));
        Assert.Empty(client.Preparations);
        Assert.Empty(client.PreparedActivations);
    }

    [Fact]
    public async Task StrictTimerUsesReviewedSnapshotAndOnlyHashedRecoveryCredential()
    {
        using var data = new TestData();
        var reviewed = Profile();
        await data.Profiles.SaveProfileAsync(reviewed with { Name = "Later edit", ApplicationRules = [], WebsiteRules = [] });
        var generated = RecoveryCodeService.Generate();
        var client = new RecordingClient();

        await new SessionWorkflow(client, data.Profiles).StartStrictTimerAsync(
            new(reviewed, 1500, 900, generated.Credential, true), CancellationToken.None);

        var request = Assert.Single(client.TimerActivations);
        Assert.Equal(reviewed.Name, request.ProfileName);
        Assert.True(request.StrictMode);
        Assert.True(request.EnforcementEnabled);
        Assert.Equal(900, request.RecoveryDelaySeconds);
        Assert.Single(request.ApplicationRules!);
        Assert.Same(generated.Credential, request.RecoveryCredential);
        Assert.True(RecoveryCodeService.Validate(generated.DisplayCode, request.RecoveryCredential));
        var envelope = IpcProtocol.CreateActivateSessionRequest(Guid.NewGuid(), request).Value!;
        Assert.DoesNotContain(generated.DisplayCode, envelope.PayloadJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrMissingStrictCredentialNeverSendsActivation(bool malformed)
    {
        using var data = new TestData();
        var client = new RecordingClient();
        RecoveryCredential? credential = malformed ? new(99, "Invalid", 1, "", "") : null;
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionWorkflow(client, data.Profiles).StartStrictTimerAsync(
                new(Profile(), 1500, 900, credential), CancellationToken.None));
        Assert.Empty(client.TimerActivations);
    }

    [Fact]
    public async Task LostPreparedActivationResponsePropagatesWithoutRetryOrRepreparation()
    {
        using var data = new TestData();
        var failure = new TimeoutException("Activation response was lost.");
        var client = new RecordingClient { ActivationFailure = failure };
        var actual = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await new SessionWorkflow(client, data.Profiles).StartNormalCycleAsync(
                new(Profile(), Configuration()), CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.Single(client.Preparations);
        Assert.Single(client.PreparedActivations);
    }

    [Fact]
    public async Task CancelledPreparationDoesNotSendOrTouchStorage()
    {
        using var data = new TestData();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new RecordingClient();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new SessionWorkflow(client, data.Profiles).PrepareCycleAsync(
                new(Profile(), Configuration()), cancellation.Token));
        Assert.Empty(client.Preparations);
        Assert.False(File.Exists(data.DatabasePath));
    }

    [Fact]
    public async Task RealPipeKeepsPreparationTtlAndNormalSkipUnderServiceAuthority()
    {
        using var data = new TestData();
        var clock = new TestClock();
        var service = new InMemoryFocusService(clock);
        var pipe = "FocusGuard.Tests.Fg168." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Profiles);
            var input = new CycleSessionInput(Profile(), Configuration());
            var prepared = await workflow.PrepareCycleAsync(input, cancellation.Token);
            Assert.Equal(clock.GetUtcNow().AddMinutes(2), prepared.ExpiresUtc);
            Assert.Equal("Inactive", (await workflow.GetStatusAsync(cancellation.Token)).SessionState);
            clock.Advance(TimeSpan.FromMinutes(2));
            var expired = await workflow.ActivatePreparedAsync(new(prepared.PreparationId!.Value), cancellation.Token);
            Assert.False(expired.Accepted);
            Assert.Equal("Inactive", (await workflow.GetStatusAsync(cancellation.Token)).SessionState);

            var started = await workflow.StartNormalCycleAsync(input, cancellation.Token);
            Assert.True(started.Activation!.Accepted);
            clock.Advance(TimeSpan.FromMinutes(1));
            var skipped = await workflow.SkipBreakAsync(1, cancellation.Token);
            Assert.True(skipped.Accepted);
            Assert.Equal(SessionPhaseKind.Focus, skipped.CurrentPhase!.Kind);
            Assert.Equal(clock.GetUtcNow().AddMinutes(1), skipped.SessionEndUtc);
            Assert.True((await workflow.StopNormalAsync(cancellation.Token)).Accepted);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task RealStrictPipeUsesOriginalPreparedRulesAndDeniesStopAndSkip()
    {
        using var data = new TestData();
        var clock = new TestClock();
        var strictStore = new TestStrictStore();
        var service = new InMemoryFocusService(clock, strictSessionStore: strictStore);
        var pipe = "FocusGuard.Tests.Fg168.Strict." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            var reviewed = Profile();
            await data.Profiles.SaveProfileAsync(reviewed);
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipe), data.Profiles);
            var prepared = await workflow.PrepareCycleAsync(
                new(reviewed, Configuration(), StrictMode: true, RecoveryDelaySeconds: 900), cancellation.Token);
            Assert.Null(strictStore.Snapshot);
            await data.Profiles.SaveProfileAsync(reviewed with { ApplicationRules = [], WebsiteRules = [] });
            var generated = RecoveryCodeService.Generate();
            var activated = await workflow.ActivatePreparedAsync(new(prepared.PreparationId!.Value, generated.Credential), cancellation.Token);
            Assert.True(activated.Accepted);
            Assert.Equal(prepared.EndUtc, activated.EndUtc);
            Assert.Single(strictStore.Snapshot!.Policy.ApplicationRules);
            Assert.Single(strictStore.Snapshot.Policy.WebsiteRules);
            Assert.Equal(generated.Credential, strictStore.Snapshot.RecoveryCredential);
            Assert.False((await workflow.ActivatePreparedAsync(new(prepared.PreparationId.Value, generated.Credential), cancellation.Token)).Accepted);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.False((await workflow.SkipBreakAsync(1, cancellation.Token)).Accepted);
            Assert.False((await workflow.StopNormalAsync(cancellation.Token)).Accepted);
            var status = await workflow.GetStatusAsync(cancellation.Token);
            Assert.True(status.StrictMode);
            Assert.Equal(activated.SessionId, status.SessionId);
            Assert.Equal(activated.EndUtc, status.EndUtc);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    private static StoredProfile Profile() => new(ProfileId.New(), "Reviewed",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
        [new(Guid.NewGuid(), RuleAction.Block, "social.example")]);

    private static FocusCycleConfiguration Configuration() => FocusCycleConfiguration.Create(
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 2,
        CycleTerminationMode.CycleCount, cycleCount: 2).Value!;

    private sealed class TestData : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg168-" + Guid.NewGuid());
        public string DatabasePath => Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Profiles { get; }
        public TestData() => Profiles = new(DatabasePath);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    // In-process persistence fixture only; never used by the product or claims of installed recovery.
    private sealed class TestStrictStore : IStrictSessionStore
    {
        public StrictPolicySnapshot? Snapshot { get; private set; }
        public ValueTask ActivateAsync(Session session, EnforcementPolicy policy, RecoveryCredential credential, CancellationToken token)
        {
            Snapshot = new(StrictPolicySnapshotStore.CurrentSchemaVersion, policy, session.StartUtc, "test", credential, session.StrictMode.RecoveryDelay);
            return ValueTask.CompletedTask;
        }
        public ValueTask<StrictPolicyLoadResult> LoadAsync(DateTimeOffset now, CancellationToken token) =>
            ValueTask.FromResult(Snapshot is null ? new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent")
                : new StrictPolicyLoadResult(StrictPolicyLoadStatus.Active, Snapshot, "Active"));
    }

    private sealed class RecordingClient : IFocusControlClient
    {
        public List<PrepareSessionRequest> Preparations { get; } = [];
        public List<ActivatePreparedSessionRequest> PreparedActivations { get; } = [];
        public List<ActivateSessionRequest> TimerActivations { get; } = [];
        public Exception? ActivationFailure { get; init; }
        public Exception? PreparationFailure { get; init; }
        public PrepareSessionResponse Preparation { get; init; } = new(true, "Prepared", Guid.NewGuid(), null, null, null, SessionMode.FocusCycle, []);
        public ActivatePreparedSessionResponse Activation { get; } = new(true, "Activated", Guid.NewGuid(), null, null, SessionMode.FocusCycle);
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token)
        { Preparations.Add(request); if (PreparationFailure is not null) throw PreparationFailure; return ValueTask.FromResult(Preparation); }
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token)
        { PreparedActivations.Add(request); if (ActivationFailure is not null) throw ActivationFailure; return ValueTask.FromResult(Activation); }
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token)
        { TimerActivations.Add(request); return ValueTask.FromResult(new ActivateSessionResponse(true, "Activated", Guid.NewGuid(), null, null)); }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
