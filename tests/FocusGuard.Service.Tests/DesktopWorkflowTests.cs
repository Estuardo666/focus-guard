using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Service.Tests;

public sealed class DesktopWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "focusguard-workflows-" + Guid.NewGuid());
    private static readonly DateTimeOffset Friday = new(2026, 10, 2, 9, 59, 0, TimeSpan.Zero);
    private readonly Clock _clock = new(Friday);
    private SqliteSessionHistoryStore History => new(Path.Combine(_directory, "history.db"));
    private string SchedulePath => Path.Combine(_directory, "schedules.json");
    private WeeklySchedule Schedule(int minutes = 50, bool enforcementEnabled = false)
    {
        var profileId = Guid.NewGuid();
        IReadOnlyList<ApplicationPolicyRule> rules = enforcementEnabled
            ? [new ApplicationPolicyRule(Guid.NewGuid(), "Block", "FileName", "NeverRun.exe")]
            : [];
        return new WeeklySchedule(Guid.NewGuid(), "Morning", 1 << (int)DayOfWeek.Friday, 600, "UTC", true,
            new ActivateSessionRequest(profileId, minutes * 60, false, 0,
                ApplicationRules: rules, EnforcementEnabled: enforcementEnabled, ProfileName: "Work"));
    }

    [Fact]
    public async Task ScheduleRunsWithoutDesktop_ExpiresAtOriginalDeadline_AndDoesNotReplayAfterRestart()
    {
        var service = new InMemoryFocusService(_clock, historyStore: History);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule();
        Assert.True((await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default)).Accepted);
        _clock.Advance(TimeSpan.FromMinutes(6)); // five minutes late, but still inside the authorized interval
        await workflows.TickAsync(default);
        var status = await service.GetStatusAsync(default);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(50), status.EndUtc!.Value.UtcDateTime);
        var id = status.SessionId;
        await workflows.TickAsync(default);
        Assert.Equal(id, (await service.GetStatusAsync(default)).SessionId);
        await service.StopNormalSessionAsync(default);
        var restarted = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        await restarted.TickAsync(default);
        Assert.Equal("Stopped", (await service.GetStatusAsync(default)).SessionState);
        _clock.Advance(TimeSpan.FromHours(1));
        await restarted.TickAsync(default);
        Assert.Equal("Started", Assert.Single((await restarted.ListAsync(default)).Schedules).LastResult);
    }

    [Fact]
    public async Task ScheduleNeverReplacesAnActiveSession()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule();
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);
        var manual = await service.ActivateAsync(schedule.Activation, default);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await workflows.TickAsync(default);
        Assert.Equal(manual.Value!.SessionId, (await service.GetStatusAsync(default)).SessionId);
        Assert.Equal("Busy", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);
    }

    [Fact]
    public async Task RevisionedScheduleListReturnsServiceCalculatedNextEndOnlyWhenRequested()
    {
        var workflows = new DesktopWorkflowService(new InMemoryFocusService(_clock), History, SchedulePath, _clock);
        var schedule = Schedule(1);
        var saved = await workflows.ChangeRevisionedAsync(new("Save", schedule.Id, null, schedule, IncludeNextEndUtc: true), default);
        Assert.True(saved.Accepted);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(1), Assert.Single(saved.Schedules).Schedule.NextEndUtc!.Value.UtcDateTime);

        var legacy = Assert.Single((await workflows.ListRevisionedAsync(default)).Schedules).Schedule;
        Assert.Equal(Friday.Date.AddHours(10), legacy.NextStartUtc!.Value.UtcDateTime);
        Assert.Null(legacy.NextEndUtc);

        var withEnd = Assert.Single((await workflows.ListRevisionedAsync(default, includeNextEndUtc: true)).Schedules).Schedule;
        Assert.Equal(Friday.Date.AddHours(10), withEnd.NextStartUtc!.Value.UtcDateTime);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(1), withEnd.NextEndUtc!.Value.UtcDateTime);
        Assert.Equal("Etc/UTC", withEnd.TimeZoneIanaId);
    }

    [Fact]
    public async Task RevisionedScheduleListReturnsNextEndOnlyWhenTheClientRequestsIt()
    {
        var workflows = new DesktopWorkflowService(new InMemoryFocusService(_clock), History, SchedulePath, _clock);
        var schedule = Schedule(1);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        var legacy = Assert.Single((await workflows.ListRevisionedAsync(default)).Schedules).Schedule;
        Assert.Equal(Friday.Date.AddHours(10), legacy.NextStartUtc!.Value.UtcDateTime);
        Assert.Null(legacy.NextEndUtc);

        var withEnd = Assert.Single((await workflows.ListRevisionedAsync(default, includeNextEndUtc: true)).Schedules).Schedule;
        Assert.Equal(Friday.Date.AddHours(10), withEnd.NextStartUtc!.Value.UtcDateTime);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(1), withEnd.NextEndUtc!.Value.UtcDateTime);
    }

    [Fact]
    public async Task LateScheduleReleasesAtDeadlineAndClockRollbackCannotReplay()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var s = Schedule();
        await workflows.ChangeAsync(new("Save", s.Id, s), default);
        _clock.Advance(TimeSpan.FromMinutes(6));
        await workflows.TickAsync(default);
        _clock.Advance(TimeSpan.FromMinutes(45));
        Assert.Equal("Expired", (await service.GetStatusAsync(default)).SessionState);
        _clock.Advance(TimeSpan.FromMinutes(-40));
        await workflows.TickAsync(default);
        Assert.Equal("Expired", (await service.GetStatusAsync(default)).SessionState);
    }

    [Fact]
    public async Task SkippingABreakPersistsOnlyTheBreakThatActuallyElapsed()
    {
        var history = History;
        var service = new InMemoryFocusService(_clock, historyStore: history);
        var prepared = await service.PrepareSessionAsync(new(Guid.NewGuid(), "Work", SessionMode.FocusCycle, 0,
            new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0), default);
        await service.ActivatePreparedSessionAsync(new(prepared.Value!.PreparationId!.Value), default);
        _clock.Advance(TimeSpan.FromSeconds(70));
        Assert.True((await service.SkipBreakAsync(new(1), default)).Value!.Accepted);
        _clock.Advance(TimeSpan.FromSeconds(60));
        await service.GetStatusAsync(default);
        var phases = (await history.GetMostRecentAsync())!.Phases!;
        Assert.Equal(10, (phases[1].EndUtc - phases[1].StartUtc).TotalSeconds);
        Assert.Equal(phases[1].EndUtc, phases[2].StartUtc);
        Assert.Equal(2, (await history.GetMostRecentAsync())!.FocusedMinutes);
    }

    [Fact]
    public async Task MissedAndDisabledSchedulesDoNotStart()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var s = Schedule();
        await workflows.ChangeAsync(new("Save", s.Id, s), default);
        _clock.Advance(TimeSpan.FromHours(1));
        await workflows.TickAsync(default);
        Assert.Equal("Missed", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);
        await workflows.ChangeAsync(new("Enable", s.Id, Enabled: false), default);
        _clock.Advance(TimeSpan.FromDays(7));
        await workflows.TickAsync(default);
        Assert.Equal("Inactive", (await service.GetStatusAsync(default)).SessionState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneMinuteScheduleStartsWithRemainingTimeAndKeepsItsScheduledEnd(bool enforcementEnabled)
    {
        var service = new InMemoryFocusService(_clock, historyStore: History);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule(1, enforcementEnabled);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        _clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(5)));
        await workflows.TickAsync(default);

        var status = await service.GetStatusAsync(default);
        Assert.NotNull(status.SessionId);
        Assert.Equal(55, status.RemainingSeconds);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(1), status.EndUtc!.Value.UtcDateTime);
        Assert.Equal(new ScheduledSessionIntent("Work", 1, enforcementEnabled), status.ScheduledIntent);
        Assert.Equal("Started", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);
        var sessionId = status.SessionId!.Value;
        Assert.True((await workflows.ChangeAsync(new("Delete", schedule.Id), default)).Accepted);

        _clock.Advance(TimeSpan.FromSeconds(55));
        var expired = await service.GetStatusAsync(default);
        Assert.Equal("Expired", expired.SessionState);
        Assert.Equal(new ScheduledSessionIntent("Work", 1, enforcementEnabled), expired.ScheduledIntent);
        var summary = await service.GetSessionSummaryAsync(new(sessionId), default);
        Assert.True(summary.Available);
        Assert.Equal(schedule.Activation.ProfileId, summary.ProfileId);
        Assert.Equal(new ScheduledSessionIntent("Work", 1, enforcementEnabled), summary.ScheduledIntent);
    }

    [Fact]
    public async Task OneMinuteScheduleWithNoTimeRemainingIsMissedWithoutActivation()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule(1);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await workflows.TickAsync(default);

        Assert.Equal("Inactive", (await service.GetStatusAsync(default)).SessionState);
        Assert.Null((await service.GetStatusAsync(default)).SessionId);
        Assert.Equal("Missed", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);
    }

    [Fact]
    public async Task OneMinuteScheduleWithLessThanOneSecondRemainingIsMissedWithoutActivation()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule(1);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        _clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(59.5)));
        await workflows.TickAsync(default);

        Assert.Equal("Inactive", (await service.GetStatusAsync(default)).SessionState);
        Assert.Null((await service.GetStatusAsync(default)).SessionId);
        Assert.Equal("Missed", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);
    }

    [Fact]
    public async Task OneMinuteScheduleWithExactlyOneSecondRemainingStartsAndExpiresAtScheduledEnd()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var schedule = Schedule(1);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        _clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(59)));
        await workflows.TickAsync(default);

        var status = await service.GetStatusAsync(default);
        Assert.NotNull(status.SessionId);
        Assert.Equal(1, status.RemainingSeconds);
        Assert.Equal(Friday.Date.AddHours(10).AddMinutes(1), status.EndUtc!.Value.UtcDateTime);
        Assert.Equal("Started", Assert.Single((await workflows.ListAsync(default)).Schedules).LastResult);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Expired", (await service.GetStatusAsync(default)).SessionState);
    }

    [Fact]
    public async Task ShortScheduledRemainderKeepsSessionSummaryValid()
    {
        var history = History;
        var service = new InMemoryFocusService(_clock, historyStore: history);
        var workflows = new DesktopWorkflowService(service, history, SchedulePath, _clock);
        var schedule = Schedule(1);
        await workflows.ChangeAsync(new("Save", schedule.Id, schedule), default);

        _clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(45)));
        await workflows.TickAsync(default);
        var active = await service.GetStatusAsync(default);
        Assert.NotNull(active.SessionId);
        Assert.Equal(15, active.RemainingSeconds);
        _clock.Advance(TimeSpan.FromSeconds(15));
        await service.GetStatusAsync(default);

        var summary = await service.GetSessionSummaryAsync(new(active.SessionId), default);
        Assert.True(summary.Available);
        Assert.Equal("Completed", summary.Outcome);
        Assert.Equal(0, summary.FocusedMinutes);
        Assert.Equal(1, summary.PlannedMinutes);
        var envelope = IpcProtocol.CreateSessionSummaryResponse(Guid.NewGuid(), summary);
        Assert.True(envelope.IsValid);
        Assert.True(IpcProtocol.ReadSessionSummaryResponse(envelope.Value!).IsValid);
    }

    [Fact]
    public async Task EnablingDuringAnIntervalWaitsUntilNextWeek()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var s = Schedule() with { Enabled = false };
        await workflows.ChangeAsync(new("Save", s.Id, s), default);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await workflows.ChangeAsync(new("Enable", s.Id, Enabled: true), default);
        await workflows.TickAsync(default);
        Assert.Equal("Inactive", (await service.GetStatusAsync(default)).SessionState);
        Assert.Equal(Friday.Date.AddDays(7).AddHours(10), Assert.Single((await workflows.ListAsync(default)).Schedules).NextStartUtc!.Value.UtcDateTime);
    }

    [Fact]
    public async Task OverlapsAreRejected_AndInvalidStoreNeverActivates()
    {
        var service = new InMemoryFocusService(_clock);
        var workflows = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        var s = Schedule();
        await workflows.ChangeAsync(new("Save", s.Id, s), default);
        var second = Schedule();
        Assert.Equal("Overlap", (await workflows.ChangeAsync(new("Save", second.Id, second), default)).Reason);
        await File.WriteAllTextAsync(SchedulePath, "[{}]");
        var restarted = new DesktopWorkflowService(service, History, SchedulePath, _clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.TickAsync(default));
        Assert.Equal("Inactive", (await service.GetStatusAsync(default)).SessionState);
    }

    [Fact]
    public async Task TodayPersistsActualCycleSpansAndExcludesBreakMinutes()
    {
        var history = History;
        var service = new InMemoryFocusService(_clock, historyStore: history);
        var request = new PrepareSessionRequest(Guid.NewGuid(), "Work", SessionMode.FocusCycle, 0,
            new FocusCycleConfigurationContract(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0);
        var prepared = await service.PrepareSessionAsync(request, default);
        await service.ActivatePreparedSessionAsync(new(prepared.Value!.PreparationId!.Value), default);
        _clock.Advance(TimeSpan.FromMinutes(3));
        await service.GetStatusAsync(default);
        var workflows = new DesktopWorkflowService(service, history, SchedulePath, _clock);
        var today = await workflows.TodayAsync(new(new DateTimeOffset(Friday.Date, TimeSpan.Zero), new DateTimeOffset(Friday.Date.AddDays(1), TimeSpan.Zero)), default);
        Assert.Equal(120, today.FocusSeconds);
        Assert.Equal(1, today.CompletedSessions);
        Assert.Equal(["Focus", "ShortBreak", "Focus"], today.Timeline.Select(p => p.Kind));
        Assert.Equal(2, (await history.GetMostRecentAsync())!.FocusedMinutes);
    }

    [Fact]
    public async Task TodayClipsSpansAtMidnightAndCountsEarlyStopsHonestly()
    {
        var history = History;
        var start = new DateTimeOffset(2026, 10, 1, 23, 50, 0, TimeSpan.Zero);
        await history.RecordAsync(new(Guid.NewGuid(), new(Guid.NewGuid()), "Work", start, start.AddMinutes(20), false, 50, 20, 0,
            [new(SessionPhaseKind.Focus, start, start.AddMinutes(20))]));
        var workflows = new DesktopWorkflowService(new InMemoryFocusService(_clock), history, SchedulePath, _clock);
        var today = await workflows.TodayAsync(new(new DateTimeOffset(Friday.Date, TimeSpan.Zero), new DateTimeOffset(Friday.Date.AddDays(1), TimeSpan.Zero)), default);
        Assert.Equal(600, today.FocusSeconds);
        Assert.Equal(0, today.CompletedSessions);
        Assert.Equal(Friday.Date, Assert.Single(today.Timeline).StartUtc.UtcDateTime);
    }

    [Fact]
    public void WeeklyOccurrenceSkipsSpringGapAndRunsFallOverlapOnce()
    {
        var spring = WeeklyOccurrenceCalculator.Around(new(2026, 3, 8, 0, 0, 0, TimeSpan.Zero), 1, 150, 3000, "America/New_York");
        Assert.DoesNotContain(spring, o => o.StartUtc.UtcDateTime.Date == new DateTime(2026, 3, 8));
        var fall = WeeklyOccurrenceCalculator.Around(new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), 1, 90, 3000, "America/New_York");
        var occurrence = Assert.Single(fall, o => o.StartUtc.UtcDateTime.Date == new DateTime(2026, 11, 1));
        Assert.Equal(6, occurrence.StartUtc.Hour);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Clock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
