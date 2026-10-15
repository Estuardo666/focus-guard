using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostSettingsSyncConsentBridgeTests
{
    [Fact]
    public async Task CancelledNativeConsentLeavesUserSettingsAndDeviceVaultUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-SyncConsentBridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var deviceRoot = Path.Combine(root, "devices");
            var identityVaultPath = Path.Combine(root, "identity.dpapi");
            var preferencePath = Path.Combine(root, "desktop-settings.json");
            var preferences = new DesktopSettingsStore(preferencePath);
            var originalPreferences = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
                ReduceMotion: true, CyclePhaseNotifications: false, EnforcementEnabled: true);
            preferences.SaveVerified(originalPreferences);

            var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/realms/test"),
                "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"),
                ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(configuration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            using var account = new HostAccountService(identity, new UserTokenVault(identityVaultPath), configuration,
                deviceRoot, preferences);
            SetPrivateField(account, "deviceLinked", true);

            var consentPrompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completeConsent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            account.SetSettingsSyncConsentPresenter(async token =>
            {
                consentPrompted.TrySetResult();
                return await completeConsent.Task.WaitAsync(token);
            });

            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-consent-bridge"),
                new VerifiedNativeHostAuthorization(), lifetime, accountService: account);
            var enableResponseJson = await bridge.HandleAsync("https://focusguard.app/index.html",
                Request(lease.Token, "account.settings.sync.enable", "{}"), CancellationToken.None);
            Assert.NotNull(enableResponseJson);
            var enableResponse = JsonSerializer.Deserialize<BridgeAccountResponse>(enableResponseJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Null(enableResponse.Error);
            Assert.True(enableResponse.Result.SettingsSyncBusy);
            Assert.Equal("Enabling", enableResponse.Result.SettingsSyncStatus);

            await consentPrompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            completeConsent.TrySetResult(false);
            var syncTask = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(account)!;
            await syncTask.WaitAsync(TimeSpan.FromSeconds(5));

            var stateResponseJson = await bridge.HandleAsync("https://focusguard.app/index.html",
                Request(lease.Token, "account.get", "{}"), CancellationToken.None);
            Assert.NotNull(stateResponseJson);
            var stateResponse = JsonSerializer.Deserialize<BridgeAccountResponse>(stateResponseJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Null(stateResponse.Error);
            Assert.False(stateResponse.Result.SettingsSyncEnabled);
            Assert.False(stateResponse.Result.SettingsSyncBusy);
            Assert.Equal("Cancelled", stateResponse.Result.SettingsSyncStatus);

            Assert.False(Directory.Exists(deviceRoot));
            Assert.False(File.Exists(identityVaultPath));
            Assert.Equal(originalPreferences, preferences.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string Request(string documentToken, string method, string parameters) =>
        $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{documentToken}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);
}
