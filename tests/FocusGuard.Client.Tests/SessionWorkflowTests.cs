using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Client.Tests;

public sealed class SessionWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartResolvesSavedProfileAndPreservesRulesAndExplicitOptIn(bool enforcementEnabled)
    {
        using var profiles = new TemporaryProfiles();
        var profile = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(profile);
        var client = new RecordingClient();
        var workflow = new SessionWorkflow(client, profiles.Store);

        var response = await workflow.StartNormalAsync(
            new(profile.Id.Value, 1500, enforcementEnabled), CancellationToken.None);

        Assert.Same(client.ActivationResponse, response);
        var request = Assert.Single(client.Activations);
        Assert.Equal(profile.Id.Value, request.ProfileId);
        Assert.Equal(profile.Name, request.ProfileName);
        Assert.Equal(1500, request.DurationSeconds);
        Assert.Equal(enforcementEnabled, request.EnforcementEnabled);
        Assert.False(request.StrictMode);
        Assert.Equal(0, request.RecoveryDelaySeconds);
        Assert.Null(request.RecoveryCredential);
        Assert.Equal(profile.ApplicationRules.Count, request.ApplicationRules!.Count);
        foreach (var rule in profile.ApplicationRules)
        {
            var actual = Assert.Single(request.ApplicationRules, candidate => candidate.Id == rule.Id);
            Assert.Equal(rule.Action.ToString(), actual.Action);
            Assert.Equal(rule.MatchKind.ToString(), actual.MatchKind);
            Assert.Equal(rule.Value, actual.Value);
        }
        Assert.Equal(profile.WebsiteRules.Count, request.WebsiteRules!.Count);
        foreach (var rule in profile.WebsiteRules)
        {
            var actual = Assert.Single(request.WebsiteRules, candidate => candidate.Id == rule.Id);
            Assert.Equal(rule.Action.ToString(), actual.Action);
            Assert.Equal(rule.Pattern, actual.Pattern);
        }
    }

    [Fact]
    public async Task StartUsesCurrentSavedProfileButDoesNotMutateAnAcceptedSnapshot()
    {
        using var profiles = new TemporaryProfiles();
        var original = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(original);
        var client = new RecordingClient();
        var workflow = new SessionWorkflow(client, profiles.Store);
        var updated = original with { Name = "Updated", ApplicationRules = [], WebsiteRules = [] };
        await profiles.Store.SaveProfileAsync(updated);

        await workflow.StartNormalAsync(new(updated.Id.Value, 60), CancellationToken.None);
        await profiles.Store.SaveProfileAsync(original);

        var snapshot = Assert.Single(client.Activations);
        Assert.Equal("Updated", snapshot.ProfileName);
        Assert.Empty(snapshot.ApplicationRules!);
        Assert.Empty(snapshot.WebsiteRules!);
        Assert.False(snapshot.EnforcementEnabled);
    }

    [Fact]
    public async Task DeletedProfileIsRejectedWithoutSendingOrInventingRules()
    {
        using var profiles = new TemporaryProfiles();
        var profile = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(profile);
        await profiles.Store.DeleteProfileAsync(profile.Id);
        var client = new RecordingClient();

        var response = await new SessionWorkflow(client, profiles.Store).StartNormalAsync(
            new(profile.Id.Value, 60), CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Equal(SessionWorkflow.ProfileMissingReason, response.Reason);
        Assert.Null(response.SessionId);
        Assert.Empty(client.Activations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(2592001)]
    public async Task InvalidDurationIsRejectedBeforeStorageOrTransport(int seconds)
    {
        using var profiles = new TemporaryProfiles();
        var client = new RecordingClient();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new SessionWorkflow(client, profiles.Store).StartNormalAsync(
                new(Guid.NewGuid(), seconds), CancellationToken.None));

        Assert.False(File.Exists(profiles.DatabasePath));
        Assert.Empty(client.Activations);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(2592000)]
    public async Task ExistingBackendDurationBoundsArePreserved(int seconds)
    {
        using var profiles = new TemporaryProfiles();
        var profile = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(profile);
        var client = new RecordingClient();

        await new SessionWorkflow(client, profiles.Store).StartNormalAsync(
            new(profile.Id.Value, seconds), CancellationToken.None);

        Assert.Equal(seconds, Assert.Single(client.Activations).DurationSeconds);
    }

    [Fact]
    public async Task CancelledStartDoesNotOpenStorageOrSendActivation()
    {
        using var profiles = new TemporaryProfiles();
        var client = new RecordingClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new SessionWorkflow(client, profiles.Store).StartNormalAsync(
                new(Guid.NewGuid(), 60), cancellation.Token));

        Assert.False(File.Exists(profiles.DatabasePath));
        Assert.Empty(client.Activations);
    }

    [Fact]
    public async Task ActivationFailurePropagatesWithoutAutomaticRetry()
    {
        using var profiles = new TemporaryProfiles();
        var profile = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(profile);
        var failure = new TimeoutException("Response lost after sending activation.");
        var client = new RecordingClient { ActivationFailure = failure };
        using var cancellation = new CancellationTokenSource();

        var actual = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await new SessionWorkflow(client, profiles.Store).StartNormalAsync(
                new(profile.Id.Value, 60), cancellation.Token));

        Assert.Same(failure, actual);
        Assert.Single(client.Activations);
        Assert.Equal(cancellation.Token, client.LastActivationToken);
        Assert.Equal(0, client.StatusReads);
    }

    [Fact]
    public async Task StatusIsPassedThroughIncludingStrictCycleAndDegradedState()
    {
        using var profiles = new TemporaryProfiles();
        var end = DateTimeOffset.UtcNow.AddMinutes(10);
        var status = new StatusResponse(true, false, "Active", 600, Guid.NewGuid(), Guid.NewGuid(),
            end.AddMinutes(-25), end, true, false, SessionMode.FocusCycle,
            new PhaseStatus(1, SessionPhaseKind.ShortBreak, 1, 30, end.AddSeconds(-570)),
            FinalEndUtc: end);
        var client = new RecordingClient { Status = status };

        var response = await new SessionWorkflow(client, profiles.Store).GetStatusAsync(CancellationToken.None);

        Assert.Same(status, response);
        Assert.Equal(1, client.StatusReads);
        Assert.False(File.Exists(profiles.DatabasePath));
    }

    [Fact]
    public async Task FailedStatusReadPropagatesRatherThanFabricatingIdle()
    {
        using var profiles = new TemporaryProfiles();
        var failure = new IOException("Service unavailable.");
        var client = new RecordingClient { StatusFailure = failure };

        var actual = await Assert.ThrowsAsync<IOException>(async () =>
            await new SessionWorkflow(client, profiles.Store).GetStatusAsync(CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(1, client.StatusReads);
        Assert.Empty(client.Activations);
    }

    [Fact]
    public async Task SharedWorkflowUsesRealSqliteAndV2PipeAndRecoversStateWithANewClient()
    {
        using var profiles = new TemporaryProfiles();
        var profile = ProfileWithRules();
        await profiles.Store.SaveProfileAsync(profile);
        var pipeName = $"FocusGuard.Tests.ClientWorkflow.{Guid.NewGuid():N}";
        var service = new InMemoryFocusService();
        using var server = new FocusControlPipeService(service, pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await server.StartAsync(cancellation.Token);
        try
        {
            var workflow = new SessionWorkflow(new NamedPipeFocusControlClient(pipeName), profiles.Store);
            var activation = await workflow.StartNormalAsync(new(profile.Id.Value, 1500), cancellation.Token);
            Assert.True(activation.Accepted);
            var status = await workflow.GetStatusAsync(cancellation.Token);
            Assert.Equal("Active", status.SessionState);
            Assert.False(status.EnforcementConnected); // Timer-only test, no OS enforcement.
            Assert.Equal(profile.Id.Value, status.ProfileId);

            var reopened = new SessionWorkflow(new NamedPipeFocusControlClient(pipeName),
                new SqliteProfileStore(profiles.DatabasePath));
            var restored = await reopened.GetStatusAsync(cancellation.Token);
            Assert.Equal(activation.SessionId, restored.SessionId);
            Assert.Equal(activation.EndUtc, restored.EndUtc);
            var rejected = await reopened.StartNormalAsync(new(profile.Id.Value, 60), cancellation.Token);
            Assert.False(rejected.Accepted);
            Assert.Equal(activation.SessionId, rejected.SessionId);
            Assert.Equal(activation.EndUtc, (await reopened.GetStatusAsync(cancellation.Token)).EndUtc);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void SharedClientHasNoDesktopOrPlatformHostDependencies()
    {
        Assert.DoesNotContain(typeof(SessionWorkflow).Assembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.StartsWith("Avalonia", StringComparison.Ordinal)
            || assembly.Name is "FocusGuard.Desktop" or "FocusGuard.Service"
                or "FocusGuard.Infrastructure.Windows");
    }

    private static StoredProfile ProfileWithRules() => new(
        ProfileId.New(), "Deep work",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, @"C:\Apps\Chat.exe"),
            new(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "work.exe")],
        [new(Guid.NewGuid(), RuleAction.Block, "*.social.example"),
            new(Guid.NewGuid(), RuleAction.Allow, "social.example/work/*")]);

    private sealed class TemporaryProfiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FocusGuard.Client.{Guid.NewGuid():N}");
        public string DatabasePath => Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Store { get; }

        public TemporaryProfiles() => Store = new(DatabasePath);

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingClient : IFocusControlClient
    {
        public List<ActivateSessionRequest> Activations { get; } = [];
        public CancellationToken LastActivationToken { get; private set; }
        public int StatusReads { get; private set; }
        public Exception? ActivationFailure { get; init; }
        public Exception? StatusFailure { get; init; }
        public ActivateSessionResponse ActivationResponse { get; } = new(true, "Accepted", Guid.NewGuid(), null, null);
        public StatusResponse Status { get; init; } = new(true, false, "Inactive", 0, null, null, null, null, false, false);

        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token)
        {
            LastActivationToken = token;
            Activations.Add(request);
            if (ActivationFailure is not null) throw ActivationFailure;
            return ValueTask.FromResult(ActivationResponse);
        }

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StatusReads++;
            if (StatusFailure is not null) throw StatusFailure;
            return ValueTask.FromResult(Status);
        }

        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
