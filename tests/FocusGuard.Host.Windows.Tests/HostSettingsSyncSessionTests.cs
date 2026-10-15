using System.Security.Cryptography;
using System.Text;
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

public sealed class HostSettingsSyncSessionTests
{
    [Fact]
    public async Task ConstructionIsInertAndExplicitOptInCreatesEncryptedBaselineAndConsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 45));

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("settings-sync-host-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var membershipDigest = MembershipProtocol.Digest(membership);
        var transport = new MemorySettingsTransport();

        try
        {
            var deviceVault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await deviceVault.SaveAsync(device, null, account, contentKey, 1, membershipDigest,
                CancellationToken.None, deviceSession);
            var loaded = await deviceVault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            var syncSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot);

            await using (syncSession)
            {
                Assert.False(Directory.Exists(syncRoot));
                Assert.False(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                    localSyncRoot: syncRoot));
                Assert.Equal(0, transport.ReadCount);
                Assert.Equal(0, transport.PublishCount);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    syncSession.EnableAsync(ownerConfirmed: false));
                Assert.False(Directory.Exists(syncRoot));
                Assert.Equal(0, transport.ReadCount);
                Assert.Equal(0, transport.PublishCount);

                var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                    new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
                var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
                typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                    new AccountState(true, "Test account", "test@example.invalid", true));
                using var lifetime = new DocumentLifetime();
                var lease = lifetime.BeginDocument();
                using var accountService = new HostAccountService(identity,
                    new UserTokenVault(Path.Combine(root, "identity.dpapi")), identityConfiguration,
                    Path.Combine(root, "devices"), preferences);
                SetPrivateField(accountService, "settingsSyncSession", syncSession);
                SetPrivateField(accountService, "deviceLinked", true);
                accountService.SetSettingsSyncConsentPresenter(_ => Task.FromResult(true));
                var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-consent-enable"),
                    new VerifiedNativeHostAuthorization(), lifetime, settingsStore: preferences,
                    accountService: accountService);
                var responseJson = await bridge.HandleAsync("https://focusguard.app/index.html",
                    BridgeRequest(lease.Token, "account.settings.sync.enable", "{}"), CancellationToken.None);
                Assert.NotNull(responseJson);
                var response = JsonSerializer.Deserialize<BridgeAccountResponse>(responseJson!,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Null(response.Error);
                Assert.True(response.Result.SettingsSyncBusy);
                Assert.Equal("Enabling", response.Result.SettingsSyncStatus);
                await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(5));
                var completed = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
                Assert.True(completed.SettingsSyncEnabled);
                Assert.Equal("Enabled", completed.SettingsSyncStatus);
                SetPrivateField(accountService, "settingsSyncSession", null!);

                Assert.True(Directory.Exists(syncRoot));
                Assert.True(transport.ReadCount >= 2);
                Assert.Equal(1, transport.PublishCount);
                Assert.NotNull(transport.Document);
                Assert.DoesNotContain("\"theme\"", Encoding.UTF8.GetString(transport.Document));
                var envelope = JsonSerializer.Deserialize<EncryptedContentDocument>(transport.Document,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal(HostSettingsSyncSession.GlobalSettingsEntityId, envelope.EntityId);
                Assert.NotEqual("baf56f37-89a7-4a98-9a1e-e87bb3504a91", envelope.EntityId);

                var state = await syncSession.Outbox.LoadAsync();
                Assert.NotNull(state);
                Assert.Equal(1, state.Confirmed.Revision);
                Assert.Equal(new GlobalSettings(45, "light", "en"), state.Confirmed.Values);
                Assert.False(state.Pending);
                Assert.Null(state.InFlight);
                Assert.True(state.SyncConsentEnabled);
                Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                    localSyncRoot: syncRoot));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SavedConsentResumesTheSyncLoopInANewSessionWithoutReenabling()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Restart-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-restart-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostSettingsSyncSession? firstSession = null;
        HostSettingsSyncSession? resumedSession = null;
        using var stop = new CancellationTokenSource();

        try
        {
            var deviceVault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await deviceVault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var firstLoaded = await deviceVault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            firstSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                firstLoaded, [membership], preferences, syncRoot);
            await firstSession.EnableAsync(ownerConfirmed: true);
            var initial = await firstSession.Outbox.LoadAsync();
            Assert.NotNull(initial);
            Assert.True(initial.SyncConsentEnabled);
            Assert.Equal(new GlobalSettings(50, "dark", "es"), initial.Confirmed.Values);
            var readsBeforeRestart = transport.ReadCount;
            var publishesBeforeRestart = transport.PublishCount;
            await firstSession.DisposeAsync();
            firstSession = null;

            var resumedLoaded = await deviceVault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The linked device vault did not survive the session restart.");
            resumedSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                resumedLoaded, [membership], preferences, syncRoot);
            transport.BlockReads = true;
            var run = resumedSession.RunAsync(stop.Token);
            await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.Equal(readsBeforeRestart + 1, transport.ReadCount);
            Assert.Equal(publishesBeforeRestart, transport.PublishCount);
            var resumed = await resumedSession.Outbox.LoadAsync();
            Assert.NotNull(resumed);
            Assert.True(resumed.SyncConsentEnabled);
            Assert.Equal(initial.Confirmed, resumed.Confirmed);
            var local = preferences.Load();
            Assert.Equal(AppTheme.Dark, local.Theme);
            Assert.Equal("es", local.UiLanguage);
            Assert.Equal(50, local.LastDurationMinutes);
            Assert.True(local.ReduceMotion);
            Assert.True(local.EnforcementEnabled);
            Assert.False(local.CyclePhaseNotifications);

            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            Assert.True(transport.ReadWasCancelled);
            Assert.True((await resumedSession.Outbox.LoadAsync())!.SyncConsentEnabled);
        }
        finally
        {
            stop.Cancel();
            if (firstSession is not null) await firstSession.DisposeAsync();
            if (resumedSession is not null) await resumedSession.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsSenderBoundToAnotherAccountBeforePreparingLocalStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("settings-sync-binding-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            using var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            var store = new DesktopSettingsStore(Path.Combine(root, "prefs", "desktop-settings.json"));

            Assert.Throws<System.Security.SecurityException>(() => HostSettingsSyncSession.CreateForTesting(
                "another-account-binding", transport, loaded, [membership], store, Path.Combine(root, "sync")));
            Assert.False(Directory.Exists(Path.Combine(root, "sync")));
            Assert.Equal(0, transport.ReadCount);
            Assert.Equal(0, transport.PublishCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostAccountServiceRoutesOnlySharedPreferenceEditsThroughConsentedOutbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Edits-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        var deviceVaultRoot = Path.Combine(root, "devices");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var desktopStore = new DesktopSettingsStore(preferencesPath);
        _ = desktopStore.Load();
        var baseline = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
        desktopStore.SaveVerified(baseline);

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-account-settings-edit-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "linked-device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            var session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], desktopStore, syncRoot);
            await session.EnableAsync(ownerConfirmed: true);

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            var tokenVault = new UserTokenVault(Path.Combine(root, "identity.dpapi"));
            using var accountService = new HostAccountService(identity, tokenVault, identityConfiguration,
                deviceVaultRoot, desktopStore);
            typeof(HostAccountService).GetField("settingsSyncSession",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(accountService, session);

            var proposed = baseline with
            {
                Theme = AppTheme.Light,
                UiLanguage = "en",
                LastDurationMinutes = 45,
                ReduceMotion = false,
                CyclePhaseNotifications = true
            };
            var routed = await accountService.TrySyncSharedSettingsEditAsync(baseline, proposed, CancellationToken.None);

            Assert.True(routed);
            var state = await session.Outbox.LoadAsync();
            Assert.NotNull(state);
            Assert.True(state.SyncConsentEnabled);
            Assert.Equal(new GlobalSettings(45, "light", "en"), state.Draft);
            var local = desktopStore.Load();
            Assert.Equal(proposed.Theme, local.Theme);
            Assert.Equal(proposed.UiLanguage, local.UiLanguage);
            Assert.Equal(proposed.LastDurationMinutes, local.LastDurationMinutes);
            Assert.True(local.ReduceMotion); // The sync adapter projects the shared allowlist only.
            Assert.False(local.CyclePhaseNotifications); // The preferences bridge owns other local changes.
            Assert.True(local.EnforcementEnabled);
            Assert.Equal(1, transport.PublishCount); // Only the explicitly consented initial baseline was sent.
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RevokedSyncPreferenceEditUpdatesOnlyExistingLocalOverridesWithoutNetworkWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-RevokedEdit-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        var deviceVaultRoot = Path.Combine(root, "devices");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        Directory.CreateDirectory(deviceVaultRoot);
        var preferences = new DesktopSettingsStore(preferencesPath);
        var baseline = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
        preferences.SaveVerified(baseline);

        const string subject = "revoked-settings-sync-test-subject";
        var authority = new Uri("https://identity.example.invalid/realms/test");
        var bindingHash = SHA256.HashData(Encoding.UTF8.GetBytes(authority.AbsoluteUri.TrimEnd('/') + "\0" + subject));
        string accountBinding;
        try { accountBinding = DeviceEncoding.Encode(bindingHash); }
        finally { CryptographicOperations.ZeroMemory(bindingHash); }
        var account = new DeviceAccountContext(authority.AbsoluteUri.TrimEnd('/'), accountBinding,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));

        using var device = DeviceIdentity.Create();
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostAccountService? service = null;
        HostSettingsSyncSession? reenabled = null;
        try
        {
            var deviceVault = new WindowsDeviceVault(Path.Combine(deviceVaultRoot, accountBinding + ".dpapi"));
            await deviceVault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await deviceVault.LoadForAccountAsync(account.Issuer, accountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The linked device vault could not be loaded.");
            var session = HostSettingsSyncSession.CreateForTesting(accountBinding, transport, loaded,
                [membership], preferences, syncRoot);
            await session.EnableAsync(ownerConfirmed: true);
            await session.Outbox.UpdateAsync(state => state! with
            {
                LocalOverrides = new LocalSettingsOverrides(Theme: "dark", DefaultDurationMinutes: 50)
            });

            var configuration = new IdentityConfiguration(authority, "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), LocalTest: true,
                ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(configuration, TimeProvider.System);
            SeedSyntheticIdentitySession(identity, subject,
                new AccountState(true, "Local test account", "sync@example.invalid", true));
            service = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                configuration, deviceVaultRoot, preferences, settingsSyncTestRoot: syncRoot);
            SetPrivateField(service, "deviceLinked", true);
            SetPrivateField(service, "settingsSyncEnabled", true);
            SetPrivateField(service, "settingsSyncSession", session);

            var disabling = await service.ExecuteAsync(HostAccountAction.DisableSettingsSync, CancellationToken.None);
            Assert.True(disabling.SettingsSyncBusy);
            await GetPrivateTask(service, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(5));
            await ((Task)typeof(HostAccountService).GetMethod("DisposeSettingsSyncSessionAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(service, [CancellationToken.None])!).WaitAsync(TimeSpan.FromSeconds(5));

            var stateAfterRevoke = await session.Outbox.LoadAsync();
            Assert.NotNull(stateAfterRevoke);
            Assert.False(stateAfterRevoke.SyncConsentEnabled);
            var readsAfterRevoke = transport.ReadCount;
            var publishesAfterRevoke = transport.PublishCount;

            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-revoked-pref"),
                new VerifiedNativeHostAuthorization(), lifetime, settingsStore: preferences, accountService: service);
            const string parameters = "{\"uiLanguage\":\"en\",\"theme\":\"Light\",\"reduceMotion\":false,\"sessionEnd\":\"Window\",\"cyclePhaseNotifications\":true,\"enforcementEnabled\":true,\"lastDurationMinutes\":90,\"lastSessionMode\":\"SimpleTimer\",\"lastCyclePresetId\":null}";
            var responseJson = await bridge.HandleAsync("https://focusguard.app/index.html",
                BridgeRequest(lease.Token, "preferences.set", parameters), CancellationToken.None);
            Assert.NotNull(responseJson);
            using var responseDocument = JsonDocument.Parse(responseJson!);
            var response = responseDocument.RootElement.GetProperty("result").Deserialize<BridgePreferences>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(response);
            Assert.Equal("Light", response.Theme); // Preferences remain editable after revoking sync.
            Assert.Equal(90, response.LastDurationMinutes);
            Assert.Equal("en", response.UiLanguage);

            var stateAfterEdit = await session.Outbox.LoadAsync();
            Assert.NotNull(stateAfterEdit);
            Assert.False(stateAfterEdit.SyncConsentEnabled);
            Assert.Equal(new LocalSettingsOverrides(Theme: "light", DefaultDurationMinutes: 90),
                stateAfterEdit.LocalOverrides);
            Assert.Equal(readsAfterRevoke, transport.ReadCount);
            Assert.Equal(publishesAfterRevoke, transport.PublishCount);
            var localAfterEdit = preferences.Load();
            Assert.Equal(AppTheme.Light, localAfterEdit.Theme);
            Assert.Equal(90, localAfterEdit.LastDurationMinutes);
            Assert.Equal("en", localAfterEdit.UiLanguage);

            var reloaded = await deviceVault.LoadForAccountAsync(account.Issuer, accountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The linked device vault could not be reloaded.");
            reenabled = HostSettingsSyncSession.CreateForTesting(accountBinding, transport, reloaded,
                [membership], preferences, syncRoot);
            await reenabled.EnableAsync(ownerConfirmed: true);
            SetPrivateField(service, "settingsSyncSession", reenabled);
            SetPrivateField(service, "settingsSyncEnabled", true);
            typeof(HostAccountService).GetMethod("StartSettingsSyncRunner",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(service, [reenabled]);
            await WaitUntilAsync(() =>
            {
                var local = preferences.Load();
                return local.Theme == AppTheme.Light && local.LastDurationMinutes == 90;
            }, TimeSpan.FromSeconds(5));
            Assert.Equal(AppTheme.Light, preferences.Load().Theme);
            Assert.Equal(90, preferences.Load().LastDurationMinutes);
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
        }
        finally
        {
            service?.Dispose();
            if (service is null && reenabled is not null) await reenabled.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostAccountServiceDisableActionCancelsAnInFlightSyncAndPersistsRevocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Disable-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50));

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-disable-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostAccountService? accountService = null;

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            var firstIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var idleReports = 0;
            var session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot, report: (result, _, _) =>
                {
                    if (result.Status == SettingsRetryStatus.Idle && Interlocked.Increment(ref idleReports) >= 2)
                        firstIdle.TrySetResult();
                    return Task.CompletedTask;
                });
            await session.EnableAsync(ownerConfirmed: true);

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            accountService = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                identityConfiguration, Path.Combine(root, "devices"), preferences);
            SetPrivateField(accountService, "settingsSyncSession", session);
            SetPrivateField(accountService, "settingsSyncEnabled", true);
            SetPrivateField(accountService, "deviceLinked", true);

            var readsBeforeRunner = transport.ReadCount;
            transport.BlockReads = false;
            typeof(HostAccountService).GetMethod("StartSettingsSyncRunner",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(accountService, [session]);
            await firstIdle.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(readsBeforeRunner + 2, transport.ReadCount);

            transport.BlockReads = true;
            Assert.True(accountService.NotifySettingsSyncConnectivityRestored());
            await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(readsBeforeRunner + 3, transport.ReadCount);

            var disabling = await accountService.ExecuteAsync(HostAccountAction.DisableSettingsSync,
                CancellationToken.None);
            Assert.True(disabling.SettingsSyncBusy);
            var operation = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(accountService)!;
            await operation.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(transport.ReadWasCancelled);
            var state = await session.Outbox.LoadAsync();
            Assert.NotNull(state);
            Assert.False(state.SyncConsentEnabled);
            var settled = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.False(settled.SettingsSyncEnabled);
            Assert.Equal("Disabled", settled.SettingsSyncStatus);
            var readsAfterDisable = transport.ReadCount;
            Assert.False(accountService.NotifySettingsSyncConnectivityRestored());
            Assert.Equal(readsAfterDisable, transport.ReadCount);
        }
        finally
        {
            accountService?.Dispose();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostAccountServiceRecoveryRepairsOnlyDamagedRetryCheckpointAfterOwnerConfirmation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-RetryRecovery-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-retry-recovery-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        var attentionReported = new TaskCompletionSource<SettingsRetryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryReports = new System.Collections.Concurrent.ConcurrentQueue<(int Confirmations, SettingsRetryResult Result)>();
        HostSettingsSyncSession? session = null;
        HostAccountService? accountService = null;
        var confirmationCount = 0;

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            accountService = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                identityConfiguration, Path.Combine(root, "devices"), preferences);
            SetPrivateField(accountService, "nextDeviceCheckUtc", DateTimeOffset.MaxValue);
            session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot, report: async (result, state, token) =>
                {
                    retryReports.Enqueue((Volatile.Read(ref confirmationCount), result));
                    var reportMethod = typeof(HostAccountService).GetMethod("ReportSettingsSyncStateAsync",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    await ((Task)reportMethod.Invoke(accountService, [session, result, state, token])!).ConfigureAwait(false);
                    if (result.Status == SettingsRetryStatus.NeedsAttention)
                        attentionReported.TrySetResult(result);
                });
            await session.EnableAsync(ownerConfirmed: true);

            var scope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, device.Public.DeviceId);
            var syncDirectory = SettingsSyncLocalStorage.GetScopedDirectory(syncRoot, scope);
            var retryPath = Path.Combine(syncDirectory, "settings-retry.dpapi");
            var neighborPath = retryPath + "-backup.tmp";
            var outboxPath = Path.Combine(syncDirectory, "settings-outbox.dpapi");
            var outboxHashBeforeRecovery = await FileHashAsync(outboxPath);
            await File.WriteAllTextAsync(retryPath, "damaged retry checkpoint");
            await File.WriteAllTextAsync(neighborPath, "keep unrelated neighbor");

            SetPrivateField(accountService, "settingsSyncSession", session);
            SetPrivateField(accountService, "settingsSyncEnabled", true);
            SetPrivateField(accountService, "deviceLinked", true);
            accountService.SetSettingsSyncRecoveryPresenter(_ =>
                Task.FromResult(Interlocked.Increment(ref confirmationCount) > 1));

            typeof(HostAccountService).GetMethod("StartSettingsSyncRunner",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(accountService, [session]);
            var attention = await attentionReported.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(SettingsRetryStatus.NeedsAttention, attention.Status);
            Assert.Equal(SettingsTransportFailure.Storage, attention.Failure);
            var attentionSnapshot = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Equal("Attention", attentionSnapshot.SettingsSyncStatus);
            Assert.True(attentionSnapshot.SettingsSyncRecoveryAvailable);
            Assert.True(attentionSnapshot.SettingsSyncEnabled);
            var readsBeforeRecovery = transport.ReadCount;

            var cancelledRecovery = await accountService.ExecuteAsync(HostAccountAction.RecoverSettingsSyncCheckpoint,
                CancellationToken.None);
            Assert.True(cancelledRecovery.SettingsSyncBusy, cancelledRecovery.Error);
            await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(3));
            var cancelledSnapshot = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Equal(1, confirmationCount);
            Assert.Equal("Attention", cancelledSnapshot.SettingsSyncStatus);
            Assert.True(cancelledSnapshot.SettingsSyncRecoveryAvailable);
            Assert.True(File.Exists(retryPath));
            Assert.Equal("keep unrelated neighbor", await File.ReadAllTextAsync(neighborPath));
            Assert.Equal(outboxHashBeforeRecovery, await FileHashAsync(outboxPath));
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.Equal(readsBeforeRecovery, transport.ReadCount);
            var localAfterCancel = preferences.Load();
            Assert.Equal(AppTheme.Dark, localAfterCancel.Theme);
            Assert.Equal("es", localAfterCancel.UiLanguage);
            Assert.Equal(50, localAfterCancel.LastDurationMinutes);
            Assert.True(localAfterCancel.ReduceMotion);
            Assert.True(localAfterCancel.EnforcementEnabled);
            Assert.False(localAfterCancel.CyclePhaseNotifications);

            transport.BlockReads = true;
            var confirmedRecovery = await accountService.ExecuteAsync(HostAccountAction.RecoverSettingsSyncCheckpoint,
                CancellationToken.None);
            Assert.True(confirmedRecovery.SettingsSyncBusy, confirmedRecovery.Error);
            await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(File.Exists(retryPath));
            var immediatelyRecovered = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.False(immediatelyRecovered.SettingsSyncRecoveryAvailable,
                $"Recovery did not clear the Host attention state: {immediatelyRecovered.SettingsSyncStatus}/{immediatelyRecovered.Error}");
            var readOrTimeout = await Task.WhenAny(transport.ReadStarted, Task.Delay(TimeSpan.FromSeconds(3)));
            Assert.True(ReferenceEquals(transport.ReadStarted, readOrTimeout),
                $"No post-recovery transport read. Reads={transport.ReadCount}; " +
                $"reports={string.Join(',', retryReports.Select(report => $"{report.Confirmations}:{report.Result.Status}/{report.Result.Failure}/{report.Result.RetryCheckpointRecoveryAvailable}"))}; " +
                $"runner={GetPrivateTask(accountService, "settingsSyncRunnerTask").Status}; " +
                $"retryExists={File.Exists(retryPath)}; " +
                $"sync={(await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None)).SettingsSyncStatus}/" +
                $"{(await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None)).SettingsSyncRecoveryAvailable}/" +
                $"{(await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None)).Error}");
            var recoveredSnapshot = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Equal(2, confirmationCount);
            Assert.True(recoveredSnapshot.SettingsSyncEnabled);
            Assert.False(recoveredSnapshot.SettingsSyncRecoveryAvailable);
            Assert.False(File.Exists(retryPath));
            Assert.Equal("keep unrelated neighbor", await File.ReadAllTextAsync(neighborPath));
            Assert.Equal(outboxHashBeforeRecovery, await FileHashAsync(outboxPath));
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.Equal(readsBeforeRecovery + 1, transport.ReadCount);
            var localAfterRecovery = preferences.Load();
            Assert.Equal(AppTheme.Dark, localAfterRecovery.Theme);
            Assert.Equal("es", localAfterRecovery.UiLanguage);
            Assert.Equal(50, localAfterRecovery.LastDurationMinutes);
            Assert.True(localAfterRecovery.ReduceMotion);
            Assert.True(localAfterRecovery.EnforcementEnabled);
            Assert.False(localAfterRecovery.CyclePhaseNotifications);
        }
        finally
        {
            accountService?.Dispose();
            if (accountService is null && session is not null) await session.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostAccountServiceResetPreviewsCancelsAndPublishesOnlyTheSharedSettingsTombstone()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Reset-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));

        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-reset-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostSettingsSyncSession? session = null;
        HostAccountService? accountService = null;
        HostSettingsSyncResetPreview? preview = null;
        var resetPresenterMode = 0;
        var stalePreviewPresented = new TaskCompletionSource<HostSettingsSyncResetPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalePreviewDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot);
            await session.EnableAsync(ownerConfirmed: true);

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            accountService = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                identityConfiguration, Path.Combine(root, "devices"), preferences);
            SetPrivateField(accountService, "settingsSyncSession", session);
            SetPrivateField(accountService, "settingsSyncEnabled", true);
            SetPrivateField(accountService, "settingsSyncStatus", "Enabled");
            SetPrivateField(accountService, "deviceLinked", true);
            SetPrivateField(accountService, "nextDeviceCheckUtc", DateTimeOffset.MaxValue);
            accountService.SetSettingsSyncResetPresenter((current, _) =>
            {
                preview = current;
                if (Volatile.Read(ref resetPresenterMode) == 1)
                {
                    stalePreviewPresented.TrySetResult(current);
                    return stalePreviewDecision.Task;
                }
                return Task.FromResult(Volatile.Read(ref resetPresenterMode) == 2);
            });

            var scope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, device.Public.DeviceId);
            var directory = SettingsSyncLocalStorage.GetScopedDirectory(syncRoot, scope);
            var outboxPath = Path.Combine(directory, "settings-outbox.dpapi");
            var outboxHashBefore = await FileHashAsync(outboxPath);
            var preferencesHashBefore = await FileHashAsync(preferencesPath);
            var readsBefore = transport.ReadCount;
            var publishesBefore = transport.PublishCount;

            var cancelled = await accountService.ExecuteAsync(HostAccountAction.ResetSettingsSync, CancellationToken.None);
            Assert.True(cancelled.SettingsSyncBusy, cancelled.Error);
            await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(preview);
            Assert.Equal(new GlobalSettings(50, "dark", "es"), preview.Current);
            Assert.Equal(GlobalSettings.Default, preview.ResetTo);
            Assert.Equal(outboxHashBefore, await FileHashAsync(outboxPath));
            Assert.Equal(preferencesHashBefore, await FileHashAsync(preferencesPath));
            Assert.Equal(readsBefore, transport.ReadCount);
            Assert.Equal(publishesBefore, transport.PublishCount);
            Assert.True((await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None)).SettingsSyncEnabled);

            Volatile.Write(ref resetPresenterMode, 1);
            _ = await accountService.ExecuteAsync(HostAccountAction.ResetSettingsSync, CancellationToken.None);
            await stalePreviewPresented.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await session.Edits.EditSelectedAsync(new GlobalSettings(45, "dark", "es"), SettingsFields.Duration,
                new LocalSettingsOverrides());
            stalePreviewDecision.TrySetResult(true);
            await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(3));
            var staleResetState = await session.Outbox.LoadAsync();
            Assert.NotNull(staleResetState);
            Assert.True(staleResetState.SyncConsentEnabled);
            Assert.False(staleResetState.DraftDeleted);
            Assert.Equal(new GlobalSettings(45, "dark", "es"), staleResetState.Draft);
            Assert.Equal("Pending", (await accountService.ExecuteAsync(HostAccountAction.Get,
                CancellationToken.None)).SettingsSyncStatus);

            await session.Edits.EditSelectedAsync(new GlobalSettings(50, "dark", "es"), SettingsFields.Duration,
                new LocalSettingsOverrides());
            var stableState = await session.Outbox.LoadAsync();
            Assert.NotNull(stableState);
            Assert.False(stableState.Pending);
            var reportMethod = typeof(HostAccountService).GetMethod("ReportSettingsSyncStateAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await ((Task)reportMethod.Invoke(accountService, [session, new SettingsRetryResult(SettingsRetryStatus.Idle),
                stableState, CancellationToken.None])!);

            Volatile.Write(ref resetPresenterMode, 2);
            transport.BlockReads = true;
            var accepted = await accountService.ExecuteAsync(HostAccountAction.ResetSettingsSync, CancellationToken.None);
            Assert.True(accepted.SettingsSyncBusy, accepted.Error);
            await GetPrivateTask(accountService, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(3));
            await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(3));

            var reset = await session.Outbox.LoadAsync();
            Assert.NotNull(reset);
            Assert.True(reset.SyncConsentEnabled);
            Assert.True(reset.DraftDeleted);
            Assert.Equal(GlobalSettings.Default, reset.Draft);
            Assert.True(reset.Pending);
            Assert.Equal(SettingsFields.None, reset.Conflicts);
            Assert.Equal(publishesBefore, transport.PublishCount);
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));

            var local = preferences.Load();
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(local));
            Assert.True(local.ReduceMotion);
            Assert.True(local.EnforcementEnabled);
            Assert.False(local.CyclePhaseNotifications);
            var status = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.True(status.SettingsSyncEnabled);
            Assert.False(status.SettingsSyncBusy);
            Assert.Equal("Pending", status.SettingsSyncStatus);
        }
        finally
        {
            accountService?.Dispose();
            if (accountService is null && session is not null) await session.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostConflictResolutionMapsTheReviewedChoiceAndPreservesOtherSharedAndLocalFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Resolve-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));
        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-resolve-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostSettingsSyncSession? session = null;
        HostAccountService? accountService = null;

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot);
            await session.EnableAsync(ownerConfirmed: true);
            var reviewedDraft = new GlobalSettings(60, "dark", "en");
            var remote = new GlobalSettings(50, "light", "es");
            _ = await session.Outbox.UpdateAsync(state => state! with
            {
                Confirmed = new SettingsSnapshot(2, remote),
                Draft = reviewedDraft,
                Conflicts = SettingsFields.Theme
            });
            var review = new HostSettingsSyncConflictReview(2, ["theme"], remote, reviewedDraft, false);

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            accountService = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                identityConfiguration, Path.Combine(root, "devices"), preferences);
            SetPrivateField(accountService, "settingsSyncSession", session);
            SetPrivateField(accountService, "settingsSyncEnabled", true);
            SetPrivateField(accountService, "settingsSyncStatus", "Conflict");
            SetPrivateField(accountService, "settingsSyncConflictReview", review);
            SetPrivateField(accountService, "deviceLinked", true);

            transport.BlockReads = true;
            var queued = await accountService.ExecuteAsync(HostAccountAction.ResolveSettingsSyncConflict,
                CancellationToken.None, settingsSyncResolution: new HostSettingsSyncConflictResolution(2,
                    [new HostSettingsSyncConflictChoice(HostSettingsSyncConflictField.Theme,
                        HostSettingsSyncConflictSource.OtherDevices)]));
            Assert.True(queued.SettingsSyncBusy);
            var operation = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(accountService)!;
            await operation.WaitAsync(TimeSpan.FromSeconds(3));
            await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(3));

            var resolved = await session.Outbox.LoadAsync();
            Assert.NotNull(resolved);
            Assert.Equal(SettingsFields.None, resolved.Conflicts);
            Assert.Equal(new GlobalSettings(60, "light", "en"), resolved.Draft);
            var local = preferences.Load();
            Assert.Equal(AppTheme.Light, local.Theme);
            Assert.Equal("en", local.UiLanguage);
            Assert.Equal(60, local.LastDurationMinutes);
            Assert.True(local.ReduceMotion);
            Assert.True(local.EnforcementEnabled);
            Assert.False(local.CyclePhaseNotifications);
            var settled = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Equal("Pending", settled.SettingsSyncStatus);
            Assert.Null(settled.SettingsSyncConflict);
        }
        finally
        {
            accountService?.Dispose();
            if (accountService is null && session is not null) await session.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostResetConflictCanRebaseTheTombstoneOnlyAfterTheReviewedChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Reset-" + Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        _ = preferences.Load();
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 35,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));
        using var device = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-settings-sync-reset-test"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(unsigned);
        MembershipDocument membership;
        try { membership = unsigned with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var transport = new MemorySettingsTransport();
        HostSettingsSyncSession? session = null;
        HostAccountService? accountService = null;

        try
        {
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(device, null, account, contentKey, 1, MembershipProtocol.Digest(membership),
                CancellationToken.None, deviceSession);
            var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The test device vault could not be loaded.");
            session = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, transport,
                loaded, [membership], preferences, syncRoot);
            await session.EnableAsync(ownerConfirmed: true);
            var remote = new GlobalSettings(35, "light", "en");
            var conflicts = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
            _ = await session.Outbox.UpdateAsync(state => state! with
            {
                Confirmed = new SettingsSnapshot(2, remote),
                Draft = GlobalSettings.Default,
                Conflicts = conflicts,
                DraftDeleted = true
            });
            var review = new HostSettingsSyncConflictReview(2, ["theme", "language", "duration"],
                remote, GlobalSettings.Default, true);

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            accountService = new HostAccountService(identity, new UserTokenVault(Path.Combine(root, "identity.dpapi")),
                identityConfiguration, Path.Combine(root, "devices"), preferences);
            SetPrivateField(accountService, "settingsSyncSession", session);
            SetPrivateField(accountService, "settingsSyncEnabled", true);
            SetPrivateField(accountService, "settingsSyncStatus", "Conflict");
            SetPrivateField(accountService, "settingsSyncConflictReview", review);
            SetPrivateField(accountService, "deviceLinked", true);

            transport.BlockReads = true;
            var queued = await accountService.ExecuteAsync(HostAccountAction.ResolveSettingsSyncResetConflict,
                CancellationToken.None, settingsSyncResetResolution: new HostSettingsSyncResetResolution(2,
                    HostSettingsSyncResetSource.ThisDeviceReset));
            Assert.True(queued.SettingsSyncBusy);
            var operation = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(accountService)!;
            await operation.WaitAsync(TimeSpan.FromSeconds(3));
            await transport.ReadStarted.WaitAsync(TimeSpan.FromSeconds(3));

            var resolved = await session.Outbox.LoadAsync();
            Assert.NotNull(resolved);
            Assert.True(resolved.DraftDeleted);
            Assert.Equal(GlobalSettings.Default, resolved.Draft);
            Assert.Equal(SettingsFields.None, resolved.Conflicts);
            Assert.Null(resolved.InFlight); // A new signed tombstone will be prepared at revision 2 by the worker.
            var local = preferences.Load();
            Assert.Equal(AppTheme.Dark, local.Theme);
            Assert.Equal("es", local.UiLanguage);
            Assert.Equal(50, local.LastDurationMinutes);
            Assert.True(local.ReduceMotion);
            Assert.True(local.EnforcementEnabled);
            Assert.False(local.CyclePhaseNotifications);
            var settled = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Equal("Pending", settled.SettingsSyncStatus);
            Assert.Null(settled.SettingsSyncConflict);
        }
        finally
        {
            accountService?.Dispose();
            if (accountService is null && session is not null) await session.DisposeAsync();
            transport.Clear();
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static string BridgeRequest(string documentToken, string method, string parameters) =>
        $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{documentToken}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private static Task GetPrivateTask(object instance, string name) =>
        (Task)instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static void SeedSyntheticIdentitySession(IdentitySession identity, string subject, AccountState state)
    {
        var tokenType = typeof(IdentitySession).GetNestedType("SessionTokens",
            System.Reflection.BindingFlags.NonPublic)!;
        var tokens = Activator.CreateInstance(tokenType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, binder: null,
            args: ["synthetic-access-token", "synthetic-refresh-token", null, DateTimeOffset.UtcNow.AddHours(1),
                subject, "synthetic-test-nonce"], culture: null)!;
        typeof(IdentitySession).GetField("tokens",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(identity, tokens);
        typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity, state with { SignedIn = true });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The expected settings projection did not occur.");
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }

    private static async Task<string> FileHashAsync(string path) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

    private sealed class MemorySettingsTransport : ISettingsDocumentTransport
    {
        private readonly TaskCompletionSource readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int blockReads;
        private int readWasCancelled;
        public int ReadCount { get; private set; }
        public int PublishCount { get; private set; }
        public byte[]? Document { get; private set; }
        public bool BlockReads { get => Volatile.Read(ref blockReads) != 0; set => Volatile.Write(ref blockReads, value ? 1 : 0); }
        public bool ReadWasCancelled => Volatile.Read(ref readWasCancelled) != 0;
        public Task ReadStarted => readStarted.Task;

        public async Task<byte[]?> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReadCount++;
            if (BlockReads)
            {
                readStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { Volatile.Write(ref readWasCancelled, 1); throw; }
            }
            return Document?.ToArray();
        }

        public Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.NotEqual(Guid.Empty, operationId);
            PublishCount++;
            Document = envelope.ToArray();
            return Task.FromResult(SettingsPublishResult.Stored);
        }

        public void Clear()
        {
            if (Document is not null) CryptographicOperations.ZeroMemory(Document);
            Document = null;
        }
    }
}
