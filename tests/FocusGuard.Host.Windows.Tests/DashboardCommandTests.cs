using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class DashboardCommandTests
{
    [Theory]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":1500,\"enforcementEnabled\":false,\"strictMode\":true}")]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":1500,\"enforcementEnabled\":false,\"rules\":[]}")]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":1500,\"durationSeconds\":1500,\"enforcementEnabled\":false}")]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":1501,\"enforcementEnabled\":false}")]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":43260,\"enforcementEnabled\":false}")]
    [InlineData("session.prepareNormal", "{\"profileId\":\"00000000000000000000000000000001\",\"durationSeconds\":1500,\"enforcementEnabled\":\"true\"}")]
    [InlineData("profiles.list", "{\"offset\":true}")]
    [InlineData("profiles.list", "{\"offset\":-1}")]
    [InlineData("profiles.list", "{\"offset\":0,\"path\":\"C:/other.db\"}")]
    [InlineData("session.startNormal", "{\"preparationId\":\"00000000000000000000000000000001\"}")]
    [InlineData("session.stop", "{}")]
    public void CommandsRejectUnknownDuplicateOrUnsafeParameters(string method, string parameters)
    {
        var json = Request(Guid.NewGuid().ToString("N"), method, JsonSerializer.Deserialize<JsonElement>(parameters));
        Assert.False(new BridgeRequestParser().TryParse(json, out _, out _));
    }

    [Fact]
    public void SessionObjectiveIsRequiredAndBoundedOnAllStartConfirmations()
    {
        var parser = new BridgeRequestParser();
        var token = Guid.NewGuid().ToString("N");
        var preparationId = Guid.NewGuid().ToString("N");
        var operationId = Guid.NewGuid().ToString("N");
        var tooLong = new string('o', 241);
        foreach (var (method, parameters) in new (string Method, object Parameters)[]
        {
            ("session.startNormal", new { preparationId, operationId, objective = tooLong }),
            ("session.startCycle", new { preparationId, operationId, objective = tooLong }),
            ("session.strict.activate", new { preparationId, operationId, typedConfirmation = "Focus", recoverySaved = true,
                unsavedWorkConfirmed = true, confirmed = true, objective = tooLong }),
            ("session.startNormal", new { preparationId, operationId }),
        })
        {
            var json = Request(token, method, parameters);
            Assert.False(parser.TryParse(json, out _, out _));
        }

        var maximumObjective = new string('o', 240);
        Assert.True(parser.TryParse(Request(token, "session.startNormal",
            new { preparationId, operationId, objective = maximumObjective }), out _, out _));
        Assert.True(parser.TryParse(Request(token, "session.startCycle",
            new { preparationId, operationId, objective = maximumObjective }), out _, out _));
        Assert.True(parser.TryParse(Request(token, "session.strict.activate", new { preparationId, operationId,
            typedConfirmation = "Focus", recoverySaved = true, unsavedWorkConfirmed = true, confirmed = true,
            objective = maximumObjective }), out _, out _));
    }

    [Fact]
    public void DefaultVerifiedHostRemainsReadOnlyAndPilotPermitsOnlyKnownNormalOperations()
    {
        var normal = new VerifiedNativeHostAuthorization();
        Assert.Equal(HostAuthorizationState.Authorized, normal.Authorize(HostControlOperation.ReadProfiles).State);
        Assert.Equal(HostAuthorizationState.Denied, normal.Authorize(HostControlOperation.StartNormal).State);
        Assert.Equal(HostAuthorizationState.Denied, normal.Authorize(HostControlOperation.PrepareNormal).State);
        var pilot = new VerifiedNativeHostAuthorization(true);
        Assert.Equal(HostAuthorizationState.Authorized, pilot.Authorize(HostControlOperation.StartNormal).State);
        Assert.Equal(HostAuthorizationState.Denied, pilot.Authorize((HostControlOperation)999).State);
    }

    [Fact]
    public async Task DeniedPilotCannotReadProfileStoreOrSendCommands()
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client();
        var bridge = new NativeBridgeDispatcher(client, new Phase4AuthorizationNotIntegrated(), lifetime,
            profileStore: data.Store);
        var result = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "profiles.list", new { offset = 0 }), CancellationToken.None);
        Assert.Equal("AuthorizationUnavailable", Error(result));
        Assert.False(File.Exists(data.DatabasePath));
        Assert.Equal(0, client.StatusCalls);
    }

    [Fact]
    public async Task SavedProfilesArePagedAndNeverExposePathsOrPolicyValues()
    {
        using var data = new Data();
        for (var i = 0; i < 10; i++) await data.Store.SaveProfileAsync(Profile() with
            { Name = new string('界', 70) + i, AccentHex = i == 0 ? "#AABBCC" : null });
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(), lifetime,
            profileStore: data.Store);
        var page = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "profiles.list", new { offset = 0 }), CancellationToken.None);
        using var json = JsonDocument.Parse(page!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(8, result.GetProperty("items").GetArrayLength());
        Assert.Equal("#AABBCC", result.GetProperty("items")[0].GetProperty("accentHex").GetString());
        Assert.Equal(10, result.GetProperty("total").GetInt32());
        Assert.False(result.GetProperty("pilotEnabled").GetBoolean());
        Assert.DoesNotContain("chat.exe", page!);
        Assert.DoesNotContain("social.example", page!);
        Assert.DoesNotContain(".db", page!);
        var next = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "profiles.list", new { offset = 8 }), CancellationToken.None);
        using var nextJson = JsonDocument.Parse(next!);
        Assert.Equal(2, nextJson.RootElement.GetProperty("result").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task RealPipeReviewAndConfirmationPreservePreparedEndAndSessionOnNewBridge()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        var pipe = "FocusGuard.Tests.Fg186." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(new InMemoryFocusService(), pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var client = new NamedPipeFocusControlClient(pipe);
            var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
                profileStore: data.Store, settingsStore: data.Settings, sessionJournalStore: data.Journal);
            var json = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
                "session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), cancellation.Token);
            using var parsed = JsonDocument.Parse(json!);
            var review = parsed.RootElement.GetProperty("result");
            Assert.Equal(profile.Name, review.GetProperty("profileName").GetString());
            Assert.Equal("Inactive", (await client.GetStatusAsync(cancellation.Token)).SessionState);
            await data.Store.SaveProfileAsync(profile with { Name = "Later edit", ApplicationRules = [], WebsiteRules = [] });
            const string objective = "Read chapter four";
            var startParams = new { preparationId = review.GetProperty("preparationId").GetString(), operationId = Guid.NewGuid().ToString("N"), objective };
            var started = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
                "session.startNormal", startParams), cancellation.Token);
            using var result = JsonDocument.Parse(started!);
            var session = result.RootElement.GetProperty("result");
            Assert.Equal(review.GetProperty("endUtc").GetDateTimeOffset(), session.GetProperty("endUtc").GetDateTimeOffset());
            Assert.Equal(objective, data.Journal.GetLatest(session.GetProperty("sessionId").GetGuid())!.Objective);
            Assert.Equal("PreparationUnavailable", Error(await bridge.HandleAsync("https://focusguard.app/index.html",
                Request(lease.Token, "session.startNormal", startParams), cancellation.Token)));
            lifetime.BeginDocument();
            var reopened = new NamedPipeFocusControlClient(pipe);
            var status = await reopened.GetStatusAsync(cancellation.Token);
            Assert.Equal(session.GetProperty("sessionId").GetGuid(), status.SessionId);
            Assert.Equal(review.GetProperty("endUtc").GetDateTimeOffset(), status.EndUtc);
            Assert.Equal("Active", status.SessionState);
            Assert.False(status.StrictMode);
            await reopened.StopAsync(cancellation.Token);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task NewDocumentCannotConsumeOldReviewEvenWithKnownPreparationId()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.prepareNormal",
            new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), CancellationToken.None);
        var newLease = lifetime.BeginDocument();
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(newLease.Token,
            "session.startNormal", new { preparationId = client.PreparationId.ToString("N"), operationId = Guid.NewGuid().ToString("N"), objective = "" }), CancellationToken.None);
        Assert.Equal("PreparationUnavailable", Error(response));
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task LostActivationReplyConsumesReviewAndDoesNotReplay()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client { ActivationFailure = new TimeoutException() };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.prepareNormal",
            new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), CancellationToken.None);
        var parameters = new { preparationId = client.PreparationId.ToString("N"), operationId = Guid.NewGuid().ToString("N"), objective = "" };
        Assert.Equal("OutcomeUnknown", Error(await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "session.startNormal", parameters), CancellationToken.None)));
        Assert.Equal("PreparationUnavailable", Error(await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "session.startNormal", parameters), CancellationToken.None)));
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task ShortTimerReviewValidityCannotExtendBeyondItsExactEnd()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
            "session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 60, enforcementEnabled = false }), CancellationToken.None);
        using var json = JsonDocument.Parse(response!);
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(result.GetProperty("endUtc").GetDateTimeOffset(), result.GetProperty("expiresUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task ProfileEditedToAllowOnlyCannotPretendToEnableBlocking()
    {
        using var data = new Data();
        var profile = Profile() with { ApplicationRules = [] };
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
            "session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = true }), CancellationToken.None);
        Assert.Equal("NoBlockingRules", Error(response));
        Assert.Equal(0, client.StartCalls);
        Assert.Equal("PreparationUnavailable", Error(await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
            "session.startNormal", new { preparationId = client.PreparationId.ToString("N"), operationId = Guid.NewGuid().ToString("N"), objective = "" }), CancellationToken.None)));
    }

    [Fact]
    public async Task InvalidAcceptedActivationIsUnknownAndCannotBeReplayed()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client { InvalidActivationReply = true };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.prepareNormal",
            new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), CancellationToken.None);
        var parameters = new { preparationId = client.PreparationId.ToString("N"), operationId = Guid.NewGuid().ToString("N"), objective = "" };
        Assert.Equal("OutcomeUnknown", Error(await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "session.startNormal", parameters), CancellationToken.None)));
        Assert.Equal("PreparationUnavailable", Error(await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "session.startNormal", parameters), CancellationToken.None)));
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task ReusingOperationIdOnAnotherReviewDoesNotSendAnotherActivation()
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new Client();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        var operationId = Guid.NewGuid().ToString("N");
        for (var i = 0; i < 2; i++)
        {
            await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.prepareNormal",
                new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), CancellationToken.None);
            var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
                "session.startNormal", new { preparationId = client.PreparationId.ToString("N"), operationId, objective = "" }), CancellationToken.None);
            Assert.Equal(i == 0 ? null : "DuplicateOperation", Error(response));
        }
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task StatusAndCommandsUseTheSameExclusiveGate()
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var waiting = new TaskCompletionSource<StatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { StatusPending = waiting.Task };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        var statusTask = bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "status.get", new { }), CancellationToken.None).AsTask();
        Assert.Equal(1, client.StatusCalls);
        var response = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(lease.Token, "profiles.list", new { offset = 0 }), CancellationToken.None);
        Assert.Equal("RequestInProgress", Error(response));
        Assert.False(File.Exists(data.DatabasePath));
        waiting.SetResult(new(true, false, "Inactive", 0, null, null, null, null, false, false));
        Assert.NotNull(await statusTask);
    }

    [Theory]
    [InlineData("Active", false)]
    [InlineData("RecoveryPending", false)]
    [InlineData("Expired", true)]
    public async Task StrictSessionMustFinishBeforeNormalReviewCanBePrepared(string state, bool allowed)
    {
        using var data = new Data();
        var profile = Profile();
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var now = DateTimeOffset.UtcNow;
        var client = new Client
        {
            StatusOverride = new(true, true, state, state == "Active" ? 300 : 0,
            Guid.NewGuid(), profile.Id.Value, now.AddMinutes(-1), now.AddMinutes(5), true, false)
        };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Store);
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token,
            "session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false }), CancellationToken.None);
        Assert.Equal(allowed ? null : "SessionAlreadyActive", Error(response));
        Assert.Equal(0, client.StartCalls);
    }

    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new
    { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });
    private static string? Error(string? response)
    {
        using var parsed = JsonDocument.Parse(response!);
        return parsed.RootElement.GetProperty("error").GetString();
    }
    private static StoredProfile Profile() => new(ProfileId.New(), "Saved profile",
        [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
        [new(Guid.NewGuid(), RuleAction.Allow, "social.example")]);
    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg186-" + Guid.NewGuid());
        public string DatabasePath => Path.Combine(_directory, "profiles.db");
        public SessionJournalStore Journal { get; }
        public DesktopSettingsStore Settings { get; }
        public SqliteProfileStore Store { get; }
        public Data()
        {
            Store = new(DatabasePath);
            Journal = new(Path.Combine(_directory, "session-journal.json"));
            Settings = new(Path.Combine(_directory, "desktop-settings.json"));
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
    private sealed class Client : IFocusControlClient
    {
        public Guid PreparationId { get; } = Guid.NewGuid();
        private DateTimeOffset _start;
        public int StartCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public Exception? ActivationFailure { get; init; }
        public bool InvalidActivationReply { get; init; }
        public Task<StatusResponse>? StatusPending { get; init; }
        public StatusResponse? StatusOverride { get; init; }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token)
        { StatusCalls++; return StatusPending is null ? ValueTask.FromResult(StatusOverride ?? new StatusResponse(true, false, "Inactive", 0, null, null, null, null, false, false)) : new(StatusPending); }
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token)
        { _start = DateTimeOffset.UtcNow; return ValueTask.FromResult(new PrepareSessionResponse(true, "Prepared", PreparationId, _start, _start.AddSeconds(request.DurationSeconds), _start.AddMinutes(2), SessionMode.SimpleTimer, [])); }
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token)
        { StartCalls++; if (ActivationFailure is not null) throw ActivationFailure; return ValueTask.FromResult(new ActivatePreparedSessionResponse(true, "Started", Guid.NewGuid(), _start, _start.AddMinutes(25).AddSeconds(InvalidActivationReply ? 1 : 0), SessionMode.SimpleTimer)); }
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
