using System.Text;
using System.Security.Cryptography;
using FocusGuard.Client.Preferences;
using FocusGuard.Domain;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Hosting;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class SharedDesktopSettingsTests
{
    [Fact]
    public void MergeAndImportOnlySelectedAllowlistedPreferences()
    {
        var profileId = Guid.NewGuid();
        var cyclePresetId = Guid.NewGuid();
        var local = new DesktopSettings(SessionEnd: SessionEndPresentation.Notification,
            Theme: AppTheme.Light, ReduceMotion: true,
            ProfileThemes: new Dictionary<Guid, AppTheme> { [profileId] = AppTheme.Light },
            EnforcementEnabled: true, CyclePhaseNotifications: false, LastProfileId: profileId,
            LastDurationMinutes: 30, CompactTopmost: true, LastSessionMode: SessionMode.FocusCycle,
            LastCyclePresetId: cyclePresetId, UiLanguage: "en");
        var existing = new SharedDesktopSettings(1, "Dark", "es", 50);

        var merged = SharedDesktopSettings.Merge(existing, local,
            new SettingsTransferSelection(Theme: true, Language: false, Duration: true));
        Assert.Equal(new SharedDesktopSettings(1, "Light", "es", 30), merged);

        var canonical = merged.ToCanonicalJson();
        try
        {
            var text = Encoding.UTF8.GetString(canonical);
            Assert.Equal("{\"durationMinutes\":30,\"language\":\"es\",\"schemaVersion\":1,\"theme\":\"Light\"}", text);
            var parsed = SharedDesktopSettings.Parse(canonical);
            var imported = parsed.ApplyTo(local, new SettingsTransferSelection(Theme: false, Language: true, Duration: true));
            Assert.Equal(AppTheme.Light, imported.Theme);
            Assert.Equal("es", imported.UiLanguage);
            Assert.Equal(30, imported.LastDurationMinutes);
            Assert.Equal(local.SessionEnd, imported.SessionEnd);
            Assert.True(imported.EnforcementEnabled);
            Assert.True(imported.ReduceMotion);
            Assert.Equal(AppTheme.Light, imported.ProfileThemes![profileId]);
            Assert.Equal(local.CyclePhaseNotifications, imported.CyclePhaseNotifications);
            Assert.Equal(local.LastProfileId, imported.LastProfileId);
            Assert.Equal(local.CompactTopmost, imported.CompactTopmost);
            Assert.Equal(local.LastSessionMode, imported.LastSessionMode);
            Assert.Equal(local.LastCyclePresetId, imported.LastCyclePresetId);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(canonical); }
    }

    [Fact]
    public void PersistingImportedSettingsPreservesConcurrentUnselectedLocalChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard.SettingsTransfer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "desktop-settings.json");
        var profileId = Guid.NewGuid();
        var cyclePresetId = Guid.NewGuid();
        try
        {
            var importerStore = new DesktopSettingsStore(path);
            importerStore.SaveVerified(DesktopSettings.Default);
            var baseline = importerStore.Load();

            var concurrentStore = new DesktopSettingsStore(path);
            var concurrent = concurrentStore.Load() with
            {
                SessionEnd = SessionEndPresentation.Notification,
                ReduceMotion = true,
                ProfileThemes = new Dictionary<Guid, AppTheme> { [profileId] = AppTheme.Dark },
                EnforcementEnabled = true,
                CyclePhaseNotifications = false,
                LastProfileId = profileId,
                CompactTopmost = true,
                LastSessionMode = SessionMode.FocusCycle,
                LastCyclePresetId = cyclePresetId
            };
            concurrentStore.SaveVerified(concurrent);

            var shared = new SharedDesktopSettings(1, "Light", "en", 25);
            var imported = shared.ApplyTo(baseline, new SettingsTransferSelection(true, true, true));
            importerStore.Save(imported);

            var persisted = new DesktopSettingsStore(path).Load();
            Assert.Equal(AppTheme.Light, persisted.Theme);
            Assert.Equal("en", persisted.UiLanguage);
            Assert.Equal(25, persisted.LastDurationMinutes);
            Assert.Equal(concurrent.SessionEnd, persisted.SessionEnd);
            Assert.True(persisted.ReduceMotion);
            Assert.Equal(AppTheme.Dark, persisted.ProfileThemes![profileId]);
            Assert.True(persisted.EnforcementEnabled);
            Assert.False(persisted.CyclePhaseNotifications);
            Assert.Equal(profileId, persisted.LastProfileId);
            Assert.True(persisted.CompactTopmost);
            Assert.Equal(SessionMode.FocusCycle, persisted.LastSessionMode);
            Assert.Equal(cyclePresetId, persisted.LastCyclePresetId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsEmptySelectionUnsupportedFieldsAndInvalidValues()
    {
        var local = DesktopSettings.Default;
        Assert.Throws<InvalidDataException>(() => SharedDesktopSettings.Merge(null, local,
            new SettingsTransferSelection(false, false, false)));
        Assert.Throws<InvalidDataException>(() => SharedDesktopSettings.Parse(
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"theme\":\"Dark\",\"enforcementEnabled\":true}")));
        Assert.Throws<InvalidDataException>(() => SharedDesktopSettings.Parse(
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"theme\":\"Dark\",\"theme\":\"Light\"}")));
        Assert.Throws<InvalidDataException>(() => SharedDesktopSettings.Parse(
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"durationMinutes\":0}")));
    }

    [Fact]
    public void SelectedPreferencesRoundTripOnlyInsideSignedEncryptedContentDocument()
    {
        using var author = DeviceIdentity.Create();
        using var unrelatedAuthor = DeviceIdentity.Create();
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var context = new ContentDocumentContext(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            "baf56f37-89a7-4a98-9a1e-e87bb3504a91", "preferences.initial", 0, 1, 1, 1);
        var selected = new SharedDesktopSettings(1, "Light", "en", 25);
        var clear = selected.ToCanonicalJson();
        try
        {
            var encrypted = ContentDocumentCodec.Encrypt(clear, contentKey, context, author);
            Assert.DoesNotContain("Light", encrypted.Ciphertext, StringComparison.Ordinal);
            var opened = ContentDocumentCodec.Decrypt(encrypted, context, contentKey, author.Public);
            try
            {
                var imported = SharedDesktopSettings.Parse(opened).ApplyTo(DesktopSettings.Default,
                    new SettingsTransferSelection(true, true, true));
                Assert.Equal(AppTheme.Light, imported.Theme);
                Assert.Equal("en", imported.UiLanguage);
                Assert.Equal(25, imported.LastDurationMinutes);
                Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(
                    encrypted, context, contentKey, unrelatedAuthor.Public));
            }
            finally { CryptographicOperations.ZeroMemory(opened); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(contentKey); }
    }
}
