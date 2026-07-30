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
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(30);

    public static ValidationResult<Session> Create(
        ProfileId profileId,
        DateTimeOffset startUtc,
        TimeSpan duration,
        StrictModeOptions strictMode)
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

        if (duration < MinimumDuration || duration > MaximumDuration)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError(
                    "session.duration.invalid",
                    "Session duration must be between one minute and 30 days."));
        }

        if (strictMode is null || !strictMode.IsValid)
        {
            return ValidationResult<Session>.Failure(
                new ValidationError("session.strict_options.invalid", "Strict-mode recovery settings are invalid."));
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
