using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
using FocusGuard.Sync;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostLocalSettingsOverridesTests
{
    [Fact]
    public async Task BridgeReviewCanCancelAcceptAndRejectAChangedDraftWithoutNetworkWrites()
    {
        using var fixture = await Fixture.CreateAsync();
        using var lifetime = new DocumentLifetime();
        var document = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-local-overrides"),
            new VerifiedNativeHostAuthorization(), lifetime, settingsStore: fixture.Preferences,
            accountService: fixture.Service);
        LocalSettingsOverrides? selection = null;
        fixture.Service.SetSettingsLocalOverridesPresenter((review, _) =>
        {
            Assert.Equal(4, review.Confirmed.Revision);
            return Task.FromResult(selection);
        });

        var before = await fixture.Session.Outbox.LoadAsync();
        selection = null;
        await InvokeAndWaitAsync(bridge, document.Token, fixture.Service);
        Assert.Equal(before, await fixture.Session.Outbox.LoadAsync());
        Assert.Equal(0, fixture.Transport.ReadCount);
        Assert.Equal(0, fixture.Transport.PublishCount);

        selection = new LocalSettingsOverrides(Theme: "light", DefaultDurationMinutes: 90);
        await InvokeAndWaitAsync(bridge, document.Token, fixture.Service);
        var accepted = await fixture.Session.Outbox.LoadAsync();
        Assert.Equal(selection, accepted!.LocalOverrides);
        Assert.Null(accepted.Draft);
        Assert.Null(accepted.InFlight);
        Assert.True(accepted.SyncConsentEnabled);
        Assert.Equal(AppTheme.Light, fixture.Preferences.Load().Theme);
        Assert.Equal(90, fixture.Preferences.Load().LastDurationMinutes);
        Assert.Equal(0, fixture.Transport.ReadCount);
        Assert.Equal(0, fixture.Transport.PublishCount);

        fixture.Service.SetSettingsLocalOverridesPresenter(async (review, token) =>
        {
            var latest = await fixture.Session.Outbox.UpdateAsync(state =>
                state!.Edit(state.Confirmed.EffectiveValues with { UiLanguage = "en" }), token);
            Assert.NotEqual(review.Draft, latest.Draft);
            return new LocalSettingsOverrides(Theme: "dark");
        });
        await InvokeAndWaitAsync(bridge, document.Token, fixture.Service);
        var staleResult = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(document.Token, "account.get", "{}"), CancellationToken.None);
        var staleSnapshot = JsonSerializer.Deserialize<BridgeAccountResponse>(staleResult!, JsonOptions)!.Result;
        Assert.Equal("AccountSettingsConflict", staleSnapshot.Error);
        Assert.Equal("Pending", staleSnapshot.SettingsSyncStatus);
        Assert.Equal(new GlobalSettings(50, "dark", "en"), (await fixture.Session.Outbox.LoadAsync())!.Draft);
        Assert.Equal(selection, (await fixture.Session.Outbox.LoadAsync())!.LocalOverrides);
        Assert.Equal(0, fixture.Transport.ReadCount);
        Assert.Equal(0, fixture.Transport.PublishCount);

        var resumedService = fixture.NewService();
        var persisted = await fixture.Session.Outbox.LoadAsync();
        SetPrivateField(resumedService, "settingsSyncSession", fixture.Session);
        SetPrivateField(resumedService, "settingsSyncEnabled", true);
        var reporter = typeof(HostAccountService).GetMethod("ReportSettingsSyncStateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)reporter.Invoke(resumedService, [fixture.Session,
            new SettingsRetryResult(SettingsRetryStatus.Pending), persisted, CancellationToken.None])!;
        var restoredSummary = await resumedService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
        Assert.Equal(new HostSettingsLocalOverrideSummary(true, false, true), restoredSummary.SettingsSyncLocalOverrides);
        resumedService.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string Request(string token, string method, string parameters) =>
        $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{token}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private static async Task InvokeAndWaitAsync(NativeBridgeDispatcher bridge, string token, HostAccountService service)
    {
        var json = await bridge.HandleAsync("https://focusguard.app/index.html",
            Request(token, "account.settings.sync.overrides", "{}"), CancellationToken.None);
        Assert.NotNull(json);
        var queued = JsonSerializer.Deserialize<BridgeAccountResponse>(json!, JsonOptions)!;
        Assert.Null(queued.Error);
        Assert.True(queued.Result.SettingsSyncBusy);
        await GetPrivateTask(service, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static Task GetPrivateTask(object instance, string name) =>
        (Task)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private sealed class Fixture : IDisposable
    {
        private readonly string root;
        private readonly DeviceIdentity device;
        private readonly byte[] contentKey;
        private readonly WindowsDeviceVault deviceVault;
        private readonly DeviceAccountContext context;
        private readonly MembershipDocument membership;
        private readonly string deviceSession;
        private readonly IdentityConfiguration configuration;
        private readonly string syncRoot;
        private bool disposed;

        public DesktopSettingsStore Preferences { get; }
        public CountingTransport Transport { get; }
        public HostSettingsSyncSession Session { get; }
        public HostAccountService Service { get; }

        private Fixture(string root, DeviceIdentity device, byte[] contentKey, WindowsDeviceVault deviceVault,
            DeviceAccountContext context, MembershipDocument membership, string deviceSession,
            IdentityConfiguration configuration, string syncRoot, DesktopSettingsStore preferences,
            CountingTransport transport, HostSettingsSyncSession session, HostAccountService service)
        {
            this.root = root; this.device = device; this.contentKey = contentKey; this.deviceVault = deviceVault;
            this.context = context; this.membership = membership; this.deviceSession = deviceSession;
            this.configuration = configuration; this.syncRoot = syncRoot; Preferences = preferences;
            Transport = transport; Session = session; Service = service;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusGuard-LocalOverrides-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var preferences = new DesktopSettingsStore(Path.Combine(root, "prefs", "desktop-settings.json"));
            preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
                ReduceMotion: true, EnforcementEnabled: true));
            var device = DeviceIdentity.Create();
            var binding = DeviceEncoding.Encode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root)));
            var context = new DeviceAccountContext("https://identity.example.invalid/realms/test", binding,
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
                device.Public.EncryptionPublicKey, "Active",
                ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
            var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", context.AccountId, context.GenerationId,
                0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
                device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
            var canonical = MembershipProtocol.CanonicalMembership(unsigned);
            MembershipDocument membership;
            try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
            finally { CryptographicOperations.ZeroMemory(canonical); }
            using var recovery = RecoveryKit.Generate();
            var contentKey = RandomNumberGenerator.GetBytes(32);
            var sessionBytes = RandomNumberGenerator.GetBytes(32);
            string deviceSession;
            try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
            finally { CryptographicOperations.ZeroMemory(sessionBytes); }
            var syncRoot = Path.Combine(root, "sync");
            var deviceVault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await deviceVault.SaveAsync(device, recovery, context, contentKey, 1,
                MembershipProtocol.Digest(membership), CancellationToken.None, deviceSession);
            var loaded = await deviceVault.LoadForAccountAsync(context.Issuer, context.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("Test device state failed to load.");
            var transport = new CountingTransport();
            var session = HostSettingsSyncSession.CreateForTesting(context.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot);
            await session.Outbox.SaveAsync(new(new(4, GlobalSettings.Default), SyncConsentEnabled: true));
            var configuration = new IdentityConfiguration(new Uri(context.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), LocalTest: true,
                ApiOrigin: new Uri("https://api.example.invalid/"));
            var service = CreateService(root, deviceVault, context, membership, deviceSession,
                configuration, syncRoot, preferences, session);
            return new(root, device, contentKey, deviceVault, context, membership, deviceSession,
                configuration, syncRoot, preferences, transport, session, service);
        }

        public HostAccountService NewService() => CreateService(root, deviceVault, context, membership,
            deviceSession, configuration, syncRoot, Preferences, Session);

        private static HostAccountService CreateService(string root, WindowsDeviceVault deviceVault,
            DeviceAccountContext context, MembershipDocument membership, string deviceSession,
            IdentityConfiguration configuration, string syncRoot, DesktopSettingsStore preferences,
            HostSettingsSyncSession session)
        {
            var identity = new IdentitySession(configuration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Local test", "local@example.invalid", true));
            var service = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                configuration, Path.Combine(root, "devices"), preferences, settingsSyncTestRoot: syncRoot);
            SetPrivateField(service, "settingsSyncSession", session);
            SetPrivateField(service, "settingsSyncEnabled", true);
            SetPrivateField(service, "settingsSyncStatus", "Enabled");
            SetPrivateField(service, "deviceLinked", true);
            return service;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Service.Dispose();
            Transport.Dispose();
            device.Dispose();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CountingTransport : ISettingsDocumentTransport, IDisposable
    {
        public int ReadCount { get; private set; }
        public int PublishCount { get; private set; }
        public Task<byte[]?> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); ReadCount++; return Task.FromResult<byte[]?>(null); }
        public Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token)
        { token.ThrowIfCancellationRequested(); PublishCount++; return Task.FromResult(SettingsPublishResult.Stored); }
        public void Dispose() { }
    }
}
