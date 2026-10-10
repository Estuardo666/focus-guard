using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

/// <summary>
/// Renders the complete cycle rhythm as one continuous bar. The service-owned phase
/// index and remaining time determine the completed and current fill; no timer lives here.
/// </summary>
public sealed class CyclePhaseTimeline : Control
{
    private readonly DispatcherTimer _animationTimer;
    private bool _isAttached;

    public static readonly StyledProperty<FocusCycleConfiguration?> ConfigurationProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, FocusCycleConfiguration?>(nameof(Configuration));

    public static readonly StyledProperty<PhaseStatus?> CurrentPhaseProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, PhaseStatus?>(nameof(CurrentPhase));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, IBrush?>(nameof(FocusBrush));

    public static readonly StyledProperty<IBrush?> FocusFutureBrushProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, IBrush?>(nameof(FocusFutureBrush));

    public static readonly StyledProperty<IBrush?> BreakBrushProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, IBrush?>(nameof(BreakBrush));

    public static readonly StyledProperty<IBrush?> BreakFutureBrushProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, IBrush?>(nameof(BreakFutureBrush));

    public static readonly StyledProperty<bool> IsReduceMotionProperty =
        AvaloniaProperty.Register<CyclePhaseTimeline, bool>(nameof(IsReduceMotion));

    public CyclePhaseTimeline()
    {
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _animationTimer.Tick += (_, _) => InvalidateVisual();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            UpdateAnimationTimer();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            _animationTimer.Stop();
        };
    }

    static CyclePhaseTimeline() => AffectsRender<CyclePhaseTimeline>(
        ConfigurationProperty,
        CurrentPhaseProperty,
        TrackBrushProperty,
        FocusBrushProperty,
        FocusFutureBrushProperty,
        BreakBrushProperty,
        BreakFutureBrushProperty,
        IsReduceMotionProperty);

    public FocusCycleConfiguration? Configuration { get => GetValue(ConfigurationProperty); set => SetValue(ConfigurationProperty, value); }
    public PhaseStatus? CurrentPhase { get => GetValue(CurrentPhaseProperty); set => SetValue(CurrentPhaseProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }
    public IBrush? FocusFutureBrush { get => GetValue(FocusFutureBrushProperty); set => SetValue(FocusFutureBrushProperty, value); }
    public IBrush? BreakBrush { get => GetValue(BreakBrushProperty); set => SetValue(BreakBrushProperty, value); }
    public IBrush? BreakFutureBrush { get => GetValue(BreakFutureBrushProperty); set => SetValue(BreakFutureBrushProperty, value); }
    public bool IsReduceMotion { get => GetValue(IsReduceMotionProperty); set => SetValue(IsReduceMotionProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentPhaseProperty || change.Property == IsReduceMotionProperty)
        {
            UpdateAnimationTimer();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var phases = BuildPhases(Configuration);
        if (phases.Count == 0 || Bounds.Width <= 0d || Bounds.Height <= 0d)
        {
            return;
        }

        var radius = Bounds.Height / 2d;
        context.DrawRectangle(TrackBrush, null, new RoundedRect(new Rect(Bounds.Size), radius));
        var totalSeconds = phases.Sum(phase => phase.Duration.TotalSeconds);
        var x = 0d;

        for (var index = 0; index < phases.Count; index++)
        {
            var phase = phases[index];
            var width = index == phases.Count - 1
                ? Bounds.Width - x
                : Bounds.Width * phase.Duration.TotalSeconds / totalSeconds;
            var futureBrush = phase.IsFocus ? FocusFutureBrush : BreakFutureBrush;
            context.DrawRectangle(futureBrush, null, new Rect(x, 0d, width, Bounds.Height));

            var completion = PhaseCompletion(CurrentPhase, phase, index, IsReduceMotion);
            if (completion > 0d)
            {
                var activeBrush = phase.IsFocus ? FocusBrush : BreakBrush;
                context.DrawRectangle(activeBrush, null, new Rect(x, 0d, width * completion, Bounds.Height));
            }

            x += width;
        }

        // Restores the pill silhouette after the contiguous rectangular phase fills.
        context.DrawRectangle(null, new Pen(TrackBrush, 1d), new RoundedRect(new Rect(Bounds.Size), radius));
    }

    private static double PhaseCompletion(
        PhaseStatus? current,
        TimelinePhase phase,
        int index,
        bool reduceMotion)
    {
        if (current is null)
        {
            return 0d;
        }

        if (index < current.Index)
        {
            return 1d;
        }

        if (index > current.Index)
        {
            return 0d;
        }

        var durationSeconds = Math.Max(1d, phase.Duration.TotalSeconds);
        var remainingSeconds = reduceMotion
            ? current.RemainingSeconds
            : Math.Max(0d, (current.EndUtc - DateTimeOffset.UtcNow).TotalSeconds);
        return Math.Clamp(1d - remainingSeconds / durationSeconds, 0d, 1d);
    }

    private void UpdateAnimationTimer()
    {
        if (_isAttached && CurrentPhase is not null && !IsReduceMotion)
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }
    }

    private static IReadOnlyList<TimelinePhase> BuildPhases(FocusCycleConfiguration? configuration)
    {
        if (configuration is null || configuration.TerminationMode != CycleTerminationMode.CycleCount)
        {
            return [];
        }

        var phases = new List<TimelinePhase>();
        var cycleCount = configuration.CycleCount ?? 0;
        for (var cycle = 1; cycle <= cycleCount; cycle++)
        {
            phases.Add(new TimelinePhase(configuration.FocusDuration, true));
            if (cycle == cycleCount)
            {
                continue;
            }

            var isLongBreak = cycle % configuration.CyclesBeforeLongBreak == 0;
            phases.Add(new TimelinePhase(
                isLongBreak ? configuration.LongBreakDuration : configuration.ShortBreakDuration,
                false));
        }

        return phases;
    }

    private sealed record TimelinePhase(TimeSpan Duration, bool IsFocus);
}
