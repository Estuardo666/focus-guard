using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class PolicyDeliveryTests
{
    [Fact]
    public void SimpleTimerHashMatchesTheBrowserIntegrityContract()
    {
        var policy = new EnforcementPolicy(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new ProfileId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 13, 0, 0, TimeSpan.Zero),
            IsStrict: false,
            ApplicationRules:
            [
                new ApplicationRule(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    RuleAction.Block,
                    ApplicationMatchKind.FileName,
                    "chat.exe")
            ],
            WebsiteRules: [],
            EnforcementEnabled: true,
            CyclePlan: null,
            BreakApplicationRules: [],
            BreakWebsiteRules: [],
            AuthorizedPhaseIndex: -1,
            AuthorizedPhaseEndUtc: null);
        var delivery = new PolicyDelivery(
            PolicyDelivery.CurrentSchemaVersion,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "6070D18C319F2FB9CDB748F90FD3E98716D8815DBF716EE6C46DE32E4BE126B2",
            policy);

        Assert.True(delivery.HasValidIntegrity());
    }

    [Fact]
    public void CycleCountHashMatchesTheBrowserIntegrityContract()
    {
        var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var configuration = new FocusCycleConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.CycleCount,
            CycleCount: 1,
            TotalDuration: null);
        var plan = new FocusCyclePlan(
            configuration,
            [new SessionPhase(0, SessionPhaseKind.Focus, start, start.AddMinutes(25), 1)],
            start.AddMinutes(25));
        var policy = new EnforcementPolicy(
            Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"),
            new ProfileId(Guid.Parse("cccccccc-3333-3333-3333-333333333333")),
            start,
            start.AddMinutes(25),
            IsStrict: false,
            ApplicationRules: [],
            WebsiteRules:
            [
                new WebsiteRule(
                    Guid.Parse("dddddddd-4444-4444-4444-444444444444"),
                    RuleAction.Block,
                    "example.com")
            ],
            EnforcementEnabled: true,
            CyclePlan: plan,
            BreakApplicationRules: [],
            BreakWebsiteRules: [],
            AuthorizedPhaseIndex: -1,
            AuthorizedPhaseEndUtc: null);
        var delivery = new PolicyDelivery(
            PolicyDelivery.CurrentSchemaVersion,
            Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
            "D7E663D452D0DDC6215D9C963252FC34CB0A8483B673782519ECAB7F8FD6D0D4",
            policy);

        Assert.True(delivery.HasValidIntegrity());
    }

    [Fact]
    public void AuthorizedBreakReplacesFocusRulesUntilItsExactEnd()
    {
        var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var configuration = FocusCycleConfiguration.Create(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            2,
            CycleTerminationMode.CycleCount,
            cycleCount: 2).Value!;
        var plan = FocusCyclePlanBuilder.Build(configuration, start).Value!;
        var session = Session.Create(ProfileId.New(), start, plan.EndUtc - start, StrictModeOptions.Normal)
            .Value! with
        { CyclePlan = plan };
        var blocked = new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe");
        var allowedDuringBreak = new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "chat.exe");
        var policy = EnforcementPolicy.Create(session, [blocked], [], breakApplicationRules: [allowedDuringBreak]) with
        {
            AuthorizedPhaseIndex = 1,
            AuthorizedPhaseEndUtc = plan.Phases[1].EndUtc
        };

        Assert.True(policy.IsAuthorizedBreak(start.AddMinutes(1)));
        Assert.Equal([allowedDuringBreak], policy.EffectiveApplicationRules(start.AddMinutes(1)));
        Assert.Equal([blocked], policy.EffectiveApplicationRules(start.AddMinutes(2)));
    }

    [Fact]
    public void PolicyDeliveryIntegrityCoversCyclePlanAndBreakExceptions()
    {
        var start = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var session = Session.Create(ProfileId.New(), start, TimeSpan.FromMinutes(2), StrictModeOptions.Normal).Value!;
        var policy = EnforcementPolicy.Create(
            session,
            [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
            [],
            breakApplicationRules:
            [new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "music.exe")]);
        var delivery = PolicyDelivery.Create(policy);
        var tampered = delivery with
        {
            Policy = policy with
            {
                BreakApplicationRules = [new ApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "other.exe")]
            }
        };

        Assert.True(delivery.HasValidIntegrity());
        Assert.False(tampered.HasValidIntegrity());
    }
}
