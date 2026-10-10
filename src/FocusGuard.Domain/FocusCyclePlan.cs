namespace FocusGuard.Domain;

public enum SessionMode
{
    SimpleTimer,
    FocusCycle
}

public enum SessionPhaseKind
{
    Focus,
    ShortBreak,
    LongBreak
}

public enum CycleTerminationMode
{
    CycleCount,
    TotalDuration
}

public sealed record FocusCycleConfiguration(
    TimeSpan FocusDuration,
    TimeSpan ShortBreakDuration,
    TimeSpan LongBreakDuration,
    int CyclesBeforeLongBreak,
    CycleTerminationMode TerminationMode,
    int? CycleCount,
    TimeSpan? TotalDuration)
{
    public const int MinimumCycleCount = 1;
    public const int MaximumCycleCount = 24;
    public const int MinimumCyclesBeforeLongBreak = 2;
    public const int MaximumCyclesBeforeLongBreak = 12;
    public static readonly TimeSpan MinimumFocusDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumFocusDuration = TimeSpan.FromMinutes(180);
    public static readonly TimeSpan MinimumShortBreakDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumShortBreakDuration = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan MinimumLongBreakDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumLongBreakDuration = TimeSpan.FromMinutes(120);

    public static ValidationResult<FocusCycleConfiguration> Create(
        TimeSpan focusDuration,
        TimeSpan shortBreakDuration,
        TimeSpan longBreakDuration,
        int cyclesBeforeLongBreak,
        CycleTerminationMode terminationMode,
        int? cycleCount = null,
        TimeSpan? totalDuration = null)
    {
        var errors = new List<ValidationError>();

        AddDurationError(
            errors,
            "cycle.focus_duration.invalid",
            "Focus duration must be between one and 180 minutes.",
            focusDuration,
            MinimumFocusDuration,
            MaximumFocusDuration);
        AddDurationError(
            errors,
            "cycle.short_break_duration.invalid",
            "Short-break duration must be between one and 60 minutes.",
            shortBreakDuration,
            MinimumShortBreakDuration,
            MaximumShortBreakDuration);
        AddDurationError(
            errors,
            "cycle.long_break_duration.invalid",
            "Long-break duration must be between one and 120 minutes.",
            longBreakDuration,
            MinimumLongBreakDuration,
            MaximumLongBreakDuration);

        if (cyclesBeforeLongBreak is < MinimumCyclesBeforeLongBreak or > MaximumCyclesBeforeLongBreak)
        {
            errors.Add(new ValidationError(
                "cycle.long_break_frequency.invalid",
                "Long breaks must occur every 2 to 12 focus cycles."));
        }

        if (!Enum.IsDefined(terminationMode))
        {
            errors.Add(new ValidationError(
                "cycle.termination_mode.invalid",
                "Cycle termination mode is invalid."));
        }
        else if (terminationMode == CycleTerminationMode.CycleCount)
        {
            if (cycleCount is null or < MinimumCycleCount or > MaximumCycleCount)
            {
                errors.Add(new ValidationError(
                    "cycle.count.invalid",
                    "Cycle count must be between one and 24."));
            }

            if (totalDuration is not null)
            {
                errors.Add(new ValidationError(
                    "cycle.total_duration.unexpected",
                    "Total duration must be empty when terminating by cycle count."));
            }
        }
        else
        {
            if (totalDuration is null
                || totalDuration < Session.MinimumDuration
                || totalDuration > Session.MaximumDuration)
            {
                errors.Add(new ValidationError(
                    "cycle.total_duration.invalid",
                    "Total duration must be between one minute and 30 days."));
            }

            if (cycleCount is not null)
            {
                errors.Add(new ValidationError(
                    "cycle.count.unexpected",
                    "Cycle count must be empty when terminating by total duration."));
            }
        }

        return errors.Count == 0
            ? ValidationResult<FocusCycleConfiguration>.Success(
                new FocusCycleConfiguration(
                    focusDuration,
                    shortBreakDuration,
                    longBreakDuration,
                    cyclesBeforeLongBreak,
                    terminationMode,
                    cycleCount,
                    totalDuration))
            : ValidationResult<FocusCycleConfiguration>.Failure([.. errors]);
    }

    private static void AddDurationError(
        ICollection<ValidationError> errors,
        string code,
        string message,
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(new ValidationError(code, message));
        }
    }
}

public sealed record SessionPhase(
    int Index,
    SessionPhaseKind Kind,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int FocusCycleNumber)
{
    public TimeSpan Duration => EndUtc - StartUtc;

    public bool IsPartial(TimeSpan plannedDuration) => Duration < plannedDuration;
}

