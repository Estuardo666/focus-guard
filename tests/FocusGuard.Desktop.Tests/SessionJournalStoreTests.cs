using FocusGuard.Client.Journal;
using FocusGuard.Client.Preferences;
using FocusGuard.Desktop;
using FocusGuard.Domain;

namespace FocusGuard.Desktop.Tests;

public sealed class SessionJournalStoreTests
{
    [Fact]
    public void IntentionsAndReflectionsSurviveRestartAndHaveBounds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focusguard-journal-" + Guid.NewGuid());
        var path = Path.Combine(directory, "journal.json");
        try
        {
            var entry = new SessionIntention(Guid.NewGuid(), Guid.NewGuid(), "Work", 50, SessionMode.SimpleTimer, null, null, false, new('x', 300), new('y', 1100));
            new SessionJournalStore(path).Save(entry);
            var reloaded = new SessionJournalStore(path).Get(entry.SessionId)!;
            Assert.Equal(240, reloaded.Objective.Length);
            Assert.Equal(1000, reloaded.Reflection.Length);
            Assert.Equal(entry.ProfileId, reloaded.ProfileId);
            Assert.False(File.Exists(path + ".tmp"));
            File.WriteAllText(path, "broken json");
            Assert.Null(new SessionJournalStore(path).Get(entry.SessionId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void InvalidPreferencesFallBackAndOldPreferencesRemainReadable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focusguard-settings-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new DesktopSettingsStore(path);
            File.WriteAllText(path, "{\"theme\":1,\"reduceMotion\":true}");
            Assert.Equal(AppTheme.Light, store.Load().Theme);
            Assert.Equal(50, store.Load().LastDurationMinutes);
            File.WriteAllText(path, "{\"lastDurationMinutes\":-1}");
            Assert.Equal(DesktopSettings.Default, store.Load());
            store.Save(new(LastDurationMinutes: 47, CompactTopmost: true));
            Assert.Equal(47, store.Load().LastDurationMinutes);
            Assert.True(store.Load().CompactTopmost);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
