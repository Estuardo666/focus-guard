using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Preferences;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class CycleCommandTests
{
    [Theory]
    [InlineData("cycles.list", "{\"offset\":-1}")]
    [InlineData("cycles.list", "{\"offset\":0,\"path\":\"other.db\"}")]
    [InlineData("session.prepareCycle", "{\"profileId\":\"00000000000000000000000000000001\",\"presetId\":null,\"configuration\":null,\"enforcementEnabled\":false}")]
    [InlineData("session.prepareCycle", "{\"profileId\":\"00000000000000000000000000000001\",\"presetId\":\"00000000000000000000000000000002\",\"configuration\":null,\"enforcementEnabled\":false,\"strictMode\":true}")]
    [InlineData("session.startCycle", "{\"preparationId\":\"00000000000000000000000000000001\"}")]
    public void UnsafeParametersAreRejected(string method, string parameters) =>
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method,
            JsonSerializer.Deserialize<JsonElement>(parameters)), out _, out _));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPipeCycleReviewStartsOnceWithExactEndAndSurvivesNewClient(bool custom)
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Cycle profile", [], []);
        await data.Store.SaveProfileAsync(profile);
        await data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, null,
            [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "game.exe")],
            [new(Guid.NewGuid(), RuleAction.Block, "games.example")]));
        var pipe = "FocusGuard.Tests.Fg194." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(new InMemoryFocusService(), pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await server.StartAsync(cancellation.Token);
        try
        {
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var client = new NamedPipeFocusControlClient(pipe);
            var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
                profileStore: data.Store, settingsStore: data.Settings, sessionJournalStore: data.Journal);
            async Task<JsonElement> Call(string method, object parameters)
            {
                var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, method, parameters), cancellation.Token);
                using var json = JsonDocument.Parse(response!);
                return json.RootElement.Clone();
            }
            var presets = await Call("cycles.list", new { offset = 0 });
            Assert.Equal(JsonValueKind.Null, presets.GetProperty("error").ValueKind);
            Assert.NotEmpty(presets.GetProperty("result").GetProperty("items").EnumerateArray());
            object? configuration = custom ? new
            {
                focusSeconds = 60,
                shortBreakSeconds = 60,
                longBreakSeconds = 120,
                cyclesBeforeLongBreak = 2,
                terminationMode = "TotalDuration",
                cycleCount = (int?)null,
                totalDurationSeconds = 150
            } : null;
            var prepared = await Call("session.prepareCycle", new
            {
                profileId = profile.Id.Value.ToString("N"),
                presetId = custom ? null : BuiltInCyclePresets.ClassicId.ToString("N"),
                configuration,
                enforcementEnabled = false
            });
            Assert.Equal(JsonValueKind.Null, prepared.GetProperty("error").ValueKind);
            var review = prepared.GetProperty("result");
            Assert.Equal(custom, review.GetProperty("partialFinalPhase").GetBoolean());
            Assert.Equal(1, review.GetProperty("breakApplicationRuleCount").GetInt32());
            Assert.Equal("game.exe", review.GetProperty("breakApplications")[0].GetString());
            Assert.Equal(1, review.GetProperty("breakWebsiteRuleCount").GetInt32());
            Assert.Equal("games.example", review.GetProperty("breakWebsites")[0].GetString());
            Assert.Equal("Inactive", (await client.GetStatusAsync(cancellation.Token)).SessionState);
            var preparationId = review.GetProperty("preparationId").GetString();
            const string objective = "Complete the reading notes";
            var started = await Call("session.startCycle", new { preparationId, operationId = Guid.NewGuid().ToString("N"), objective });
            Assert.Equal(JsonValueKind.Null, started.GetProperty("error").ValueKind);
            Assert.Equal(review.GetProperty("endUtc").GetDateTimeOffset(), started.GetProperty("result").GetProperty("endUtc").GetDateTimeOffset());
            var sessionId = started.GetProperty("result").GetProperty("sessionId").GetGuid();
            Assert.Equal(objective, data.Journal.GetLatest(sessionId)!.Objective);
            Assert.Equal("PreparationUnavailable", (await Call("session.startCycle", new { preparationId, operationId = Guid.NewGuid().ToString("N"), objective })).GetProperty("error").GetString());
            var reopened = await new NamedPipeFocusControlClient(pipe).GetStatusAsync(cancellation.Token);
            Assert.Equal(SessionMode.FocusCycle, reopened.Mode);
            Assert.False(reopened.StrictMode);
            Assert.Equal(review.GetProperty("endUtc").GetDateTimeOffset(), reopened.EndUtc);
            await client.StopAsync(cancellation.Token);
            var second = await Call("session.prepareCycle", new { profileId = profile.Id.Value.ToString("N"), presetId = BuiltInCyclePresets.ClassicId.ToString("N"), configuration = (object?)null, enforcementEnabled = false });
            var oldId = second.GetProperty("result").GetProperty("preparationId").GetString();
            await Call("session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 60, enforcementEnabled = false });
            Assert.Equal("PreparationUnavailable", (await Call("session.startCycle", new { preparationId = oldId, operationId = Guid.NewGuid().ToString("N"), objective })).GetProperty("error").GetString());
            var readOnly = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(), lifetime, profileStore: data.Store);
            var denied = await readOnly.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.prepareCycle",
                new { profileId = profile.Id.Value.ToString("N"), presetId = BuiltInCyclePresets.ClassicId.ToString("N"), configuration = (object?)null, enforcementEnabled = false }), cancellation.Token);
            using var deniedJson = JsonDocument.Parse(denied!);
            Assert.NotEqual(JsonValueKind.Null, deniedJson.RootElement.GetProperty("error").ValueKind);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new
    { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });
    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg194-" + Guid.NewGuid());
        public SqliteProfileStore Store { get; }
        public SessionJournalStore Journal { get; }
        public DesktopSettingsStore Settings { get; }
        public Data()
        {
            Store = new(Path.Combine(_directory, "profiles.db"));
            Journal = new(Path.Combine(_directory, "session-journal.json"));
            Settings = new(Path.Combine(_directory, "desktop-settings.json"));
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
