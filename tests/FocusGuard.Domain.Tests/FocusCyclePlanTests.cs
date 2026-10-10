using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class FocusCyclePlanTests
{
    private static readonly DateTimeOffset Start =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClassicFourCycles_HasThreeShortBreaksAndEndsAfterFinalFocus()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.CycleCount,
            cycleCount: 4);

        var result = FocusCyclePlanBuilder.Build(configuration, Start);

        Assert.True(result.IsValid);
        var plan = result.Value!;
        Assert.Equal(7, plan.Phases.Count);
        Assert.Equal(4, plan.Phases.Count(phase => phase.Kind == SessionPhaseKind.Focus));
        Assert.Equal(3, plan.Phases.Count(phase => phase.Kind == SessionPhaseKind.ShortBreak));
        Assert.DoesNotContain(plan.Phases, phase => phase.Kind == SessionPhaseKind.LongBreak);
        Assert.Equal(Start.AddMinutes(115), plan.EndUtc);
        Assert.Equal(SessionPhaseKind.Focus, plan.FinalPhase!.Kind);
    }

    [Fact]
    public void LongBreak_ReplacesShortBreakBetweenCompletedGroups()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.CycleCount,
            cycleCount: 8);

        var plan = FocusCyclePlanBuilder.Build(configuration, Start).Value!;

        Assert.Equal(
            [
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.LongBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus,
                SessionPhaseKind.ShortBreak,
                SessionPhaseKind.Focus
            ],
            plan.Phases.Select(phase => phase.Kind));
    }

    [Fact]
    public void TotalDuration_EndsExactlyAndMarksPartialFinalPhase()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.TotalDuration,
            totalDuration: TimeSpan.FromMinutes(57));

        var plan = FocusCyclePlanBuilder.Build(configuration, Start).Value!;

        Assert.Equal(Start.AddMinutes(57), plan.EndUtc);
        Assert.Equal(plan.EndUtc, plan.FinalPhase!.EndUtc);
        Assert.Equal(SessionPhaseKind.ShortBreak, plan.FinalPhase.Kind);
        Assert.Equal(TimeSpan.FromMinutes(2), plan.FinalPhase.Duration);
        Assert.True(plan.HasPartialFinalPhase);
    }

    [Theory]
    [InlineData(30, 0, 0)]
    [InlineData(60, 1, 0)]
    [InlineData(120, 1, 1)]
    [InlineData(179, 1, 1)]
    [InlineData(180, 2, 1)]
    public void CompletedMetricsAt_ExcludesPartialAndFuturePhases(int elapsedSeconds, int focusCycles, int breaks)
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            2,
            CycleTerminationMode.CycleCount,
            cycleCount: 2);
        var plan = FocusCyclePlanBuilder.Build(configuration, Start).Value!;

        var metrics = plan.CompletedMetricsAt(Start.AddSeconds(elapsedSeconds));

        Assert.Equal(new CycleHistoryMetrics(focusCycles, breaks), metrics);
    }

    [Fact]
    public void TotalDurationShorterThanFocus_ContainsOnlyPartialFocus()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.TotalDuration,
            totalDuration: TimeSpan.FromMinutes(10));

        var plan = FocusCyclePlanBuilder.Build(configuration, Start).Value!;

        var phase = Assert.Single(plan.Phases);
        Assert.Equal(SessionPhaseKind.Focus, phase.Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), phase.Duration);
        Assert.True(plan.HasPartialFinalPhase);
    }

    [Fact]
    public void Configuration_RejectsMismatchedTerminationArgumentsAndOutOfRangeValues()
    {
        var result = FocusCycleConfiguration.Create(
            TimeSpan.FromMinutes(181),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(121),
            1,
            CycleTerminationMode.CycleCount,
            cycleCount: 25,
            totalDuration: TimeSpan.FromHours(1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "cycle.focus_duration.invalid");
        Assert.Contains(result.Errors, error => error.Code == "cycle.short_break_duration.invalid");
        Assert.Contains(result.Errors, error => error.Code == "cycle.long_break_duration.invalid");
        Assert.Contains(result.Errors, error => error.Code == "cycle.long_break_frequency.invalid");
        Assert.Contains(result.Errors, error => error.Code == "cycle.count.invalid");
        Assert.Contains(result.Errors, error => error.Code == "cycle.total_duration.unexpected");
    }

    [Fact]
    public void TotalDuration_RejectsMoreThan24CompleteFocusCycles()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1),
            2,
            CycleTerminationMode.TotalDuration,
            totalDuration: TimeSpan.FromMinutes(49));

        var result = FocusCyclePlanBuilder.Build(configuration, Start);

        Assert.Contains(result.Errors, error => error.Code == "cycle.complete_count.exceeded");
    }

    [Fact]
    public void Builder_RejectsNonUtcStart()
    {
        var configuration = CreateConfiguration(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.CycleCount,
            cycleCount: 4);

        var result = FocusCyclePlanBuilder.Build(
            configuration,
            new DateTimeOffset(2030, 1, 1, 7, 0, 0, TimeSpan.FromHours(-5)));

        Assert.Contains(result.Errors, error => error.Code == "cycle.start.not_utc");
    }

    private static FocusCycleConfiguration CreateConfiguration(
        TimeSpan focus,
        TimeSpan shortBreak,
        TimeSpan longBreak,
        int frequency,
        CycleTerminationMode terminationMode,
        int? cycleCount = null,
        TimeSpan? totalDuration = null) =>
        FocusCycleConfiguration.Create(
            focus,
            shortBreak,
            longBreak,
            frequency,
            terminationMode,
            cycleCount,
            totalDuration).Value!;
}