public sealed record FocusCyclePlan(
    FocusCycleConfiguration Configuration,
    IReadOnlyList<SessionPhase> Phases,
    DateTimeOffset EndUtc)
{
    public int PlannedFocusCycles =>
        Phases.Count(phase => phase.Kind == SessionPhaseKind.Focus);

    public int CompleteFocusCycles =>
        Phases.Count(phase =>
            phase.Kind == SessionPhaseKind.Focus
            && phase.Duration == Configuration.FocusDuration);

    public CycleHistoryMetrics CompletedMetricsAt(DateTimeOffset completedThroughUtc)
    {
        if (completedThroughUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Cycle history metrics require a UTC boundary.", nameof(completedThroughUtc));
        }

        var completed = Phases.Where(phase =>
            phase.EndUtc <= completedThroughUtc
            && phase.Duration == PlannedDurationFor(phase.Kind)).ToArray();
        return new(
            completed.Count(phase => phase.Kind == SessionPhaseKind.Focus),
            completed.Count(phase => phase.Kind is SessionPhaseKind.ShortBreak or SessionPhaseKind.LongBreak));
    }

    public SessionPhase? FinalPhase => Phases.Count == 0 ? null : Phases[^1];

    public bool HasPartialFinalPhase => FinalPhase is { } phase
        && phase.Duration < PlannedDurationFor(phase.Kind);

    private TimeSpan PlannedDurationFor(SessionPhaseKind kind) => kind switch
    {
        SessionPhaseKind.Focus => Configuration.FocusDuration,
        SessionPhaseKind.ShortBreak => Configuration.ShortBreakDuration,
        SessionPhaseKind.LongBreak => Configuration.LongBreakDuration,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public sealed record CycleHistoryMetrics(int CompletedFocusCycles, int CompletedBreaks)
{
    public bool IsValid() => CompletedFocusCycles is >= 0 and <= FocusCycleConfiguration.MaximumCycleCount
        && CompletedBreaks is >= 0 and <= FocusCycleConfiguration.MaximumCycleCount;
}

public static class FocusCyclePlanBuilder
{
    public static ValidationResult<FocusCyclePlan> Build(
        FocusCycleConfiguration configuration,
        DateTimeOffset startUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (startUtc.Offset != TimeSpan.Zero)
        {
            return ValidationResult<FocusCyclePlan>.Failure(
                new ValidationError(
                    "cycle.start.not_utc",
                    "Cycle plan start time must be expressed in UTC."));
        }

        var configurationValidation = FocusCycleConfiguration.Create(
            configuration.FocusDuration,
            configuration.ShortBreakDuration,
            configuration.LongBreakDuration,
            configuration.CyclesBeforeLongBreak,
            configuration.TerminationMode,
            configuration.CycleCount,
            configuration.TotalDuration);
        if (!configurationValidation.IsValid)
        {
            return ValidationResult<FocusCyclePlan>.Failure([.. configurationValidation.Errors]);
        }

        return configuration.TerminationMode == CycleTerminationMode.CycleCount
            ? BuildByCycleCount(configuration, startUtc)
            : BuildByTotalDuration(configuration, startUtc);
    }

    private static ValidationResult<FocusCyclePlan> BuildByCycleCount(
        FocusCycleConfiguration configuration,
        DateTimeOffset startUtc)
    {
        var phases = new List<SessionPhase>();
        var cursor = startUtc;
        var cycleCount = configuration.CycleCount!.Value;

        for (var cycle = 1; cycle <= cycleCount; cycle++)
        {
            phases.Add(CreatePhase(
                phases.Count,
                SessionPhaseKind.Focus,
                cursor,
                configuration.FocusDuration,
                cycle));
            cursor = phases[^1].EndUtc;

            if (cycle == cycleCount)
            {
                continue;
            }

            var breakKind = cycle % configuration.CyclesBeforeLongBreak == 0
                ? SessionPhaseKind.LongBreak
                : SessionPhaseKind.ShortBreak;
            var breakDuration = breakKind == SessionPhaseKind.LongBreak
                ? configuration.LongBreakDuration
                : configuration.ShortBreakDuration;
            phases.Add(CreatePhase(phases.Count, breakKind, cursor, breakDuration, cycle));
            cursor = phases[^1].EndUtc;
        }

        return ValidationResult<FocusCyclePlan>.Success(
            new FocusCyclePlan(configuration, Array.AsReadOnly(phases.ToArray()), cursor));
    }

    private static ValidationResult<FocusCyclePlan> BuildByTotalDuration(
        FocusCycleConfiguration configuration,
        DateTimeOffset startUtc)
    {
        var endUtc = startUtc.Add(configuration.TotalDuration!.Value);
        var phases = new List<SessionPhase>();
        var cursor = startUtc;
        var cycle = 1;
        var completeFocusCycles = 0;

        while (cursor < endUtc)
        {
            var remaining = endUtc - cursor;
            var focusDuration = Min(remaining, configuration.FocusDuration);
            var focusIsPartial = focusDuration < configuration.FocusDuration;
            if (!focusIsPartial && ++completeFocusCycles > FocusCycleConfiguration.MaximumCycleCount)
            {
                return ValidationResult<FocusCyclePlan>.Failure(
                    new ValidationError(
                        "cycle.complete_count.exceeded",
                        "A total-duration plan may contain at most 24 complete focus cycles."));
            }

            phases.Add(CreatePhase(
                phases.Count,
                SessionPhaseKind.Focus,
                cursor,
                focusDuration,
                cycle));
            cursor = phases[^1].EndUtc;
            if (cursor == endUtc)
            {
                break;
            }

            var breakKind = cycle % configuration.CyclesBeforeLongBreak == 0
                ? SessionPhaseKind.LongBreak
                : SessionPhaseKind.ShortBreak;
            var breakDuration = breakKind == SessionPhaseKind.LongBreak
                ? configuration.LongBreakDuration
                : configuration.ShortBreakDuration;
            var actualBreakDuration = Min(endUtc - cursor, breakDuration);
            phases.Add(CreatePhase(
                phases.Count,
                breakKind,
                cursor,
                actualBreakDuration,
                cycle));
            cursor = phases[^1].EndUtc;
            if (cursor == endUtc)
            {
                break;
            }

            cycle++;
        }

        return ValidationResult<FocusCyclePlan>.Success(
            new FocusCyclePlan(configuration, Array.AsReadOnly(phases.ToArray()), endUtc));
    }

    private static SessionPhase CreatePhase(
        int index,
        SessionPhaseKind kind,
        DateTimeOffset startUtc,
        TimeSpan duration,
        int cycle) =>
        new(index, kind, startUtc, startUtc.Add(duration), cycle);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;
}
