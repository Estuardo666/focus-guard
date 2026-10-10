using System.Globalization;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Desktop.Tests;

public sealed class CyclePhaseNotificationTests
{
    [Fact]
    public void AuthoritativeBoundariesEmitOnceAndCombineAdjacentPhases()
    {
        var tracker = new CyclePhaseTransitionTracker();
        var sessionId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;

        var firstFocus = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(0, SessionPhaseKind.Focus, 1, 1500, start.AddMinutes(25))));
        var countdownOnly = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(0, SessionPhaseKind.Focus, 1, 1499, start.AddMinutes(25))));
        var shortBreak = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(1, SessionPhaseKind.ShortBreak, 1, 300, start.AddMinutes(30))));
        var secondFocus = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(2, SessionPhaseKind.Focus, 2, 1500, start.AddMinutes(55))));
        var completed = tracker.Observe(Status(
            sessionId,
            phase: null,
            state: "Expired"));
        var repeatedCompletion = tracker.Observe(Status(
            sessionId,
            phase: null,
            state: "Expired"));

        Assert.Equal(SessionPhaseKind.Focus, firstFocus!.StartedKind);
        Assert.Null(firstFocus.EndedKind);
        Assert.Null(countdownOnly);
        Assert.Equal(SessionPhaseKind.Focus, shortBreak!.EndedKind);
        Assert.Equal(SessionPhaseKind.ShortBreak, shortBreak.StartedKind);
        Assert.Equal(SessionPhaseKind.ShortBreak, secondFocus!.EndedKind);
        Assert.Equal(SessionPhaseKind.Focus, secondFocus.StartedKind);
        Assert.Equal(SessionPhaseKind.Focus, completed!.EndedKind);
        Assert.Null(completed.StartedKind);
        Assert.Null(repeatedCompletion);
    }

    [Fact]
    public void LongBreaksAreTrackedButAreNotAnnounced()
    {
        var tracker = new CyclePhaseTransitionTracker();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var longBreak = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(7, SessionPhaseKind.LongBreak, 4, 900, now.AddMinutes(15))));
        var focusAfterLongBreak = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(8, SessionPhaseKind.Focus, 5, 1500, now.AddMinutes(40))));
        var nextLongBreak = tracker.Observe(Status(
            sessionId,
            new PhaseStatus(9, SessionPhaseKind.LongBreak, 5, 900, now.AddMinutes(55))));

        Assert.Null(longBreak);
        Assert.Null(focusAfterLongBreak!.EndedKind);
        Assert.Equal(SessionPhaseKind.Focus, focusAfterLongBreak.StartedKind);
        Assert.Equal(SessionPhaseKind.Focus, nextLongBreak!.EndedKind);
        Assert.Null(nextLongBreak.StartedKind);
    }

    [Fact]
    public void SimpleTimersNeverEmitCyclePhaseTransitions()
    {
        var tracker = new CyclePhaseTransitionTracker();
        var status = Status(
            Guid.NewGuid(),
            new PhaseStatus(0, SessionPhaseKind.Focus, 1, 60, DateTimeOffset.UtcNow.AddMinutes(1)))
            with
        { Mode = SessionMode.SimpleTimer };

        Assert.Null(tracker.Observe(status));
    }

    [Theory]
    [InlineData(SessionPhaseKind.Focus)]
    [InlineData(SessionPhaseKind.ShortBreak)]
    public void TerminalRelevantPhaseEmitsOneCompletion(SessionPhaseKind kind)
    {
        var tracker = new CyclePhaseTransitionTracker();
        var sessionId = Guid.NewGuid();
        var end = DateTimeOffset.UtcNow.AddMinutes(5);
        tracker.Observe(Status(sessionId, new PhaseStatus(3, kind, 2, 300, end)));

        var completion = tracker.Observe(Status(sessionId, phase: null, state: "Expired"));

        Assert.Equal(kind, completion!.EndedKind);
        Assert.Null(completion.StartedKind);
        Assert.Null(tracker.Observe(Status(sessionId, phase: null, state: "Expired")));
    }

    [Fact]
    public void SpanishTransitionCopyNamesTheEndedAndStartedPhases()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-EC");
            var presentation = CyclePhaseNotificationCopy.Create(new CyclePhaseTransition(
                SessionPhaseKind.Focus,
                1,
                SessionPhaseKind.ShortBreak,
                1,
                new DateTimeOffset(2026, 8, 4, 20, 30, 0, TimeSpan.Zero)));

            Assert.Equal("Subciclo de enfoque 1 completado", presentation.Title);
            Assert.Contains("descanso corto", presentation.Body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void CyclePhaseNotificationPreferencePersistsAndDefaultsOn()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-settings.json");
        var store = new DesktopSettingsStore(path);

        Assert.True(store.Load().CyclePhaseNotifications);

        store.Save(DesktopSettings.Default with { CyclePhaseNotifications = false });

        Assert.False(new DesktopSettingsStore(path).Load().CyclePhaseNotifications);
    }

    [Fact]
    public void ExistingSettingsWithoutTheNewPreferenceDefaultNotificationsOn()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "desktop-settings.json");
        File.WriteAllText(path, "{\"sessionEnd\":0,\"theme\":0,\"reduceMotion\":false}");

        Assert.True(new DesktopSettingsStore(path).Load().CyclePhaseNotifications);
    }

    [Fact]
    public void CornerNotificationIsPoliteAndDoesNotStealFocusOrTaskbarSpace()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "FocusGuard.Desktop",
            "SessionToastWindow.axaml"));

        Assert.Contains("ShowInTaskbar=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowActivated=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
    }

    private static StatusResponse Status(
        Guid sessionId,
        PhaseStatus? phase,
        string state = "Active") =>
        new(
            ServiceReady: true,
            ExtensionReady: true,
            SessionState: state,
            RemainingSeconds: phase?.RemainingSeconds ?? 0,
            SessionId: sessionId,
            ProfileId: Guid.NewGuid(),
            StartUtc: DateTimeOffset.UtcNow,
            EndUtc: DateTimeOffset.UtcNow.AddHours(1),
            StrictMode: false,
            EnforcementConnected: true,
            Mode: SessionMode.FocusCycle,
            CurrentPhase: phase);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusGuard.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the FocusGuard repository root.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
