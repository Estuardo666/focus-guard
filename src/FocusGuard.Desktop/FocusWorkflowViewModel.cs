using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Desktop;

public sealed record TodaySpanViewModel(ActivitySpan Span, string Objective, IBrush AccentBrush)
{
    public string TimeText => $"{Span.StartUtc.ToLocalTime():HH:mm}–{Span.EndUtc.ToLocalTime():HH:mm}";
    public string ProfileName => Span.ProfileName;
    public bool IsFocus => Span.Kind == "Focus";
    public string PhaseLabel => IsFocus ? UiStrings.FocusPhaseLabel : UiStrings.BreakStatus;
    public string DurationText => string.Format(CultureInfo.CurrentCulture, UiStrings.DurationMinutesShort, (int)Math.Floor((Span.EndUtc - Span.StartUtc).TotalMinutes));
    public string Outcome => Span.Completed ? UiStrings.SummaryCompleteTitle : UiStrings.SummaryStoppedTitle;
}

public sealed class ScheduleDayViewModel(DayOfWeek day, DateTime date)
{
    public DayOfWeek Day { get; } = day;
    public string Label => date.ToString("ddd", CultureInfo.CurrentCulture);
    public string DateLabel => date.ToString("dd MMM", CultureInfo.CurrentCulture);
    public ObservableCollection<ScheduleRowViewModel> Rows { get; } = [];
}

