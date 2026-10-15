using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Sync;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class AccountBridgeTests
{
    private const string TrustedSource = "https://focusguard.app/index.html";

    [Fact]
    public async Task AccountReadIsSeparateFromLocalControlAuthorizationAndContainsNoTokens()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, false, false, null, null));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-account-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var response = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.get", "{}"), CancellationToken.None);

        var parsed = JsonSerializer.Deserialize<BridgeAccountResponse>(response!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(parsed.Error);
        Assert.True(parsed.Result.SignedIn);
        Assert.False(parsed.Result.DeviceLinked);
        Assert.Equal(new[] { HostAccountAction.Get }, account.Actions);
        Assert.DoesNotContain("accessToken", response!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", response!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identityToken", response!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", response!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountActionsRejectUnknownParametersAndUntrustedDocuments()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(false, false, false, false, false,
            null, null, false, false, false, null, null));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-account-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var extra = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.login", "{\"unexpected\":true}"), CancellationToken.None);
        var untrusted = await bridge.HandleAsync("https://example.invalid/index.html",
            Request(lease.Token, "account.login", "{}"), CancellationToken.None);

        Assert.Null(extra);
        Assert.Null(untrusted);
        Assert.Empty(account.Actions);
    }

    [Fact]
    public async Task DeviceLinkIsAnExplicitNativeActionWithNoWebSuppliedSecrets()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, false, true, null, null));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-account-link-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var response = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.device.link", "{}"), CancellationToken.None);

        var parsed = JsonSerializer.Deserialize<BridgeAccountResponse>(response!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(parsed.Result.DeviceLinkPending);
        Assert.Equal(new[] { HostAccountAction.LinkDevice }, account.Actions);
        Assert.DoesNotContain("recoveryKit", response!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", response!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairingRequestAndApprovalUseOnlyPublicValidatedPairingData()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        const string pairingId = "f8d742d5-2c86-42f2-9657-66a7fbddfc6e";
        const string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var summary = new HostPairingSummary(pairingId, "51a42b79-52b0-4ccb-9dca-b99c49da2f10",
            "Windows", "0.1.150", fingerprint, DateTimeOffset.UtcNow.AddMinutes(4));
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, false, false, null, null,
            PairingBusy: true, PairingId: pairingId, PairingFingerprint: fingerprint,
            PendingPairings: [summary], AccountHasLinkedDevice: true));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-account-pairing-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var requestResponse = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.device.pair.request", "{}"), CancellationToken.None);
        var requestResult = JsonSerializer.Deserialize<BridgeAccountResponse>(requestResponse!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(requestResult.Error);
        Assert.True(requestResult.Result.PairingBusy);
        Assert.Equal(pairingId, requestResult.Result.PairingId);
        Assert.Equal(fingerprint, requestResult.Result.PairingFingerprint);
        Assert.True(requestResult.Result.AccountHasLinkedDevice);
        Assert.Equal(summary, Assert.Single(requestResult.Result.PendingPairings!));

        var approveResponse = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.device.pair.approve",
                $"{{\"pairingId\":\"{pairingId}\",\"fingerprint\":\"{fingerprint}\"}}"),
            CancellationToken.None);
        Assert.NotNull(approveResponse);
        Assert.Equal(new[] { HostAccountAction.RequestPairing, HostAccountAction.ApprovePairing }, account.Actions);
        Assert.Equal(pairingId, account.LastPairingId);
        Assert.Equal(fingerprint, account.LastFingerprint);
        Assert.DoesNotContain("signingPublicKey", requestResponse!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encryptionPublicKey", requestResponse!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deviceSession", requestResponse!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("account.device.pair.approve", "{}")]
    [InlineData("account.device.pair.approve", "{\"pairingId\":\"f8d742d5-2c86-42f2-9657-66a7fbddfc6e\",\"fingerprint\":\"short\"}")]
    [InlineData("account.device.pair.complete", "{\"recoveryKit\":\"secret\"}")]
    public async Task PairingBridgeRejectsMalformedOrSecretBearingParameters(string method, string parameters)
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, false, false, null, null));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-account-pairing-invalid-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var response = await bridge.HandleAsync(TrustedSource, Request(lease.Token, method, parameters), CancellationToken.None);

        Assert.Null(response);
        Assert.Empty(account.Actions);
    }

    [Fact]
    public async Task SettingsTransferBridgeAcceptsNoWebSuppliedValuesAndReturnsOnlyStatus()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, true, false, null, null,
            SettingsTransferBusy: true));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-transfer-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var share = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.share", "{}"), CancellationToken.None);
        var import = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.import", "{}"), CancellationToken.None);
        var invalid = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.import", "{\"theme\":\"Light\"}"), CancellationToken.None);

        Assert.NotNull(share);
        Assert.NotNull(import);
        Assert.Null(invalid);
        Assert.Equal(new[] { HostAccountAction.ShareSettings, HostAccountAction.ImportSettings }, account.Actions);
        Assert.Contains("settingsTransferBusy", share!, StringComparison.Ordinal);
        Assert.DoesNotContain("theme\":\"Light", share!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortableSettingsBridgeAcceptsOnlyNativeCommandNames()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(false, false, false, false, false,
            null, null, false, false, false, null, null));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-portable-settings-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var export = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.portable.export", "{}"), CancellationToken.None);
        var import = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.portable.import", "{}"), CancellationToken.None);
        var injected = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.portable.import", "{\"path\":\"C:\\\\private.json\"}"), CancellationToken.None);

        Assert.NotNull(export);
        Assert.NotNull(import);
        Assert.Null(injected);
        Assert.Equal(new[] { HostAccountAction.ExportPortableSettings, HostAccountAction.ImportPortableSettings }, account.Actions);
        Assert.DoesNotContain("C:\\\\private.json", export!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomaticSettingsSyncActionsRequireExactEmptyParametersAndExposeOnlyConsentStatus()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, true, false, null, null,
            SettingsSyncEnabled: true, SettingsSyncStatus: "Enabled", SettingsSyncRecoveryAvailable: true,
            SettingsSyncLocalOverrides: new(true, false, true)));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-sync-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var enable = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.enable", "{}"), CancellationToken.None);
        var disable = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.disable", "{}"), CancellationToken.None);
        var recover = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.recover", "{}"), CancellationToken.None);
        var reset = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.reset", "{}"), CancellationToken.None);
        var overrides = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.overrides", "{}"), CancellationToken.None);
        var invalid = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.enable", "{\"accountId\":\"attacker-value\"}"), CancellationToken.None);
        var invalidRecovery = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.recover", "{\"path\":\"C:\\\\sensitive\"}"), CancellationToken.None);
        var invalidReset = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.reset", "{\"theme\":\"light\"}"), CancellationToken.None);
        var invalidOverrides = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.overrides", "{\"theme\":\"light\"}"), CancellationToken.None);

        var response = JsonSerializer.Deserialize<BridgeAccountResponse>(enable!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(response.Error);
        Assert.True(response.Result.SettingsSyncEnabled);
        Assert.Equal("Enabled", response.Result.SettingsSyncStatus);
        Assert.Equal(new(true, false, true), response.Result.SettingsSyncLocalOverrides);
        Assert.NotNull(disable);
        Assert.NotNull(recover);
        Assert.NotNull(reset);
        Assert.NotNull(overrides);
        Assert.Null(invalid);
        Assert.Null(invalidRecovery);
        Assert.Null(invalidReset);
        Assert.Null(invalidOverrides);
        Assert.Equal(new[] { HostAccountAction.EnableSettingsSync, HostAccountAction.DisableSettingsSync,
            HostAccountAction.RecoverSettingsSyncCheckpoint, HostAccountAction.ResetSettingsSync,
            HostAccountAction.UpdateLocalSettingsOverrides }, account.Actions);
        Assert.DoesNotContain("attacker-value", enable!, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", enable!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsConflictResolutionBridgeAcceptsOnlyTypedPerFieldChoices()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var conflict = new HostSettingsSyncConflictReview(4, ["theme"],
            new GlobalSettings(50, "light", "es"), new GlobalSettings(50, "dark", "es"), false);
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, true, false, null, null,
            SettingsSyncEnabled: true, SettingsSyncStatus: "Conflict", SettingsSyncConflict: conflict));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-conflict-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var accepted = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve",
                "{\"revision\":4,\"choices\":[{\"field\":\"theme\",\"source\":\"otherDevices\"}]}"),
            CancellationToken.None);
        var extraValue = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve",
                "{\"revision\":4,\"choices\":[{\"field\":\"theme\",\"source\":\"otherDevices\",\"value\":\"light\"}]}"),
            CancellationToken.None);
        var unknownField = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve",
                "{\"revision\":4,\"choices\":[{\"field\":\"rules\",\"source\":\"otherDevices\"}]}"),
            CancellationToken.None);

        Assert.NotNull(accepted);
        var response = JsonSerializer.Deserialize<BridgeAccountResponse>(accepted!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Conflict", response.Result.SettingsSyncStatus);
        Assert.Equal(["theme"], response.Result.SettingsSyncConflict!.Fields);
        Assert.Equal(new[] { HostAccountAction.ResolveSettingsSyncConflict }, account.Actions);
        Assert.NotNull(account.LastSettingsSyncResolution);
        Assert.Equal(4, account.LastSettingsSyncResolution.Revision);
        Assert.Equal(new HostSettingsSyncConflictChoice(HostSettingsSyncConflictField.Theme, HostSettingsSyncConflictSource.OtherDevices),
            Assert.Single(account.LastSettingsSyncResolution.Choices));
        Assert.Null(extraValue);
        Assert.Null(unknownField);
        Assert.DoesNotContain("accessToken", accepted!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetConflictBridgeAcceptsOnlyItsTwoTypedRecoveryChoices()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var review = new HostSettingsSyncConflictReview(5, ["theme", "language", "duration"],
            new GlobalSettings(35, "light", "en"), GlobalSettings.Default, true);
        var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
            "FocusGuard User", "user@example.invalid", true, true, false, null, null,
            SettingsSyncEnabled: true, SettingsSyncStatus: "Conflict", SettingsSyncConflict: review));
        var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-reset-test"),
            new Phase4AuthorizationNotIntegrated(), lifetime, accountService: account);

        var accepted = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve-reset",
                "{\"revision\":5,\"choice\":\"thisDeviceReset\"}"), CancellationToken.None);
        var unknownChoice = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve-reset",
                "{\"revision\":5,\"choice\":\"deleteEverything\"}"), CancellationToken.None);
        var extraValue = await bridge.HandleAsync(TrustedSource,
            Request(lease.Token, "account.settings.sync.resolve-reset",
                "{\"revision\":5,\"choice\":\"otherDevices\",\"theme\":\"dark\"}"), CancellationToken.None);

        Assert.NotNull(accepted);
        Assert.Equal(new[] { HostAccountAction.ResolveSettingsSyncResetConflict }, account.Actions);
        Assert.Equal(new HostSettingsSyncResetResolution(5, HostSettingsSyncResetSource.ThisDeviceReset),
            account.LastSettingsSyncResetResolution);
        Assert.Null(unknownChoice);
        Assert.Null(extraValue);
    }

    [Fact]
    public async Task LocalPreferenceEditIsAlsoJournaledWhenNativeAccountServiceHasConsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-settings-sync-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DesktopSettingsStore(Path.Combine(root, "desktop-settings.json"));
            _ = store.Load();
            store.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
                ReduceMotion: true, EnforcementEnabled: true));
            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var account = new RecordingAccount(new HostAccountSnapshot(true, false, false, false, false,
                "FocusGuard User", "user@example.invalid", true, true, true, null, null), syncSharedEdit: true);
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-pref-sync"),
                new VerifiedNativeHostAuthorization(), lifetime, settingsStore: store, accountService: account);
            const string parameters = "{\"uiLanguage\":\"en\",\"theme\":\"Light\",\"reduceMotion\":true,\"sessionEnd\":\"Window\",\"cyclePhaseNotifications\":true,\"enforcementEnabled\":true,\"lastDurationMinutes\":35,\"lastSessionMode\":\"SimpleTimer\",\"lastCyclePresetId\":null}";

            var response = await bridge.HandleAsync(TrustedSource,
                Request(lease.Token, "preferences.set", parameters), CancellationToken.None);

            Assert.NotNull(response);
            Assert.True(account.SettingsEditCalled);
            var updated = store.Load();
            Assert.Equal(AppTheme.Light, updated.Theme);
            Assert.Equal("en", updated.UiLanguage);
            Assert.Equal(35, updated.LastDurationMinutes);
            Assert.True(updated.ReduceMotion);
            Assert.True(updated.EnforcementEnabled);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Request(string token, string method, string parameters) =>
        $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{token}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private sealed class RecordingAccount(HostAccountSnapshot snapshot, bool syncSharedEdit = false) : IHostAccountService
    {
        public List<HostAccountAction> Actions { get; } = [];
        public string? LastPairingId { get; private set; }
        public string? LastFingerprint { get; private set; }
        public HostSettingsSyncConflictResolution? LastSettingsSyncResolution { get; private set; }
        public HostSettingsSyncResetResolution? LastSettingsSyncResetResolution { get; private set; }
        public bool SettingsEditCalled { get; private set; }
        public Task<HostAccountSnapshot> ExecuteAsync(HostAccountAction action, CancellationToken cancellationToken,
            string? pairingId = null, string? fingerprint = null,
            HostSettingsSyncConflictResolution? settingsSyncResolution = null,
            HostSettingsSyncResetResolution? settingsSyncResetResolution = null)
        {
            Actions.Add(action);
            LastPairingId = pairingId;
            LastFingerprint = fingerprint;
            LastSettingsSyncResolution = settingsSyncResolution;
            LastSettingsSyncResetResolution = settingsSyncResetResolution;
            return Task.FromResult(snapshot);
        }
        public void SetRecoveryKitPresenter(Func<string, CancellationToken, Task<bool>> presenter) { }
        public void SetPairingApprovalPresenter(Func<HostPendingPairingSummary, CancellationToken, Task<bool>> presenter) { }
        public void SetSettingsTransferPresenters(
            Func<DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> sharePresenter,
            Func<SharedDesktopSettings, DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> importPresenter)
        { }
        public void SetPortableSettingsPresenters(
            Func<DesktopSettings, CancellationToken, Task<PortableSettingsFileSelection?>> exportPresenter,
            Func<CancellationToken, Task<string?>> importFilePicker,
            Func<SettingsImportPreview, CancellationToken, Task<SettingsFields?>> importConfirmationPresenter)
        { }
        public void SetSettingsSyncConsentPresenter(Func<CancellationToken, Task<bool>> presenter) { }
        public void SetSettingsSyncResetPresenter(Func<HostSettingsSyncResetPreview, CancellationToken, Task<bool>> presenter) { }
        public void SetSettingsSyncRecoveryPresenter(Func<CancellationToken, Task<bool>> presenter) { }
        public bool NotifySettingsSyncConnectivityRestored() => false;
        public Task<bool> TrySyncSharedSettingsEditAsync(DesktopSettings previous, DesktopSettings proposed,
            CancellationToken cancellationToken)
        {
            SettingsEditCalled = true;
            return Task.FromResult(syncSharedEdit);
        }
        public Task<bool> TryImportReviewedSettingsAsync(SettingsImportPreview preview, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public void Dispose() { }
    }
}
