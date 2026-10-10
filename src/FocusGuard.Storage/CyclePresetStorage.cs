using FocusGuard.Domain;

namespace FocusGuard.Storage;

public sealed record StoredCyclePreset(
    Guid Id,
    string Name,
    FocusCycleConfiguration Configuration,
    bool IsBuiltIn);

public sealed record BreakApplicationRule(
    Guid Id,
    RuleAction Action,
    ApplicationMatchKind MatchKind,
    string Value);

public sealed record BreakWebsiteRule(
    Guid Id,
    RuleAction Action,
    string Pattern);

public sealed record ProfileCycleSettings(
    ProfileId ProfileId,
    Guid? DefaultPresetId,
    IReadOnlyList<BreakApplicationRule> ApplicationRules,
    IReadOnlyList<BreakWebsiteRule> WebsiteRules);

public static class BuiltInCyclePresets
{
    public static readonly Guid ClassicId = Guid.Parse("a2af4f65-2de8-4e89-a0bc-4c4db6e24a01");
    public static readonly Guid DeepFocusId = Guid.Parse("a2af4f65-2de8-4e89-a0bc-4c4db6e24a02");
    public static readonly Guid ExtendedFlowId = Guid.Parse("a2af4f65-2de8-4e89-a0bc-4c4db6e24a03");

    public static IReadOnlyList<StoredCyclePreset> All { get; } =
    [
        new(
            ClassicId,
            "Pomodoro clásico",
            FocusCycleConfiguration.Create(
                TimeSpan.FromMinutes(25),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15),
                4,
                CycleTerminationMode.CycleCount,
                cycleCount: 4).Value!,
            true),
        new(
            DeepFocusId,
            "Enfoque profundo",
            FocusCycleConfiguration.Create(
                TimeSpan.FromMinutes(50),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(20),
                3,
                CycleTerminationMode.CycleCount,
                cycleCount: 3).Value!,
            true),
        new(
            ExtendedFlowId,
            "Flujo extendido",
            FocusCycleConfiguration.Create(
                TimeSpan.FromMinutes(90),
                TimeSpan.FromMinutes(20),
                TimeSpan.FromMinutes(30),
                2,
                CycleTerminationMode.CycleCount,
                cycleCount: 2).Value!,
            true)
    ];
}
