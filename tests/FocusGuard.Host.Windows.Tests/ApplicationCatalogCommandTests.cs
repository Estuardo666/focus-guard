using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Platform;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Platform.Windows;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Tests;

public sealed class ApplicationCatalogCommandTests
{
    [Fact]
    public async Task CatalogPagesFilterDuplicatesAndUnsafePathsAndBoundIcons()
    {
        using var data = new Data(); using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var catalog = new Catalog
        {
            Items = [.. Enumerable.Range(0, 9).Select(i => new InstalledApplication($"App {i}", $@"C:\Apps\app{i}.exe")),
            new("Duplicate", @"C:\Apps\APP0.EXE"), new("Network", @"\\server\share\app.exe"), new("Script", @"C:\Apps\app.cmd")]
        };
        var icons = new Icons();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(), lifetime, profileStore: data.Store, applicationCatalog: catalog, iconProvider: icons);
        var response = await Call(bridge, lease.Token, "applications.list", new { query = "", offset = 0 });
        using var json = JsonDocument.Parse(response!); var result = json.RootElement.GetProperty("result");
        Assert.Equal(9, result.GetProperty("total").GetInt32()); Assert.Equal(6, result.GetProperty("items").GetArrayLength());
        var icon = result.GetProperty("items")[0].GetProperty("icon");
        Assert.Equal(32, icon.GetProperty("width").GetInt32()); Assert.Equal(4096, Convert.FromBase64String(icon.GetProperty("bgra").GetString()!).Length);
        Assert.Equal(6, icons.Paths.Count); Assert.DoesNotContain(icons.Paths, path => path.StartsWith(@"\\", StringComparison.Ordinal));
        await Call(bridge, lease.Token, "applications.list", new { query = "app 1", offset = 0 });
        Assert.Equal(1, catalog.Calls); Assert.Equal(6, icons.Paths.Count);
        await Call(bridge, lease.Token, "applications.list", new { query = "app 1", offset = 0, refresh = true });
        Assert.Equal(2, catalog.Calls);
    }

    [Fact]
    public async Task AddingCatalogEntryUsesOnlyCachedExactPathAndExplicitAction()
    {
        using var data = new Data(); var profile = new StoredProfile(ProfileId.New(), "Work", [], []); await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var catalog = new Catalog { Items = [new("Known", @"C:\Trusted\work.exe")] };
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store, applicationCatalog: catalog);
        var page = await Call(bridge, lease.Token, "applications.list", new { query = "", offset = 0 });
        using var json = JsonDocument.Parse(page!); var catalogId = json.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Null(Error(await Call(bridge, lease.Token, "profile.application.addFromCatalog", new { profileId = profile.Id.Value.ToString("N"), revision = ProfileRevision.Compute(profile), operationId = Guid.NewGuid().ToString("N"), action = "Allow", catalogId })));
        var rule = Assert.Single((await data.Store.LoadProfileAsync(profile.Id))!.ApplicationRules);
        Assert.Equal(ApplicationMatchKind.ExactPath, rule.MatchKind); Assert.Equal(RuleAction.Allow, rule.Action); Assert.Equal(@"C:\Trusted\work.exe", rule.Value);
        var current = (await data.Store.LoadProfileAsync(profile.Id))!;
        Assert.Equal("RuleAlreadyExists", Error(await Call(bridge, lease.Token, "profile.application.addFromCatalog", new
        { profileId = profile.Id.Value.ToString("N"), revision = ProfileRevision.Compute(current), operationId = Guid.NewGuid().ToString("N"), action = "Block", catalogId })));
        Assert.Single((await data.Store.LoadProfileAsync(profile.Id))!.ApplicationRules);
    }

    [Fact]
    public async Task NewDocumentCannotUseOldCatalogIdAndCallerCannotSupplyPath()
    {
        using var data = new Data(); var profile = new StoredProfile(ProfileId.New(), "Work", [], []); await data.Store.SaveProfileAsync(profile);
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new Client(), new VerifiedNativeHostAuthorization(true), lifetime, profileStore: data.Store,
            applicationCatalog: new Catalog { Items = [new("Known", @"C:\Known\app.exe")] });
        var page = await Call(bridge, lease.Token, "applications.list", new { query = "", offset = 0 });
        using var json = JsonDocument.Parse(page!); var id = json.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("id").GetString();
        var current = lifetime.BeginDocument();
        var parameters = new { profileId = profile.Id.Value.ToString("N"), revision = ProfileRevision.Compute(profile), operationId = Guid.NewGuid().ToString("N"), action = "Block", catalogId = id };
        Assert.Equal("CatalogChanged", Error(await Call(bridge, current.Token, "profile.application.addFromCatalog", parameters)));
        var invalid = JsonSerializer.Serialize(new { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = current.Token, method = "profile.application.addFromCatalog", @params = new { parameters.profileId, parameters.revision, parameters.operationId, parameters.action, parameters.catalogId, path = @"C:\Other\app.exe" } });
        Assert.False(new BridgeRequestParser().TryParse(invalid, out _, out _)); Assert.Empty((await data.Store.LoadProfileAsync(profile.Id))!.ApplicationRules);
    }

    [Fact]
    public async Task UnverifiedHostDoesNotCallDiscoveryOrIcons()
    {
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument(); var catalog = new Catalog(); var icons = new Icons();
        var bridge = new NativeBridgeDispatcher(new Client(), new Phase4AuthorizationNotIntegrated(), lifetime, applicationCatalog: catalog, iconProvider: icons);
        Assert.Equal("AuthorizationUnavailable", Error(await Call(bridge, lease.Token, "applications.list", new { query = "", offset = 0 })));
        Assert.Equal(0, catalog.Calls); Assert.Empty(icons.Paths);
    }

    [Fact]
    public async Task ActualWindowsDiscoveryIsReadOnlyAndReturnsExistingExeTargets()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var apps = await new WindowsApplicationCatalog().ListAsync(timeout.Token);
        Assert.InRange(apps.Count, 0, 2000);
        Assert.All(apps, app => { Assert.True(Path.IsPathFullyQualified(app.Path)); Assert.True(File.Exists(app.Path)); Assert.Equal(".exe", Path.GetExtension(app.Path), ignoreCase: true); });
        var iconPresent = false;
        if (apps.Count > 0)
        {
            var icon = new WindowsApplicationIconProvider().Read(apps[0].Path);
            if (icon is not null) { Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length); iconPresent = true; }
        }
        var evidence = Path.Combine(Directory.GetCurrentDirectory(), "actual-windows-catalog-fg190.json");
        await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new { Count = apps.Count, IconPresent = iconPresent, ReadOnly = true }), timeout.Token);
    }

    private static ValueTask<string?> Call(NativeBridgeDispatcher bridge, string token, string method, object parameters) => bridge.HandleAsync("https://focusguard.app/index.html", JsonSerializer.Serialize(new { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters }), CancellationToken.None);
    private static string? Error(string? text) { using var json = JsonDocument.Parse(text!); return json.RootElement.GetProperty("error").GetString(); }
    private sealed class Catalog : IApplicationCatalog
    { public IReadOnlyList<InstalledApplication> Items { get; init; } = []; public int Calls { get; private set; } public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token) { Calls++; return Task.FromResult(Items); } }
    private sealed class Icons : IApplicationIconProvider
    { public List<string> Paths { get; } = []; public ApplicationIconPixels Read(string path) { Paths.Add(path); return new(64, 64, new byte[64 * 64 * 4]); } }
    private sealed class Data : IDisposable
    { private readonly string _directory = Path.Combine(Path.GetTempPath(), "fg190-" + Guid.NewGuid()); public SqliteProfileStore Store { get; } public Data() => Store = new(Path.Combine(_directory, "profiles.db")); public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } }
    private sealed class Client : IFocusControlClient
    {
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => ValueTask.FromResult(new StatusResponse(true, true, "Inactive", 0, null, null, null, null, false, false));
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
