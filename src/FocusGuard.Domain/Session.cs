namespace FocusGuard.Domain;

public enum SessionState
{
    Draft,
    Activating,
    Active,
    Expired,
    Stopped,
    RecoveryPending,
    Recovered,
    Failed
}

public sealed record StrictModeOptions(bool Enabled, TimeSpan RecoveryDelay)
{
    public static StrictModeOptions Normal { get; } = new(false, TimeSpan.Zero);

    public bool IsValid => !Enabled
        ? RecoveryDelay == TimeSpan.Zero
        : RecoveryDelay >= TimeSpan.Zero && RecoveryDelay <= TimeSpan.FromHours(24);
}

public sealed record Session(
    Guid Id,
    ProfileId ProfileId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    SessionState State,
    StrictModeOptions StrictMode)
{
    /// <summary>
    /// The immutable phase plan for a focus-cycle session. A null plan preserves the
    /// original single-interval timer semantics.
    /// </summary>
    public FocusCyclePlan? CyclePlan { get; init; }

    public SessionMode Mode => CyclePlan is null ? SessionMode.SimpleTimer : SessionMode.FocusCycle;

    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MinimumScheduledDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(30);

    public static ValidationResult<Session> Create(
        ProfileId profileId,
        DateTimeOffset startUtc,
        TimeSpan duration,
        StrictModeOptions strictMode) => CreateCore(profileId, startUtc, duration, strictMode,
            MinimumDuration, allowStrict: true);

    /// <summary>
    /// Creates a normal session bounded by a scheduled end time. A service worker may
    /// wake after the occurrence begins, so this path may have less than one minute left.
    /// </summary>
    public static ValidationResult<Session> CreateScheduled(
        ProfileId profileId,
        DateTimeOffset startUtc,
        TimeSpan remainingDuration,
        StrictModeOptions strictMode) => CreateCore(profileId, startUtc, remainingDuration,
            strictMode, MinimumScheduledDuration, allowStrict: false);

    private static ValidationResult<Session> CreateCore(
        ProfileId profileId,
        DateTimeOffset startUtc,
        TimeSpan duration,
        StrictModeOptions strictMode,
        TimeSpan minimumDuration,
        bool allowStrict)
    {
        if (profileId is null || profileId.Value == Guid.Empty)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError("session.profile.required", "A valid profile is required."));
        }

        if (!startUtc.Offset.Equals(TimeSpan.Zero))
        {
            return ValidationResult<Session>.Failure(
                new ValidationError("session.start.not_utc", "Session start time must be expressed in UTC."));
        }

        if (duration < minimumDuration || duration > MaximumDuration)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError(allowStrict ? "session.duration.invalid" : "session.scheduled_duration.invalid",
                    allowStrict ? "Session duration must be between one minute and 30 days."
                        : "Scheduled session duration must be at least one second and no more than 30 days."));
        }

        if (strictMode is null || !strictMode.IsValid)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError("session.strict_options.invalid", "Strict-mode recovery settings are invalid."));
        }

        if (!allowStrict && strictMode.Enabled)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError("session.scheduled_strict.invalid", "Scheduled sessions cannot use strict mode."));
        }

        return ValidationResult<Session>.Success(
            new Session(
                Guid.NewGuid(),
                profileId,
                startUtc,
                startUtc.Add(duration),
                SessionState.Draft,
                strictMode));
    }

    public bool CanTransitionTo(SessionState next) =>
        State == next || (State, next) switch
        {
            (SessionState.Draft, SessionState.Activating) => true,
            (SessionState.Activating, SessionState.Active) => true,
            (SessionState.Activating, SessionState.Failed) => true,
            (SessionState.Activating, SessionState.Expired) => true,
            (SessionState.Activating, SessionState.Stopped) when !StrictMode.Enabled => true,
            (SessionState.Active, SessionState.Expired) => true,
            (SessionState.Active, SessionState.Stopped) when !StrictMode.Enabled => true,
            (SessionState.Active, SessionState.RecoveryPending) when StrictMode.Enabled => true,
            (SessionState.RecoveryPending, SessionState.Recovered) => true,
            _ => false
        };

    public Session TransitionTo(SessionState next)
    {
        if (State == next)
        {
            return this;
        }

        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException($"Invalid session transition: {State} -> {next}.");
        }

        return this with { State = next };
    }

    public bool IsExpiredAt(DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero && nowUtc >= EndUtc;

    public Session ExpireIfDue(DateTimeOffset nowUtc)
    {
        if (!IsExpiredAt(nowUtc)
            || State is not (SessionState.Activating or SessionState.Active))
        {
            return this;
        }

        return TransitionTo(SessionState.Expired);
    }
}
