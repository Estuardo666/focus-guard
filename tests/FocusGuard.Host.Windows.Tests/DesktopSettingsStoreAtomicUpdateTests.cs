using FocusGuard.Client.Preferences;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class DesktopSettingsStoreAtomicUpdateTests
{
    [Fact]
    public void SharedUpdateChecksBaselineAndPreservesOtherPreferencesUnderLease()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-settings-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DesktopSettingsStore(Path.Combine(root, "desktop-settings.json"));
            _ = store.Load();
            var initial = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
                ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
            store.SaveVerified(initial);

            var applied = store.TryApplySharedIfUnchanged(AppTheme.Dark, "es", 50,
                AppTheme.Light, null, 25, out var updated);

            Assert.True(applied);
            Assert.Equal(AppTheme.Light, updated!.Theme);
            Assert.Equal(25, updated.LastDurationMinutes);
            Assert.Equal("es", updated.UiLanguage);
            Assert.True(updated.ReduceMotion);
            Assert.True(updated.EnforcementEnabled);
            Assert.False(updated.CyclePhaseNotifications);

            var rejected = store.TryApplySharedIfUnchanged(AppTheme.Dark, "es", 50,
                null, "en", null, out var rejectedUpdate);
            Assert.False(rejected);
            Assert.Null(rejectedUpdate);
            Assert.Equal(updated, store.Load());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
