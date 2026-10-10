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

public sealed class StrictSessionCommandTests
{
    [Theory]
    [InlineData("SimpleTimer")]
    [InlineData("FocusCycle")]
    public async Task StrictReviewRequiresSavedCodeAndExactProfileConfirmation(string mode)
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []);
        await data.Profiles.SaveProfileAsync(profile);
        var strictStore = new RecordingStrictSessionStore();
        var pipe = "FocusGuard.Tests.Fg209." + Guid.NewGuid().ToString("N");
        using var service = new FocusControlPipeService(new InMemoryFocusService(
            strictSessionStore: strictStore, extensionReadyProvider: () => true), pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(timeout.Token);
        try
        {
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var client = new NamedPipeFocusControlClient(pipe);
            string? savedCode = null;
            var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime,
                profileStore: data.Profiles, settingsStore: data.Settings, sessionJournalStore: data.Journal,
                recoveryCodeSaver: (code, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    savedCode = code;
                    return ValueTask.FromResult(true);
                });
            var parameters = mode == "SimpleTimer"
                ? new { profileId = profile.Id.Value.ToString("N"), mode, durationSeconds = 60, presetId = (string?)null,
                    configuration = (object?)null, enforcementEnabled = false }
                : new { profileId = profile.Id.Value.ToString("N"), mode, durationSeconds = 0, presetId = (string?)null,
                    configuration = (object?)new { focusSeconds = 60, shortBreakSeconds = 60, longBreakSeconds = 120,
                        cyclesBeforeLongBreak = 2, terminationMode = "CycleCount", cycleCount = (int?)2,
                        totalDurationSeconds = (int?)null }, enforcementEnabled = false };
            const string objective = "Complete the reading notes";

            var prepared = await Call(bridge, lease.Token, "session.strict.prepare", parameters, timeout.Token);
            Assert.Null(Error(prepared));
            var review = prepared.GetProperty("result");
            Assert.True(review.GetProperty("strict").GetBoolean());
            Assert.Equal(900, review.GetProperty("recoveryDelaySeconds").GetInt32());
            Assert.Equal(profile.Name, review.GetProperty("confirmationText").GetString());
            Assert.Equal(mode, review.GetProperty("mode").GetString());
            Assert.EndsWith("FocusGuard.Recovery.exe", review.GetProperty("recoveryHelperPath").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(strictStore.Activated);
            Assert.Equal("Inactive", (await client.GetStatusAsync(timeout.Token)).SessionState);
            var preparationId = review.GetProperty("preparationId").GetString()!;
            var recoveryCode = review.GetProperty("recoveryCode").GetString()!;
            var saved = await Call(bridge, lease.Token, "session.strict.saveRecoveryCode",
                new { preparationId, confirmed = true }, timeout.Token);
            Assert.Null(Error(saved));
            Assert.True(saved.GetProperty("result").GetProperty("saved").GetBoolean());
            Assert.Equal(recoveryCode, savedCode);

            var rejected = await Call(bridge, lease.Token, "session.strict.activate", new
            {
                preparationId, operationId = Guid.NewGuid().ToString("N"), typedConfirmation = "wrong profile",
                recoverySaved = true, unsavedWorkConfirmed = true, confirmed = true, objective
            }, timeout.Token);
            Assert.Equal("StrictConfirmationMismatch", Error(rejected));
            Assert.False(strictStore.Activated);
            var notSaved = await Call(bridge, lease.Token, "session.strict.activate", new
            {
                preparationId, operationId = Guid.NewGuid().ToString("N"), typedConfirmation = profile.Name,
                recoverySaved = false, unsavedWorkConfirmed = true, confirmed = true, objective
            }, timeout.Token);
            Assert.Equal("StrictConfirmationMismatch", Error(notSaved));
            Assert.False(strictStore.Activated);

            var started = await Call(bridge, lease.Token, "session.strict.activate", new
            {
                preparationId, operationId = Guid.NewGuid().ToString("N"), typedConfirmation = profile.Name,
                recoverySaved = true, unsavedWorkConfirmed = true, confirmed = true, objective
            }, timeout.Token);
            Assert.Null(Error(started));
            var activation = started.GetProperty("result");
            Assert.Equal(review.GetProperty("startUtc").GetDateTimeOffset(), activation.GetProperty("startUtc").GetDateTimeOffset());
            Assert.Equal(review.GetProperty("endUtc").GetDateTimeOffset(), activation.GetProperty("endUtc").GetDateTimeOffset());
            Assert.True(strictStore.Activated);
            Assert.True(RecoveryCodeService.Validate(recoveryCode, strictStore.Credential));

            var status = await new NamedPipeFocusControlClient(pipe).GetStatusAsync(timeout.Token);
            Assert.Equal("Active", status.SessionState);
            Assert.True(status.StrictMode);
            Assert.Equal(mode == "FocusCycle" ? SessionMode.FocusCycle : SessionMode.SimpleTimer, status.Mode);
            var intention = data.Journal.GetLatest(activation.GetProperty("sessionId").GetGuid());
            Assert.NotNull(intention);
            Assert.True(intention!.Strict);
            Assert.False(intention.EnforcementEnabled);
            Assert.Equal(objective, intention.Objective);
            Assert.Equal(mode == "FocusCycle" ? SessionMode.FocusCycle : SessionMode.SimpleTimer, intention.Mode);
            Assert.DoesNotContain(recoveryCode, await File.ReadAllTextAsync(data.JournalPath, timeout.Token));
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task CancelAndDocumentReplacementDiscardTheOneTimeReview()
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []);
        await data.Profiles.SaveProfileAsync(profile);
        var strictStore = new RecordingStrictSessionStore();
        var pipe = "FocusGuard.Tests.Fg209.Cancel." + Guid.NewGuid().ToString("N");
        using var service = new FocusControlPipeService(new InMemoryFocusService(strictSessionStore: strictStore), pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(timeout.Token);
        try
        {
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient(pipe),
                new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Profiles,
                settingsStore: data.Settings, sessionJournalStore: data.Journal);
            var parameters = new { profileId = profile.Id.Value.ToString("N"), mode = "SimpleTimer", durationSeconds = 60,
                presetId = (string?)null, configuration = (object?)null, enforcementEnabled = false };
            var readOnly = new NativeBridgeDispatcher(new NamedPipeFocusControlClient(pipe),
                new VerifiedNativeHostAuthorization(), lifetime, profileStore: data.Profiles,
                settingsStore: data.Settings, sessionJournalStore: data.Journal);
            Assert.Equal("Unauthorized", Error(await Call(readOnly, lease.Token, "session.strict.prepare", parameters, timeout.Token)));
            var prepared = await Call(bridge, lease.Token, "session.strict.prepare", parameters, timeout.Token);
            var prepId = prepared.GetProperty("result").GetProperty("preparationId").GetString()!;
            var cancelled = await Call(bridge, lease.Token, "session.strict.cancel", new { preparationId = prepId }, timeout.Token);
            Assert.True(cancelled.GetProperty("result").GetProperty("cancelled").GetBoolean());
            var afterCancel = await Call(bridge, lease.Token, "session.strict.activate", new
            {
                preparationId = prepId, operationId = Guid.NewGuid().ToString("N"), typedConfirmation = profile.Name,
                recoverySaved = true, unsavedWorkConfirmed = true, confirmed = true, objective = ""
            }, timeout.Token);
            Assert.Equal("StrictReviewUnavailable", Error(afterCancel));

            var next = await Call(bridge, lease.Token, "session.strict.prepare", parameters, timeout.Token);
            var nextId = next.GetProperty("result").GetProperty("preparationId").GetString()!;
            var newLease = lifetime.BeginDocument();
            var afterNavigation = await Call(bridge, newLease.Token, "session.strict.activate", new
            {
                preparationId = nextId, operationId = Guid.NewGuid().ToString("N"), typedConfirmation = profile.Name,
                recoverySaved = true, unsavedWorkConfirmed = true, confirmed = true, objective = ""
            }, timeout.Token);
            Assert.Equal("StrictReviewUnavailable", Error(afterNavigation));
            Assert.False(strictStore.Activated);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task StrictCycleReviewPreservesBlockBreakRulesWithoutActivating()
    {
        using var data = new Data();
        var profile = new StoredProfile(ProfileId.New(), "Saved focus", [], []);
        await data.Profiles.SaveProfileAsync(profile);
        await data.Profiles.SaveProfileCycleSettingsAsync(new ProfileCycleSettings(profile.Id, null,
            [new BreakApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "game.exe")], []));
        var strictStore = new RecordingStrictSessionStore();
        var pipe = "FocusGuard.Tests.Fg209.BreakRules." + Guid.NewGuid().ToString("N");
        using var service = new FocusControlPipeService(new InMemoryFocusService(strictSessionStore: strictStore), pipe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(timeout.Token);
        try
        {
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient(pipe),
                new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Profiles,
                settingsStore: data.Settings, sessionJournalStore: data.Journal);
            var response = await Call(bridge, lease.Token, "session.strict.prepare", new
            {
                profileId = profile.Id.Value.ToString("N"), mode = "FocusCycle", durationSeconds = 0,
                presetId = (string?)null,
                configuration = new { focusSeconds = 60, shortBreakSeconds = 60, longBreakSeconds = 120,
                    cyclesBeforeLongBreak = 2, terminationMode = "CycleCount", cycleCount = (int?)2,
                    totalDurationSeconds = (int?)null },
                enforcementEnabled = false
            }, timeout.Token);
            Assert.Equal(JsonValueKind.Null, response.GetProperty("error").ValueKind);
            var review = response.GetProperty("result");
            Assert.Equal(1, review.GetProperty("breakApplicationRuleCount").GetInt32());
            Assert.Equal("game.exe", review.GetProperty("breakApplications")[0].GetString());
            Assert.False(strictStore.Activated);
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData("session.strict.prepare", "{\"profileId\":\"00000000000040008000000000000001\",\"mode\":\"SimpleTimer\",\"durationSeconds\":60,\"presetId\":null,\"configuration\":null,\"enforcementEnabled\":false,\"strict\":true}")]
    [InlineData("session.strict.activate", "{\"preparationId\":\"00000000000040008000000000000001\",\"operationId\":\"00000000000040008000000000000002\",\"typedConfirmation\":\"Saved\",\"recoverySaved\":true,\"unsavedWorkConfirmed\":true}")]
    public void StrictBridgeRejectsUnknownFieldsAndMissingConfirmation(string method, string parameters) =>
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method,
            JsonSerializer.Deserialize<JsonElement>(parameters)), out _, out _));

    private static async Task<JsonElement> Call(NativeBridgeDispatcher bridge, string token, string method,
        object parameters, CancellationToken cancellationToken)
    {
        var response = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(token, method, parameters), cancellationToken);
        using var json = JsonDocument.Parse(response!);
        return json.RootElement.Clone();
    }

    private static string? Error(JsonElement response) => response.GetProperty("error").GetString();
    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new
    { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });

    private sealed class Data : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg209-" + Guid.NewGuid().ToString("N"));
        public SqliteProfileStore Profiles { get; }
        public DesktopSettingsStore Settings { get; }
        public SessionJournalStore Journal { get; }
        public string JournalPath => Path.Combine(_directory, "session-journal.json");
        public Data()
        {
            Profiles = new(Path.Combine(_directory, "profiles.db"));
            Settings = new(Path.Combine(_directory, "desktop-settings.json"));
            Journal = new(JournalPath);
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class RecordingStrictSessionStore : IStrictSessionStore
    {
        public RecoveryCredential? Credential { get; private set; }
        public StrictPolicySnapshot? Snapshot { get; private set; }
        public bool Activated => Snapshot is not null;
        public ValueTask ActivateAsync(Session session, EnforcementPolicy policy, RecoveryCredential recoveryCredential,
            CancellationToken cancellationToken)
        {
            Credential = recoveryCredential;
            Snapshot = new(StrictPolicySnapshotStore.CurrentSchemaVersion, policy, session.StartUtc,
                "fg209-test-boot", recoveryCredential, session.StrictMode.RecoveryDelay);
            return ValueTask.CompletedTask;
        }
        public ValueTask<StrictPolicyLoadResult> LoadAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Snapshot is null
                ? new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent")
                : new StrictPolicyLoadResult(StrictPolicyLoadStatus.Active, Snapshot, "Active"));
    }
}