public sealed class ScheduleDayChoice(DayOfWeek day) : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selected = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    public DayOfWeek Day { get; } = day;
    public string Label => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(Day);
    public bool IsSelected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ScheduleRowViewModel(FocusGuard.Contracts.ScheduleView schedule, Func<ScheduleChangeRequest, Task> change, IBrush accentBrush, TimeProvider clock)
{
    public FocusGuard.Contracts.ScheduleView Schedule { get; } = schedule;
    public string Name => Schedule.Name;
    public string HomeStartText => Schedule.NextStartUtc?.ToLocalTime().ToString("ddd d MMM · HH:mm", CultureInfo.CurrentCulture) ?? UiStrings.ScheduleNoNext;
    public string ProfileName => Schedule.ProfileName;
    public IBrush AccentBrush { get; } = accentBrush;
    public string PolicyPreview => string.Format(CultureInfo.CurrentCulture, UiStrings.SchedulePolicyPreview, Schedule.ApplicationRuleCount, Schedule.WebsiteRuleCount);
    public string EnforcementPreview => Schedule.EnforcementEnabled ? UiStrings.ScheduleBlockingEnabled : UiStrings.ScheduleTimerOnly;
    public string NextPreview
    {
        get
        {
            var occurrence = WeeklyOccurrenceCalculator.Around(clock.GetUtcNow(), Schedule.Days, Schedule.StartMinute, Schedule.DurationMinutes * 60, Schedule.TimeZoneId).FirstOrDefault(o => o.StartUtc > clock.GetUtcNow() && (Schedule.LastOccurrenceUtc is null || o.StartUtc > Schedule.LastOccurrenceUtc));
            return occurrence is null ? UiStrings.ScheduleNoNext : string.Format(CultureInfo.CurrentCulture, UiStrings.ScheduleFirstOccurrence,
                occurrence.StartUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture), occurrence.EndUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
    }
    public string TimeText => $"{TimeSpan.FromMinutes(Schedule.StartMinute):hh\\:mm} · {string.Format(CultureInfo.CurrentCulture, UiStrings.DurationMinutesShort, Schedule.DurationMinutes)}";
    public string StateText => Schedule.Enabled ? UiStrings.ScheduleEnabled : UiStrings.SchedulePaused;
    public string LastResult => UiStrings.Get("ScheduleResult" + (Schedule.LastResult is "Started" or "Busy" or "Missed" or "Claimed" or "Rejected" ? Schedule.LastResult : "None"));
    public string ToggleLabel => Schedule.Enabled ? UiStrings.SchedulePause : UiStrings.ScheduleEnable;
    public ICommand ToggleCommand { get; } = new AsyncRuleCommand(() => change(new("Enable", schedule.Id, Enabled: !schedule.Enabled)), () => true);
    public ICommand DeleteCommand { get; } = new AsyncRuleCommand(() => change(new("Delete", schedule.Id)), () => true);
}

public sealed partial class FocusViewModel
{
    private SessionJournalWorkflow _journal = null!;
    private string _objectiveInput = "";
    private Guid? _observedSessionId;
    private Guid? _observedProfileId;
    private SessionPhaseKind? _workflowPhaseKind;
    private DateTimeOffset? _workflowPhaseEnd;
    private SessionPhaseKind? _workflowNextKind;
    private bool _workflowActive;
    private bool _workflowPreferencesRestored;
    private long _todayFocusSeconds;
    private int _todayCompletedSessions;
    private bool _todayLoaded;
    private string _todayError = "";
    private bool _todayTruncated;
    private string _scheduleName = "";
    private string _scheduleTime = "09:00";
    private int _scheduleDuration = 50;
    private bool _scheduleEnabled;
    private string _scheduleMessage = "";
    private bool _scheduleHasError;
    private DateTimeOffset? _nextScheduleStart;

    private void InitializeWorkflows()
    {
        _journal = new SessionJournalWorkflow(
            new SessionJournalStore(_settingsStore.Persists ? Path.Combine(_settingsStore.DataDirectory, "session-journal.json") : null),
            _settingsStore, _timeProvider);
        SelectedDurationMinutes = _settings.LastDurationMinutes;
        ScheduleDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        SaveScheduleCommand = new AsyncDelegateCommand(SaveScheduleAsync, () => !IsBusy && !IsActive && IsSessionStateFresh);
        RefreshTodayCommand = new AsyncDelegateCommand(() => LoadTodayAsync(), () => !IsBusy);
        RefreshSchedulesCommand = new AsyncDelegateCommand(() => LoadSchedulesAsync(), () => !IsBusy);
        RefreshApplicationsCommand = new AsyncDelegateCommand(LoadApplicationsAsync, () => !IsBusy && !IsActive && IsSessionStateFresh);
        foreach (var day in ScheduleDayChoices) day.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ScheduleFirstRunText));
    }

    public int QuickStartDurationMinutes => IsCycleMode && BuildCyclePreview() is { } plan
        ? (int)Math.Ceiling(plan.Phases.Sum(p => p.Duration.TotalMinutes)) : SelectedDurationMinutes;
    public string QuickStartSummary => string.Format(CultureInfo.CurrentCulture, UiStrings.QuickStartSummary, ProfileName, QuickStartDurationMinutes);
    public string ObjectiveInput { get => _objectiveInput; set => SetField(ref _objectiveInput, (value ?? "")[..Math.Min((value ?? "").Length, 240)]); }
    public string ActiveObjective => _observedSessionId is { } id ? _journal.Get(id)?.Objective ?? "" : "";
    public bool HasActiveObjective => !string.IsNullOrWhiteSpace(ActiveObjective);
    public string ActiveProfileName => _observedSessionId is { } id && _journal.Get(id) is { } entry ? entry.ProfileName
        : Profiles.FirstOrDefault(p => p.Id.Value == _observedProfileId)?.Name ?? ProfileName;
    // NextPhase.RemainingSeconds is the duration of the future phase; its start is the current phase end.
    public string NextBreakText => _nextPhase is { } next && _currentPhase is { } current
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.NextPhaseAt, next.Kind == SessionPhaseKind.Focus ? UiStrings.TimerMode : UiStrings.BreakStatus, current.EndUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)) : UiStrings.NoNextBreak;
    public string CompactPhaseLabel => IsActive ? CurrentPhaseLabel : UiStrings.ReadyTitle;
    public string CompactRemainingText => IsActive ? RingCenterValue : FormatRemaining(0);
    public bool CompactHasWarning => !IsSessionStateFresh || ServiceHealthLabel == UiStrings.Get("ServiceUnavailable") || IsProtectionActionable;
    public string CompactWarningText => !IsSessionStateFresh ? ConnectionNotice : ServiceHealthLabel == UiStrings.Get("ServiceUnavailable") ? ServiceHealthLabel : ProtectionSummaryText;
    public bool CompactTopmost
    {
        get => _settings.CompactTopmost;
        set { _settings = _settings with { CompactTopmost = value }; _settingsStore.Save(_settings); OnPropertyChanged(); }
    }

    private void RememberAcceptedSession(Guid? sessionId)
    {
        if (sessionId is not { } id || SelectedProfile is null) return;
        _settings = _journal.RememberAccepted(
            new SessionIntention(id, SelectedProfile.Id.Value, SelectedProfile.Name,
                QuickStartDurationMinutes, IsCycleMode ? SessionMode.FocusCycle : SessionMode.SimpleTimer,
                IsCycleMode ? SelectedCyclePreset?.Id : null, IsCycleMode ? SelectedCyclePreset?.Configuration : null,
                IsStrictMode, ObjectiveInput, EnforcementEnabled: IsEnforcementEnabled), _settings);
    }
    private void ObserveWorkflowStatus(StatusResponse status)
    {
        var identityChanged = _observedSessionId != status.SessionId || _observedProfileId != status.ProfileId;
        var phaseChanged = _workflowPhaseKind != status.CurrentPhase?.Kind || _workflowPhaseEnd != status.CurrentPhase?.EndUtc || _workflowNextKind != status.NextPhase?.Kind;
        var activeChanged = _workflowActive != IsActive;
        _observedSessionId = status.SessionId;
        _observedProfileId = status.ProfileId;
        _workflowPhaseKind = status.CurrentPhase?.Kind;
        _workflowPhaseEnd = status.CurrentPhase?.EndUtc;
        _workflowNextKind = status.NextPhase?.Kind;
        _workflowActive = IsActive;
        if (identityChanged)
        {
            _journal.RecordObservedSession(status, Profiles.FirstOrDefault(p => p.Id.Value == status.ProfileId), IsActive);
            OnPropertyChanged(nameof(ActiveObjective)); OnPropertyChanged(nameof(HasActiveObjective)); OnPropertyChanged(nameof(ActiveProfileName)); OnPropertyChanged(nameof(SessionHeadlineText));
        }
        if (phaseChanged || activeChanged) { OnPropertyChanged(nameof(NextBreakText)); OnPropertyChanged(nameof(CompactPhaseLabel)); }
        if (IsActive && Section is NavigationSection.Profiles or NavigationSection.Schedule) ShowFocus();
    }
    public SessionIntention? GetIntention(Guid? id) => id is { } value ? _journal.Get(value) : null;
    public void SaveReflection(Guid? id, string reflection) => _journal.SaveReflection(id, reflection);
    public async Task RepeatSessionAsync(Guid? id, int fallbackMinutes)
    {
        if (IsActive || IsBusy || !IsSessionStateFresh) return;
        var intention = _journal.GetLatest(id);
        if (intention?.ProfileId == Guid.Empty) intention = null;
        if (intention is not null)
        {
            var profile = Profiles.FirstOrDefault(p => p.Id.Value == intention.ProfileId);
            if (profile is null) { StatusMessage = UiStrings.RepeatProfileMissing; return; }
            SelectedProfile = profile;
            SelectedSessionMode = SessionModes.First(m => m.Mode == intention.Mode);
            SelectedDurationMinutes = intention.DurationMinutes;
            if (intention.CycleConfiguration is { } configuration)
                SelectedCyclePreset = new(intention.CyclePresetId ?? Guid.NewGuid(), UiStrings.RepeatSession, configuration, true);
            ObjectiveInput = intention.Objective;
            IsStrictMode = intention.Strict; // StartFocusAsync still requires fresh strict review and recovery acknowledgement.
        }
        else { SelectedSessionMode = SessionModes.First(m => m.Mode == SessionMode.SimpleTimer); SelectedDurationMinutes = Math.Clamp(fallbackMinutes, 1, 720); IsStrictMode = false; }
        ShowFocus();
        await StartFocusAsync();
    }

    public ObservableCollection<TodaySpanViewModel> TodayTimeline { get; } = [];
    public long TodayFocusMinutes => _todayFocusSeconds / 60;
    public int TodayCompletedSessions => _todayCompletedSessions;
    public string TodayDateLabel => _timeProvider.GetLocalNow().ToString("D", CultureInfo.CurrentCulture);
    public string TodayError => _todayError;
    public string TechnicalLogError { get; private set; } = "";
    public bool IsTodayLoaded => _todayLoaded;
    public bool IsTodayEmpty => _todayLoaded && TodayTimeline.Count == 0;
    public bool TodayTimelineTruncated => _todayTruncated;
    public ICommand RefreshTodayCommand { get; private set; } = null!;
    public async Task LoadTodayAsync(CancellationToken token = default)
    {
        try
        {
            var day = _timeProvider.GetLocalNow().Date;
            var zone = _timeProvider.LocalTimeZone;
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
            var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.AddDays(1), DateTimeKind.Unspecified), zone), TimeSpan.Zero);
            var response = await _client.GetTodayAsync(new(start, end), token);
            _todayFocusSeconds = response.FocusSeconds;
            _todayCompletedSessions = response.CompletedSessions;
            _todayLoaded = true;
            _todayError = "";
            _todayTruncated = response.TimelineTruncated;
            TodayTimeline.Clear();
            foreach (var span in response.Timeline) TodayTimeline.Add(new(span, _journal.Get(span.SessionId)?.Objective ?? "", WorkflowProfileBrush(span.ProfileId)));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
        { _todayError = UiStrings.TodayUnavailable; _todayLoaded = false; }
        foreach (var property in new[] { nameof(TodayFocusMinutes), nameof(TodayCompletedSessions), nameof(IsTodayEmpty), nameof(IsTodayLoaded), nameof(TodayError), nameof(TodayTimelineTruncated), nameof(TodayDateLabel) }) OnPropertyChanged(property);
    }

    public IReadOnlyList<DayOfWeek> ScheduleDays { get; private set; } = [];
    public IReadOnlyList<ScheduleDayChoice> ScheduleDayChoices { get; } = Enum.GetValues<DayOfWeek>().OrderBy(d => ((int)d + 6) % 7).Select(d => new ScheduleDayChoice(d)).ToArray();
    public ObservableCollection<ScheduleDayViewModel> ScheduleWeek { get; } = [];
    public ObservableCollection<ScheduleRowViewModel> Schedules { get; } = [];
    public string ScheduleName { get => _scheduleName; set => SetField(ref _scheduleName, (value ?? "")[..Math.Min((value ?? "").Length, 80)]); }
    public string ScheduleTime { get => _scheduleTime; set { SetField(ref _scheduleTime, value ?? ""); OnPropertyChanged(nameof(ScheduleEndPreview)); OnPropertyChanged(nameof(ScheduleFirstRunText)); } }
    public int ScheduleDuration { get => _scheduleDuration; set { SetField(ref _scheduleDuration, Math.Clamp(value, 1, 720)); OnPropertyChanged(nameof(ScheduleEndPreview)); OnPropertyChanged(nameof(ScheduleFirstRunText)); } }
    public bool ScheduleEnabled { get => _scheduleEnabled; set => SetField(ref _scheduleEnabled, value); }
    public string ScheduleMessage => _scheduleMessage;
    public bool ScheduleHasError => _scheduleHasError;
    public string ScheduleZoneLabel => TimeZoneInfo.Local.DisplayName;
    public string ScheduleEndPreview => TimeOnly.TryParseExact(ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.ScheduleEndPreview, time.ToString("HH:mm", CultureInfo.InvariantCulture), time.AddMinutes(ScheduleDuration).ToString("HH:mm", CultureInfo.InvariantCulture) + (time.ToTimeSpan().TotalMinutes + ScheduleDuration >= 1440 ? UiStrings.ScheduleNextDay : ""), ProfileName)
        : UiStrings.ScheduleInvalidTime;
    public string ScheduleEnforcementPreview => IsEnforcementEnabled ? UiStrings.ScheduleBlockingEnabled : UiStrings.ScheduleTimerOnly;
    public string ScheduleFirstRunText
    {
        get
        {
            if (!TimeOnly.TryParseExact(ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return UiStrings.ScheduleInvalidTime;
            var days = ScheduleDayChoices.Where(d => d.IsSelected).Sum(d => 1 << (int)d.Day);
            if (days == 0) return UiStrings.ScheduleDaysRequired;
            var occurrence = WeeklyOccurrenceCalculator.Around(_timeProvider.GetUtcNow(), days, time.Hour * 60 + time.Minute, ScheduleDuration * 60, TimeZoneInfo.Local.Id).First(o => o.StartUtc > _timeProvider.GetUtcNow());
            return string.Format(CultureInfo.CurrentCulture, UiStrings.ScheduleFirstOccurrence, occurrence.StartUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture), occurrence.EndUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
    }
    public string NextScheduledSession => _nextScheduleStart is { } start ? string.Format(CultureInfo.CurrentCulture, UiStrings.ScheduleNext, start.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)) : UiStrings.ScheduleNoNext;
    public bool HasSchedules => Schedules.Count > 0;
    public ICommand SaveScheduleCommand { get; private set; } = null!;
    public ICommand RefreshSchedulesCommand { get; private set; } = null!;

    public async Task LoadSchedulesAsync(CancellationToken token = default)
    {
        try { ApplySchedules(await _client.GetSchedulesAsync(token)); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
        {
            SetScheduleMessage(UiStrings.ScheduleUnavailable);
            SetHomeScheduleAvailability(false);
        }
    }
    private async Task SaveScheduleAsync()
    {
        if (SelectedProfile is null || IsActive || IsBusy || !IsSessionStateFresh) return;
        if (string.IsNullOrWhiteSpace(ScheduleName)) { SetScheduleMessage(UiStrings.ScheduleNameRequired); return; }
        if (!TimeOnly.TryParseExact(ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { SetScheduleMessage(UiStrings.ScheduleInvalidTime); return; }
        var days = ScheduleDayChoices.Where(d => d.IsSelected).Sum(d => 1 << (int)d.Day);
        if (days == 0) { SetScheduleMessage(UiStrings.ScheduleDaysRequired); return; }
        var id = Guid.NewGuid();
        var schedule = new WeeklySchedule(id, ScheduleName.Trim(), days, time.Hour * 60 + time.Minute, TimeZoneInfo.Local.Id, ScheduleEnabled,
            new(SelectedProfile.Id.Value, ScheduleDuration * 60, false, 0, ApplicationRules: SelectedProfile.ApplicationRules.Select(r => new ApplicationPolicyRule(r.Id, r.Action.ToString(), r.MatchKind.ToString(), r.Value)).ToArray(),
                WebsiteRules: SelectedProfile.WebsiteRules.Select(r => new WebsitePolicyRule(r.Id, r.Action.ToString(), r.Pattern)).ToArray(), EnforcementEnabled: IsEnforcementEnabled, ProfileName: SelectedProfile.Name));
        await ChangeScheduleAsync(new("Save", id, schedule));
    }
    private async Task ChangeScheduleAsync(ScheduleChangeRequest change)
    {
        if (IsBusy || IsActive || !IsSessionStateFresh) return;
        IsBusy = true;
        try { ApplySchedules(await _client.ChangeScheduleAsync(change, CancellationToken.None)); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or OperationCanceledException) { SetScheduleMessage(UiStrings.ScheduleUnavailable); MarkUnconfirmedMutation(ex); }
        finally { IsBusy = false; }
    }
    private void ApplySchedules(ScheduleResponse response)
    {
        SetHomeScheduleAvailability(response.Accepted || response.Schedules.Count > 0);
        SetScheduleMessage(response.Accepted ? (response.Reason == "Saved" ? UiStrings.ScheduleSaved : "")
            : response.Reason == "Overlap" ? UiStrings.ScheduleOverlap : response.Reason == "Limit" ? UiStrings.ScheduleLimit : UiStrings.ScheduleUnavailable, !response.Accepted);
        Schedules.Clear();
        foreach (var schedule in response.Schedules) Schedules.Add(new(schedule, ChangeScheduleAsync, WorkflowProfileBrush(schedule.ProfileId), _timeProvider));
        _nextScheduleStart = response.Schedules.Where(s => s.NextStartUtc is not null).Select(s => s.NextStartUtc).Min();
        ScheduleWeek.Clear();
        var today = _timeProvider.GetLocalNow().Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        for (var i = 0; i < 7; i++)
        {
            var day = new ScheduleDayViewModel(monday.AddDays(i).DayOfWeek, monday.AddDays(i));
            foreach (var row in Schedules.Where(r => (r.Schedule.Days & 1 << (int)day.Day) != 0).OrderBy(r => r.Schedule.StartMinute)) day.Rows.Add(row);
            ScheduleWeek.Add(day);
        }
        OnPropertyChanged(nameof(HasSchedules)); OnPropertyChanged(nameof(NextScheduledSession));
    }
    private void SetScheduleMessage(string message, bool error = true) { _scheduleMessage = message; _scheduleHasError = error; OnPropertyChanged(nameof(ScheduleMessage)); OnPropertyChanged(nameof(ScheduleHasError)); }
    private IBrush WorkflowProfileBrush(Guid? id) => Profiles.FirstOrDefault(p => p.Id.Value == id)?.AccentHex is { } hex && Color.TryParse(hex, out _)
        ? CreateFrozenBrush(hex) : DefaultAccentBrush;

    private void RestoreWorkflowPreferences()
    {
        if (_workflowPreferencesRestored) return;
        _workflowPreferencesRestored = true;
        if (_settings.LastSessionMode == SessionMode.FocusCycle && CyclePresets.FirstOrDefault(p => p.Id == _settings.LastCyclePresetId) is { } preset)
        { SelectedCyclePreset = preset; SelectedSessionMode = SessionModes.First(m => m.Mode == SessionMode.FocusCycle); }
    }
}
