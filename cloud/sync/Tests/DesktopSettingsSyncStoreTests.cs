using FocusGuard.Client.Preferences;
using FocusGuard.Sync;
using Xunit;

public sealed class DesktopSettingsSyncStoreTests
{
    [Fact]
    public async Task StrictLoadReturnsVerifiedLocalBaseline()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "desktop-settings.json");
            var local = new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 35,
                EnforcementEnabled: true, ReduceMotion: true);
            new DesktopSettingsStore(path).SaveVerified(local);

            Assert.Equal(local, await new DesktopSettingsSyncStore(path).LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StrictLoadDoesNotInventDefaultsForMissingOrCorruptPreferences()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "desktop-settings.json");
            var store = new DesktopSettingsSyncStore(path);
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync());

            await File.WriteAllTextAsync(path, "not json");
            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
            Assert.Equal("not json", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-DesktopSettingsSync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
