using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Platform;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class SavedRulePresentationTests
{
    [Fact]
    public async Task ProfileDetailsDoNotWaitForOptionalApplicationDiscovery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg-profile-detail-discovery-" + Guid.NewGuid());
        try
        {
            var store = new SqliteProfileStore(Path.Combine(directory, "profiles.db"));
            var profile = new StoredProfile(ProfileId.New(), "Work", [
                new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, @"C:\Apps\Known.exe")], []);
            await store.SaveProfileAsync(profile);
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var catalog = new SlowCatalog();
            var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(), lifetime,
                profileStore: store, applicationCatalog: catalog);

            var response = await bridge.HandleAsync("https://focusguard.app/index.html", JsonSerializer.Serialize(new
            {
                version = 1,
                requestId = Guid.NewGuid().ToString("N"),
                documentToken = lease.Token,
                method = "profile.get",
                @params = new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 }
            }), CancellationToken.None);

            using var json = JsonDocument.Parse(response!);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
            Assert.Equal(0, catalog.Calls);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").GetProperty("applications")[0]
                .GetProperty("presentation").ValueKind);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresentationIsTruthfulAndNeverReadsUnlistedRulePaths(bool discoveryFails)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg192-" + Guid.NewGuid());
        try
        {
            var store = new SqliteProfileStore(Path.Combine(directory, "profiles.db"));
            var rules = new[] {
                new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, @"C:\Apps\Known.exe"),
                new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "unique.exe"),
                new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "shared.exe"),
                new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.ExactPath, @"C:\Unlisted\secret.exe"),
                new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.Folder, @"C:\Folder"),
                new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.Sha256, new string('A', 64)) };
            var profile = new StoredProfile(ProfileId.New(), "Work", rules, []); await store.SaveProfileAsync(profile);
            using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument(); var icons = new Icons();
            var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(), lifetime, profileStore: store,
                applicationCatalog: new Catalog { Fail = discoveryFails }, iconProvider: icons);
            var catalogResponse = await bridge.HandleAsync("https://focusguard.app/index.html", JsonSerializer.Serialize(new
            {
                version = 1,
                requestId = Guid.NewGuid().ToString("N"),
                documentToken = lease.Token,
                method = "applications.list",
                @params = new { query = "__no_saved_rule_test_match__", offset = 0, refresh = false }
            }), CancellationToken.None);
            using (var catalogJson = JsonDocument.Parse(catalogResponse!))
                Assert.Equal(discoveryFails ? "ServiceUnavailable" : null,
                    catalogJson.RootElement.GetProperty("error").ValueKind == JsonValueKind.Null
                        ? null : catalogJson.RootElement.GetProperty("error").GetString());
            var response = await bridge.HandleAsync("https://focusguard.app/index.html", JsonSerializer.Serialize(new { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = lease.Token, method = "profile.get", @params = new { profileId = profile.Id.Value.ToString("N"), applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0 } }), CancellationToken.None);
            using var json = JsonDocument.Parse(response!); var result = json.RootElement.GetProperty("result");
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("error").ValueKind);
            var entries = result.GetProperty("applications").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid());
            string? Resolution(int i)
            {
                var presentation = entries[rules[i].Id].GetProperty("presentation");
                return presentation.ValueKind == JsonValueKind.Null ? null : presentation.GetProperty("resolution").GetString();
            }
            Assert.Equal(discoveryFails ? null : "ExactMatch", Resolution(0));
            Assert.Equal(discoveryFails ? null : "FileNameMatch", Resolution(1));
            Assert.Equal(discoveryFails ? null : "Ambiguous", Resolution(2));
            Assert.Equal(discoveryFails ? null : "Unresolved", Resolution(3)); Assert.Equal("Folder", Resolution(4)); Assert.Equal("Hash", Resolution(5));
            if (!discoveryFails)
            {
                Assert.Equal("Known app", entries[rules[0].Id].GetProperty("presentation").GetProperty("name").GetString());
                Assert.Equal(@"C:\Tools\unique.exe", entries[rules[1].Id].GetProperty("presentation").GetProperty("path").GetString());
            }
            if (!discoveryFails) Assert.Equal(JsonValueKind.Null, entries[rules[2].Id].GetProperty("presentation").GetProperty("path").ValueKind);
            Assert.Equal(discoveryFails ? 0 : 2, icons.Paths.Count); Assert.DoesNotContain(@"C:\Unlisted\secret.exe", icons.Paths); Assert.DoesNotContain(@"C:\Folder", icons.Paths);
            Assert.Equal(ProfileRevision.Compute(profile), result.GetProperty("revision").GetString());
            Assert.Equal(ProfileRevision.Compute(profile), ProfileRevision.Compute((await store.LoadProfileAsync(profile.Id))!));
            foreach (var rule in rules) { Assert.Equal(rule.Value, entries[rule.Id].GetProperty("value").GetString()); Assert.Equal(rule.MatchKind.ToString(), entries[rule.Id].GetProperty("matchKind").GetString()); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Catalog : IApplicationCatalog
    { public bool Fail { get; init; } public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token) { if (Fail) throw new IOException(); return Task.FromResult<IReadOnlyList<InstalledApplication>>([new("Known app", @"C:\Apps\Known.exe"), new("Unique app", @"C:\Tools\unique.exe"), new("Shared first", @"C:\First\shared.exe"), new("Shared second", @"C:\Second\shared.exe")]); } }
    private sealed class SlowCatalog : IApplicationCatalog
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public async Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return [];
        }
    }
    private sealed class Icons : IApplicationIconProvider
    { public List<string> Paths { get; } = []; public ApplicationIconPixels Read(string path) { Paths.Add(path); return new(32, 32, new byte[4096]); } }
    private sealed class Client : IFocusControlClient
    {
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
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
