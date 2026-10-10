using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Service;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class ProfileCommandTests
{
    [Theory]
    [InlineData("profile.create", "{\"name\":\"Work\",\"accentHex\":null}")]
    [InlineData("profile.create", "{\"name\":\"Work\",\"name\":\"Other\",\"accentHex\":null,\"operationId\":\"00000000000000000000000000000001\"}")]
    [InlineData("profile.create", "{\"name\":\"Work\",\"accentHex\":null,\"operationId\":\"00000000000000000000000000000001\",\"path\":\"C:/other.db\"}")]
    [InlineData("profile.get", "{\"profileId\":\"00000000000000000000000000000001\",\"applicationOffset\":-1,\"websiteOffset\":0,\"breakApplicationOffset\":0,\"breakWebsiteOffset\":0}")]
    [InlineData("profile.get", "{\"profileId\":\"00000000000000000000000000000001\",\"applicationOffset\":0,\"websiteOffset\":0,\"breakApplicationOffset\":0,\"breakWebsiteOffset\":0,\"sql\":\"SELECT *\"}")]
    [InlineData("profile.delete", "{\"profileId\":\"00000000000000000000000000000001\",\"revision\":\"bad\",\"operationId\":\"00000000000000000000000000000002\",\"confirmed\":true}")]
    [InlineData("profile.invoke", "{}")]
    public void ProfileParserRejectsMalformedDuplicateUnknownOrGenericParameters(string method, string parameters)
    {
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method,
            JsonSerializer.Deserialize<JsonElement>(parameters)), out _, out _));
    }

    [Theory]
    [InlineData("profile.delete", "confirmed", "false")]
    [InlineData("profile.duplicate", "language", "\"xx\"")]
    [InlineData("profile.application.add", "action", "0")]
    [InlineData("profile.application.add", "matchKind", "\"0\"")]
    [InlineData("profile.application.add", "matchKind", "\"Shell\"")]
    [InlineData("profile.accent", "accentHex", "\"red\"")]
    public void ProfileParserRejectsUnsupportedEnumsAndMissingConfirmation(string method, string property, string value)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["profileId"] = Guid.NewGuid().ToString("N"),
            ["revision"] = new string('A', 64),
            ["operationId"] = Guid.NewGuid().ToString("N")
        };
        if (method == "profile.application.add") { parameters["action"] = "Block"; parameters["matchKind"] = "FileName"; parameters["value"] = "work.exe"; }
        parameters[property] = JsonSerializer.Deserialize<JsonElement>(value);
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method, parameters), out _, out _));
    }

    [Fact]
    public void BreakRuleParserRejectsActionInjectionAndUnknownMatchKinds()
    {
        var baseParameters = new Dictionary<string, object?>
        {
            ["profileId"] = Guid.NewGuid().ToString("N"),
            ["cycleSettingsRevision"] = new string('A', 64),
            ["operationId"] = Guid.NewGuid().ToString("N"),
            ["matchKind"] = "FileName",
            ["value"] = "game.exe"
        };
        var withAction = new Dictionary<string, object?>(baseParameters) { ["action"] = "Allow" };
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"),
            "profile.break.application.add", withAction), out _, out _));
        baseParameters["matchKind"] = "ProcessName";
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"),
            "profile.break.application.add", baseParameters), out _, out _));
    }

    [Fact]
    public async Task DefaultVerifiedHostCanReadButCannotCreateOrModifyProfiles()
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var client = new Client();
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(), lifetime, profileStore: data.Store);
        var response = await Call(bridge, lease.Token, "profile.create", new { name = "Work", accentHex = (string?)null, operationId = Guid.NewGuid().ToString("N") });
        Assert.Equal("Unauthorized", Error(response));
        Assert.False(File.Exists(data.Path)); Assert.Equal(0, client.StatusCalls);
        var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        var read = await Call(bridge, lease.Token, "profile.get", new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
        using var json = JsonDocument.Parse(read!);
        Assert.False(json.RootElement.GetProperty("result").GetProperty("editingEnabled").GetBoolean());
        Assert.False(json.RootElement.GetProperty("result").GetProperty("cycleSettingsEditingEnabled").GetBoolean());
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Activating")]
    [InlineData("RecoveryPending")]
    public async Task ActiveOrRecoverySessionRejectsEditsWithoutOpeningDatabase(string state)
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var now = DateTimeOffset.UtcNow;
        var client = new Client { Status = new(true, true, state, 300, Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(5), true, true) };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
        var response = await Call(bridge, lease.Token, "profile.create", new { name = "Work", accentHex = (string?)null, operationId = Guid.NewGuid().ToString("N") });
        Assert.Equal("SessionAlreadyActive", Error(response)); Assert.False(File.Exists(data.Path));
    }

    [Fact]
    public async Task UnavailableOrInvalidStatusCannotCreateAProfile()
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var client = new Client { Failure = new IOException() };
        var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
        var response = await Call(bridge, lease.Token, "profile.create", new { name = "Work", accentHex = (string?)null, operationId = Guid.NewGuid().ToString("N") });
        Assert.Equal("OutcomeUnknown", Error(response)); Assert.False(File.Exists(data.Path));
    }

    [Fact]
    public async Task RealServicePipeAndSqliteSupportTypedCreateReadRuleEditsAndNoReplay()
    {
        using var data = new Data();
        var pipe = "FocusGuard.Tests.Fg188." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(new InMemoryFocusService(), pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await server.StartAsync(cancellation.Token);
        try
        {
            using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient(pipe), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
            var operationId = Guid.NewGuid().ToString("N");
            var parameters = new { name = "Real saved profile", accentHex = "#AABBCC", operationId };
            var created = await Call(bridge, lease.Token, "profile.create", parameters);
            using var createJson = JsonDocument.Parse(created!);
            var id = createJson.RootElement.GetProperty("result").GetProperty("profileId").GetGuid();
            var revision = createJson.RootElement.GetProperty("result").GetProperty("revision").GetString();
            Assert.Equal("DuplicateOperation", Error(await Call(bridge, lease.Token, "profile.create", parameters)));
            Assert.Single(await data.Store.ListProfilesAsync());
            var app = await Call(bridge, lease.Token, "profile.application.add", new
            {
                profileId = id.ToString("N"),
                revision,
                operationId = Guid.NewGuid().ToString("N"),
                action = "Allow",
                matchKind = "FileName",
                value = "work.exe"
            });
            using var appJson = JsonDocument.Parse(app!);
            revision = appJson.RootElement.GetProperty("result").GetProperty("revision").GetString();
            var site = await Call(bridge, lease.Token, "profile.website.add", new
            {
                profileId = id.ToString("N"),
                revision,
                operationId = Guid.NewGuid().ToString("N"),
                action = "Block",
                pattern = " *.Example.COM/* "
            });
            Assert.Null(Error(site));
            var existingBreakApp = new BreakApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "game.exe");
            var existingBreakWebsite = new BreakWebsiteRule(Guid.NewGuid(), RuleAction.Block, "games.example");
            await data.Store.SaveProfileCycleSettingsAsync(new(new ProfileId(id), null, [existingBreakApp], [existingBreakWebsite]));
            var detail = await Call(bridge, lease.Token, "profile.get", new { profileId = id.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
            using var detailJson = JsonDocument.Parse(detail!);
            var result = detailJson.RootElement.GetProperty("result");
            Assert.Equal("Allow", result.GetProperty("applications")[0].GetProperty("action").GetString());
            Assert.Equal("FileName", result.GetProperty("applications")[0].GetProperty("matchKind").GetString());
            Assert.Equal("*.example.com/*", result.GetProperty("websites")[0].GetProperty("pattern").GetString());
            Assert.True(result.GetProperty("editingEnabled").GetBoolean());
            Assert.True(result.GetProperty("cycleSettingsEditingEnabled").GetBoolean());
            Assert.Equal("game.exe", result.GetProperty("breakApplications")[0].GetProperty("value").GetString());
            Assert.Equal("games.example", result.GetProperty("breakWebsites")[0].GetProperty("pattern").GetString());
            var firstCycleRevision = result.GetProperty("cycleSettingsRevision").GetString();
            Assert.Equal(JsonValueKind.Null, result.GetProperty("defaultCyclePresetId").ValueKind);
            var setDefaultPreset = await Call(bridge, lease.Token, "profile.cycle.defaultPreset.set", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = firstCycleRevision,
                operationId = Guid.NewGuid().ToString("N"), presetId = BuiltInCyclePresets.ClassicId.ToString("N")
            });
            Assert.Null(Error(setDefaultPreset));
            var presetDetail = await Call(bridge, lease.Token, "profile.get", new { profileId = id.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
            using var presetDetailJson = JsonDocument.Parse(presetDetail!);
            var presetDetailResult = presetDetailJson.RootElement.GetProperty("result");
            Assert.Equal(BuiltInCyclePresets.ClassicId, presetDetailResult.GetProperty("defaultCyclePresetId").GetGuid());
            Assert.Equal("Pomodoro clásico", presetDetailResult.GetProperty("defaultCyclePresetName").GetString());
            var defaultPresetRevision = presetDetailResult.GetProperty("cycleSettingsRevision").GetString();
            var profilePage = await Call(bridge, lease.Token, "profiles.list", new { offset = 0 });
            using var profilePageJson = JsonDocument.Parse(profilePage!);
            var profileSummary = profilePageJson.RootElement.GetProperty("result").GetProperty("items")[0];
            Assert.Equal(BuiltInCyclePresets.ClassicId, profileSummary.GetProperty("defaultCyclePresetId").GetGuid());
            Assert.Equal("Pomodoro clásico", profileSummary.GetProperty("defaultCyclePresetName").GetString());
            Assert.Equal(1500, profileSummary.GetProperty("defaultCyclePresetConfiguration").GetProperty("focusSeconds").GetInt64());
            var addedApp = await Call(bridge, lease.Token, "profile.break.application.add", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = defaultPresetRevision,
                operationId = Guid.NewGuid().ToString("N"), matchKind = "FileName", value = "later.exe"
            });
            Assert.Null(Error(addedApp));
            var appRuleId = Assert.Single((await data.Store.LoadProfileCycleSettingsAsync(new ProfileId(id)))!.ApplicationRules,
                rule => rule.Value == "game.exe").Id;
            var appSnapshot = await data.Store.LoadProfileCycleSettingsAsync(new ProfileId(id));
            Assert.Equal(2, appSnapshot!.ApplicationRules.Count);
            var updatedDetail = await Call(bridge, lease.Token, "profile.get", new { profileId = id.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
            using var updatedJson = JsonDocument.Parse(updatedDetail!);
            var updatedCycleRevision = updatedJson.RootElement.GetProperty("result").GetProperty("cycleSettingsRevision").GetString();
            Assert.NotEqual(defaultPresetRevision, updatedCycleRevision);
            Assert.Equal("ProfileConflict", Error(await Call(bridge, lease.Token, "profile.break.website.add", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = defaultPresetRevision,
                operationId = Guid.NewGuid().ToString("N"), pattern = "stale.example"
            })));
            var addedSite = await Call(bridge, lease.Token, "profile.break.website.add", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = updatedCycleRevision,
                operationId = Guid.NewGuid().ToString("N"), pattern = "later.example"
            });
            Assert.Null(Error(addedSite));
            var fresh = await data.Store.LoadProfileCycleSettingsAsync(new ProfileId(id));
            var laterAppId = Assert.Single(fresh!.ApplicationRules, rule => rule.Value == "later.exe").Id;
            var currentCycleRevision = (await data.Store.LoadRevisionedProfileCycleSettingsAsync(new ProfileId(id)))!.Revision;
            var removed = await Call(bridge, lease.Token, "profile.break.application.remove", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = currentCycleRevision,
                operationId = Guid.NewGuid().ToString("N"), ruleId = laterAppId.ToString("N")
            });
            Assert.Null(Error(removed));
            var final = await data.Store.LoadProfileCycleSettingsAsync(new ProfileId(id));
            Assert.Contains(final!.ApplicationRules, rule => rule.Id == appRuleId);
            Assert.DoesNotContain(final.ApplicationRules, rule => rule.Id == laterAppId);
            Assert.Contains(final.WebsiteRules, rule => rule.Pattern == "later.example");
            var latestRevision = (await data.Store.LoadRevisionedProfileCycleSettingsAsync(new ProfileId(id)))!.Revision;
            var resetDefault = await Call(bridge, lease.Token, "profile.cycle.defaultPreset.set", new
            {
                profileId = id.ToString("N"), cycleSettingsRevision = latestRevision,
                operationId = Guid.NewGuid().ToString("N"), presetId = (string?)null
            });
            Assert.Null(Error(resetDefault));
            Assert.Null((await data.Store.LoadProfileCycleSettingsAsync(new ProfileId(id)))!.DefaultPresetId);
            Assert.Equal("Inactive", (await new NamedPipeFocusControlClient(pipe).GetStatusAsync(cancellation.Token)).SessionState);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task StaleRevisionAndLastProfileAreRejectedAndDeleteRequiresConfirmedTrue()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
        var revision = ProfileRevision.Compute(profile);
        Assert.Equal("LastProfile", Error(await Call(bridge, lease.Token, "profile.delete", new { profileId = profile.Id.Value.ToString("N"), revision, operationId = Guid.NewGuid().ToString("N"), confirmed = true })));
        await data.Store.SaveProfileAsync(profile with { Description = "Another window edited" });
        Assert.Equal("ProfileConflict", Error(await Call(bridge, lease.Token, "profile.rename", new { profileId = profile.Id.Value.ToString("N"), revision, operationId = Guid.NewGuid().ToString("N"), name = "Overwrite" })));
        Assert.Equal(profile.Name, (await data.Store.LoadProfileAsync(profile.Id))!.Name);
    }

    [Fact]
    public async Task DetailPagesAreBoundedAndRevisionIncludesRulesOutsideThePage()
    {
        using var data = new Data(); var profile = Profile() with
        { ApplicationRules = Enumerable.Range(0, 9).Select(i => new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, $"app{i}.exe")).ToArray() };
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(), lifetime, profileStore: data.Store);
        var page = await Call(bridge, lease.Token, "profile.get", new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
        using var json = JsonDocument.Parse(page!); var result = json.RootElement.GetProperty("result");
        Assert.Equal(6, result.GetProperty("applications").GetArrayLength()); Assert.Equal(9, result.GetProperty("applicationTotal").GetInt32());
        var revision = result.GetProperty("revision").GetString();
        var breakRules = Enumerable.Range(0, 9).Select(i => new BreakApplicationRule(Guid.NewGuid(), RuleAction.Block,
            ApplicationMatchKind.FileName, $"break{i}.exe")).ToArray();
        await data.Store.SaveProfileCycleSettingsAsync(new(profile.Id, null, breakRules, []));
        var breakPage = await Call(bridge, lease.Token, "profile.get", new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 6, breakWebsiteOffset = 0 });
        using var breakJson = JsonDocument.Parse(breakPage!);
        var breakResult = breakJson.RootElement.GetProperty("result");
        Assert.Equal(3, breakResult.GetProperty("breakApplications").GetArrayLength());
        Assert.Equal(9, breakResult.GetProperty("breakApplicationTotal").GetInt32());
        var cycleRevision = breakResult.GetProperty("cycleSettingsRevision").GetString();
        var cycleSnapshot = (await data.Store.LoadRevisionedProfileCycleSettingsAsync(profile.Id))!;
        var lastBreak = cycleSnapshot.Settings.ApplicationRules.OrderBy(rule => rule.Id).Last();
        var changedSettings = cycleSnapshot.Settings with
        {
            ApplicationRules = cycleSnapshot.Settings.ApplicationRules.Select(rule => rule.Id == lastBreak.Id ? rule with { Value = "changed-break.exe" } : rule).ToArray()
        };
        Assert.Equal(ProfileCycleSettingsMutationStatus.Saved,
            (await data.Store.TrySaveProfileCycleSettingsAsync(changedSettings, cycleSnapshot.Revision)).Status);
        var changedBreakPage = await Call(bridge, lease.Token, "profile.get", new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
        using var changedBreakJson = JsonDocument.Parse(changedBreakPage!);
        Assert.NotEqual(cycleRevision, changedBreakJson.RootElement.GetProperty("result").GetProperty("cycleSettingsRevision").GetString());
        var last = profile.ApplicationRules.OrderBy(rule => rule.Id).Last();
        await data.Store.SaveProfileAsync(profile with { ApplicationRules = profile.ApplicationRules.Select(rule => rule.Id == last.Id ? rule with { Value = "changed.exe" } : rule).ToArray() });
        var changed = await Call(bridge, lease.Token, "profile.get", new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 });
        using var changedJson = JsonDocument.Parse(changed!);
        Assert.NotEqual(revision, changedJson.RootElement.GetProperty("result").GetProperty("revision").GetString());
    }

    [Fact]
    public async Task OldDocumentCannotWriteAndMutationInvalidatesEarlierStartReview()
    {
        using var data = new Data(); var profile = Profile(); await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var old = lifetime.BeginDocument(); var current = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
        Assert.Equal("StaleDocument", Error(await Call(bridge, old.Token, "profile.create", new { name = "Stale", accentHex = (string?)null, operationId = Guid.NewGuid().ToString("N") })));
        await Call(bridge, current.Token, "session.prepareNormal", new { profileId = profile.Id.Value.ToString("N"), durationSeconds = 1500, enforcementEnabled = false });
        var preparedId = profile.Id.Value.ToString("N");
        await Call(bridge, current.Token, "profile.rename", new { profileId = profile.Id.Value.ToString("N"), revision = ProfileRevision.Compute(profile), operationId = Guid.NewGuid().ToString("N"), name = "New name" });
        Assert.Equal("PreparationUnavailable", Error(await Call(bridge, current.Token, "session.startNormal", new { preparationId = preparedId, operationId = Guid.NewGuid().ToString("N"), objective = "" })));
        Assert.Single(await data.Store.ListProfilesAsync());
    }

    [Fact]
    public async Task FullTypedEditCopyAndDeleteLifecyclePreservesRuleIdentitiesAndMetadata()
    {
        using var data = new Data();
        var profile = Profile() with
        {
            Description = "Keep this description",
            AccentHex = "#AABBCC",
            ApplicationRules = [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.Folder, @"C:\Apps")],
            WebsiteRules = [new(Guid.NewGuid(), RuleAction.Allow, "work.example")]
        };
        await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store);
        async Task<StoredProfile> Current() => (await data.Store.LoadProfileAsync(profile.Id))!;
        var commonId = profile.Id.Value.ToString("N");
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.rename", new { profileId = commonId, revision = ProfileRevision.Compute(await Current()), operationId = Guid.NewGuid().ToString("N"), name = "Renamed" })));
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.accent", new { profileId = commonId, revision = ProfileRevision.Compute(await Current()), operationId = Guid.NewGuid().ToString("N"), accentHex = (string?)null })));
        var updated = await Current();
        Assert.Equal("Keep this description", updated.Description);
        Assert.Equal(profile.ApplicationRules[0].Id, updated.ApplicationRules[0].Id);
        Assert.Equal(profile.WebsiteRules[0].Id, updated.WebsiteRules[0].Id);
        Assert.Null(updated.AccentHex);
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.application.remove", new { profileId = commonId, revision = ProfileRevision.Compute(updated), operationId = Guid.NewGuid().ToString("N"), ruleId = updated.ApplicationRules[0].Id.ToString("N") })));
        updated = await Current();
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.website.remove", new { profileId = commonId, revision = ProfileRevision.Compute(updated), operationId = Guid.NewGuid().ToString("N"), ruleId = updated.WebsiteRules[0].Id.ToString("N") })));
        updated = await Current(); Assert.Empty(updated.ApplicationRules); Assert.Empty(updated.WebsiteRules);
        var copy = await Call(bridge, lease.Token, "profile.duplicate", new { profileId = commonId, revision = ProfileRevision.Compute(updated), operationId = Guid.NewGuid().ToString("N"), language = "es" });
        using var json = JsonDocument.Parse(copy!); var id = json.RootElement.GetProperty("result").GetProperty("profileId").GetGuid();
        var duplicate = (await data.Store.LoadProfileAsync(new(id)))!;
        Assert.Equal("Copia de Renamed", duplicate.Name);
        Assert.Equal("Keep this description", duplicate.Description);
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.delete", new { profileId = id.ToString("N"), revision = ProfileRevision.Compute(duplicate), operationId = Guid.NewGuid().ToString("N"), confirmed = true })));
        Assert.Single(await data.Store.ListProfilesAsync());
    }

    [Fact]
    public async Task LostReplyAfterRealCommitCannotReplayTheCreation()
    {
        using var data = new Data();
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var store = new CommitThenFailStore(data.Store);
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: store);
        var parameters = new { name = "Committed once", accentHex = (string?)null, operationId = Guid.NewGuid().ToString("N") };
        Assert.Equal("OutcomeUnknown", Error(await Call(bridge, lease.Token, "profile.create", parameters)));
        Assert.Equal("DuplicateOperation", Error(await Call(bridge, lease.Token, "profile.create", parameters)));
        Assert.Equal(1, store.CommitCalls);
        Assert.Equal("Committed once", Assert.Single(await data.Store.ListProfilesAsync()).Name);
    }

    private sealed class CommitThenFailStore(SqliteProfileStore inner) : IRevisionedProfileStore
    {
        public int CommitCalls { get; private set; }
        public async Task<ProfileMutationResult> TrySaveProfileAsync(StoredProfile profile, string? revision, CancellationToken token = default)
        {
            var result = await inner.TrySaveProfileAsync(profile, revision, token);
            Assert.Equal(ProfileMutationStatus.Saved, result.Status);
            CommitCalls++; throw new IOException("Isolated post-commit reply-loss fixture");
        }
        public Task<ProfileMutationResult> TryDeleteProfileAsync(ProfileId id, string revision, CancellationToken token = default) => inner.TryDeleteProfileAsync(id, revision, token);
        public Task<ProfileMutationResult> TryDuplicateProfileAsync(ProfileId id, string revision, string prefix, CancellationToken token = default) => inner.TryDuplicateProfileAsync(id, revision, prefix, token);
        public Task SaveProfileAsync(StoredProfile profile, CancellationToken token = default) => inner.SaveProfileAsync(profile, token);
        public Task<StoredProfile?> LoadProfileAsync(ProfileId id, CancellationToken token = default) => inner.LoadProfileAsync(id, token);
        public Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(CancellationToken token = default) => inner.ListProfilesAsync(token);
        public Task<bool> DeleteProfileAsync(ProfileId id, CancellationToken token = default) => inner.DeleteProfileAsync(id, token);
        public Task SaveCyclePresetAsync(StoredCyclePreset preset, CancellationToken token = default) => inner.SaveCyclePresetAsync(preset, token);
        public Task<StoredCyclePreset?> LoadCyclePresetAsync(Guid id, CancellationToken token = default) => inner.LoadCyclePresetAsync(id, token);
        public Task<IReadOnlyList<StoredCyclePreset>> ListCyclePresetsAsync(CancellationToken token = default) => inner.ListCyclePresetsAsync(token);
        public Task<bool> DeleteCyclePresetAsync(Guid id, CancellationToken token = default) => inner.DeleteCyclePresetAsync(id, token);
        public Task SaveProfileCycleSettingsAsync(ProfileCycleSettings settings, CancellationToken token = default) => inner.SaveProfileCycleSettingsAsync(settings, token);
        public Task<ProfileCycleSettings?> LoadProfileCycleSettingsAsync(ProfileId id, CancellationToken token = default) => inner.LoadProfileCycleSettingsAsync(id, token);
    }

    private static StoredProfile Profile() => new(ProfileId.New(), "Work", [], []);
    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });
    private static ValueTask<string?> Call(NativeBridgeDispatcher bridge, string token, string method, object parameters) => bridge.HandleAsync("https://focusguard.app/index.html", Request(token, method, parameters), CancellationToken.None);
    private static string? Error(string? response) { using var json = JsonDocument.Parse(response!); return json.RootElement.GetProperty("error").GetString(); }
    private sealed class Data : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg188-" + Guid.NewGuid());
        public string Path => System.IO.Path.Combine(_directory, "profiles.db");
        public SqliteProfileStore Store { get; }
        public Data() => Store = new(Path);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
    private sealed class Client : IFocusControlClient
    {
        public int StatusCalls { get; private set; }
        public StatusResponse Status { get; init; } = new(true, true, "Inactive", 0, null, null, null, null, false, false);
        public Exception? Failure { get; init; }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) { StatusCalls++; if (Failure is not null) throw Failure; return ValueTask.FromResult(Status); }
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) { var now = DateTimeOffset.UtcNow; return ValueTask.FromResult(new PrepareSessionResponse(true, "Prepared", request.ProfileId, now, now.AddSeconds(request.DurationSeconds), now.AddMinutes(2), SessionMode.SimpleTimer, [])); }
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
