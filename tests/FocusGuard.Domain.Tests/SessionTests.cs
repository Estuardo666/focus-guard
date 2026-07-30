using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class SessionTests
{
    [Fact]
    public void Create_RejectsDurationBelowOneMinute()
    {
        var result = Session.Create(
            ProfileId.New(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(59),
            StrictModeOptions.Normal);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void StrictActiveSession_CannotStopNormally()
    {
        var created = Session.Create(
            ProfileId.New(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            new StrictModeOptions(true, TimeSpan.FromMinutes(15)));

        var active = created.Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);

        Assert.False(active.CanTransitionTo(SessionState.Stopped));
        Assert.True(active.CanTransitionTo(SessionState.RecoveryPending));
    }

    [Fact]
    public void PolicyCopiesRuleCollections()
    {
        var session = Session.Create(
            ProfileId.New(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1),
            StrictModeOptions.Normal).Value!;

        var source = new List<ApplicationRule>
        {
            new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "example.exe")
        };

        var policy = EnforcementPolicy.Create(session, source, Array.Empty<WebsiteRule>());
        source.Clear();

        Assert.Single(policy.ApplicationRules);
    }

    [Fact]
    public void Create_RejectsNonUtcStartTime()
    {
        var result = Session.Create(
            ProfileId.New(),
            new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5)),
            TimeSpan.FromMinutes(1),
            StrictModeOptions.Normal);

        Assert.Contains(result.Errors, error => error.Code == "session.start.not_utc");
    }

    [Fact]
    public void Create_RejectsInvalidStrictRecoveryDelay()
    {
        var result = Session.Create(
            ProfileId.New(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1),
            new StrictModeOptions(true, TimeSpan.FromDays(2)));

        Assert.Contains(result.Errors, error => error.Code == "session.strict_options.invalid");
    }

    [Fact]
    public void TransitionToSameState_IsIdempotent()
    {
        var session = Session.Create(
            ProfileId.New(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1),
            StrictModeOptions.Normal).Value!;

        Assert.Same(session, session.TransitionTo(SessionState.Draft));
    }

    [Fact]
    public void ExpireIfDue_ExpiresActiveSessionAtEndTime()
    {
        var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var session = Session.Create(
                ProfileId.New(),
                start,
                TimeSpan.FromMinutes(1),
                StrictModeOptions.Normal).Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);

        var expired = session.ExpireIfDue(start.AddMinutes(1));

        Assert.Equal(SessionState.Expired, expired.State);
        Assert.Same(expired, expired.ExpireIfDue(start.AddMinutes(2)));
    }

    [Fact]
    public void ExpireIfDue_DoesNotExpireBeforeEndTimeOrWithNonUtcInput()
    {
        var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var session = Session.Create(
                ProfileId.New(),
                start,
                TimeSpan.FromMinutes(1),
                StrictModeOptions.Normal).Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);

        Assert.Same(session, session.ExpireIfDue(start.AddSeconds(59)));
        Assert.Same(session, session.ExpireIfDue(new DateTimeOffset(2030, 1, 1, 7, 1, 0, TimeSpan.FromHours(-5))));
    }
}
