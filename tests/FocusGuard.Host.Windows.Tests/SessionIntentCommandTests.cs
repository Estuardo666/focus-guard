using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class SessionIntentCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedStrictSessionIntentCanBeReplayedThroughAFreshReview(bool cycle)
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []) { AccentHex = "#AABBCC" };
        await data.Profiles.SaveProfileAsync(profile);
        var sessionId = Guid.NewGuid();
        var configuration = cycle ? BuiltInCyclePresets.All[0].Configuration : null;
        data.Journal.Save(new(sessionId, profile.Id.Value, profile.Name, 25,
            cycle ? SessionMode.FocusCycle : SessionMode.SimpleTimer, null, configuration,
            Strict: true, Objective: "Read chapter four", EnforcementEnabled: false));

        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-" + Guid.NewGuid().ToString("N")),
            new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Profiles,
            sessionJournalStore: data.Journal);
        var response = await Call(bridge, document.Token, sessionId);
        var intent = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.True(intent.GetProperty("available").GetBoolean());
        Assert.True(intent.GetProperty("replayable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, intent.GetProperty("unavailableReason").ValueKind);
        Assert.True(intent.GetProperty("strict").GetBoolean());
        Assert.Equal("Read chapter four", intent.GetProperty("objective").GetString());
        Assert.Equal(profile.Id.Value.ToString("D"), intent.GetProperty("profile").GetProperty("id").GetString());
        Assert.Equal("#AABBCC", intent.GetProperty("profile").GetProperty("accentHex").GetString());
        Assert.Equal(cycle ? "FocusCycle" : "SimpleTimer", intent.GetProperty("mode").GetString());
        Assert.Equal(cycle, intent.GetProperty("cycleConfiguration").ValueKind != JsonValueKind.Null);
        Assert.Equal("Read chapter four", intent.GetProperty("objective").GetString());
        Assert.False(intent.TryGetProperty("recoveryCode", out _));
    }

    [Theory]
    [InlineData(SessionMode.SimpleTimer, false)]
    [InlineData(SessionMode.SimpleTimer, true)]
    [InlineData(SessionMode.FocusCycle, false)]
    [InlineData(SessionMode.FocusCycle, true)]
    public async Task IntentWithoutKnownEnforcementConsentCannotBeReplayed(SessionMode mode, bool strict)
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []);
        await data.Profiles.SaveProfileAsync(profile);
        var sessionId = Guid.NewGuid();
        var configuration = mode == SessionMode.FocusCycle ? BuiltInCyclePresets.All[0].Configuration : null;
        data.Journal.Save(new(sessionId, profile.Id.Value, profile.Name, 25, mode,
            null, configuration, Strict: strict, Objective: "", EnforcementEnabled: null));

        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-" + Guid.NewGuid().ToString("N")),
            new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Profiles,
            sessionJournalStore: data.Journal);
        var response = await Call(bridge, document.Token, sessionId);
        var intent = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.False(intent.GetProperty("replayable").GetBoolean());
        Assert.Equal("EnforcementConsentUnknown", intent.GetProperty("unavailableReason").GetString());
        Assert.Equal(JsonValueKind.Null, intent.GetProperty("enforcementEnabled").ValueKind);
        Assert.Equal(strict, intent.GetProperty("strict").GetBoolean());
        Assert.Equal(mode == SessionMode.FocusCycle, intent.GetProperty("cycleConfiguration").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task StrictIntentWithDeletedProfileCannotBeReplayed()
    {
        using var data = new Data();
        var sessionId = Guid.NewGuid();
        data.Journal.Save(new(sessionId, ProfileId.New().Value, "Deleted profile", 25,
            SessionMode.SimpleTimer, null, null, Strict: true, Objective: "", EnforcementEnabled: false));

        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-" + Guid.NewGuid().ToString("N")),
            new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Profiles,
            sessionJournalStore: data.Journal);
        var response = await Call(bridge, document.Token, sessionId);
        var intent = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.False(intent.GetProperty("replayable").GetBoolean());
        Assert.Equal("MissingProfile", intent.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task ActiveScheduledSessionRecoversTimerOnlyIntentAndStoresItLocally()
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []);
        await data.Profiles.SaveProfileAsync(profile);
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var status = new StatusResponse(true, true, "Active", 45, sessionId, profile.Id.Value,
            now, now.AddSeconds(45), false, false,
            ScheduledIntent: new ScheduledSessionIntent(profile.Name, 1, EnforcementEnabled: false));
        var client = new ScheduledClient(status, new(false, null, "Unavailable", "", 0, 0, 0, 0, null, []));

        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Profiles, sessionJournalStore: data.Journal);
        var response = await Call(bridge, document.Token, sessionId);
        var intent = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.True(intent.GetProperty("available").GetBoolean());
        Assert.True(intent.GetProperty("replayable").GetBoolean());
        Assert.False(intent.GetProperty("enforcementEnabled").GetBoolean());
        Assert.Equal(1, intent.GetProperty("durationMinutes").GetInt32());
        Assert.False(data.Journal.GetLatest(sessionId)!.EnforcementEnabled);
    }

    [Fact]
    public async Task CompletedScheduledSessionRecoversIntentAfterItsScheduleWasDeleted()
    {
        using var data = new Data();
        var app = ApplicationRule.Create(RuleAction.Block, ApplicationMatchKind.FileName, "NeverRun.exe");
        Assert.True(app.IsValid);
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [app.Value!], []);
        await data.Profiles.SaveProfileAsync(profile);
        var sessionId = Guid.NewGuid();
        var scheduled = new ScheduledSessionIntent(profile.Name, 25, EnforcementEnabled: true);
        var status = new StatusResponse(true, true, "Inactive", 0, null, null, null, null, false, false);
        var summary = new SessionSummaryResponse(true, sessionId, CaptureProtocol.CompletedOutcome, profile.Name,
            24, 24, 0, 1, DateTimeOffset.UtcNow, [], ProfileId: profile.Id.Value, ScheduledIntent: scheduled);
        var client = new ScheduledClient(status, summary);

        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
            profileStore: data.Profiles, sessionJournalStore: data.Journal);
        var response = await Call(bridge, document.Token, sessionId);
        var intent = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.True(intent.GetProperty("available").GetBoolean());
        Assert.True(intent.GetProperty("replayable").GetBoolean());
        Assert.True(intent.GetProperty("enforcementEnabled").GetBoolean());
        Assert.Equal(25, intent.GetProperty("durationMinutes").GetInt32());
        Assert.True(data.Journal.GetLatest(sessionId)!.EnforcementEnabled);
    }
    private static async Task<JsonElement> Call(NativeBridgeDispatcher bridge, string documentToken, Guid sessionId)
    {
        var request = JsonSerializer.Serialize(new
        {
            version = 1,
            requestId = Guid.NewGuid().ToString("N"),
            documentToken,
            method = "session.intent.get",
            @params = new { sessionId = sessionId.ToString("N") },
        });
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", request, CancellationToken.None);
        using var json = JsonDocument.Parse(response!);
        return json.RootElement.Clone();
    }

    private static string? Error(JsonElement response) => response.GetProperty("error").GetString();

    private sealed class ScheduledClient(StatusResponse status, SessionSummaryResponse summary) : IFocusControlClient
    {
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => ValueTask.FromResult(status);
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => ValueTask.FromResult(summary);
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg210-" + Guid.NewGuid().ToString("N"));
        public SqliteProfileStore Profiles { get; }
        public SessionJournalStore Journal { get; } = new(null);

        public Data() => Profiles = new(Path.Combine(_directory, "profiles.db"));

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
