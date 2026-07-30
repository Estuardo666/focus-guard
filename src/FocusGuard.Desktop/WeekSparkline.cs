using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FocusGuard.Desktop;

/// <summary>
/// Seven bars of focus minutes, drawn directly instead of through an ItemsControl:
/// the header repaints every minute while a session runs, and seven rectangles in
/// <see cref="Render"/> cost far less than seven templated item containers.
/// </summary>
public sealed class WeekSparkline : Control
{
    private const int DayCount = 7;

    public static readonly StyledProperty<IReadOnlyList<double>?> DaysProperty =
        AvaloniaProperty.Register<WeekSparkline, IReadOnlyList<double>?>(nameof(Days));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<WeekSparkline, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<WeekSparkline, IBrush?>(nameof(BarBrush));

    public static readonly StyledProperty<double> BarWidthProperty =
        AvaloniaProperty.Register<WeekSparkline, double>(nameof(BarWidth), 7d);

    public static readonly StyledProperty<double> BarSpacingProperty =
        AvaloniaProperty.Register<WeekSparkline, double>(nameof(BarSpacing), 3d);

    static WeekSparkline()
    {
        AffectsRender<WeekSparkline>(DaysProperty, AccentProperty, BarBrushProperty);
        AffectsMeasure<WeekSparkline>(BarWidthProperty, BarSpacingProperty);
    }

    /// <summary>Minutes focused per day, oldest first. The last entry is "today" and takes the accent.</summary>
    public IReadOnlyList<double>? Days
    {
        get => GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public double BarWidth
    {
        get => GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double BarSpacing
    {
        get => GetValue(BarSpacingProperty);
        set => SetValue(BarSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = (BarWidth * DayCount) + (BarSpacing * (DayCount - 1));
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 26d;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        var days = Days;
        if (days is null || days.Count == 0 || Bounds.Height <= 0)
        {
            return;
        }

        // Scale against the busiest day so the shape stays readable regardless of totals.
        var peak = 0d;
        for (var index = 0; index < days.Count; index++)
        {
            peak = Math.Max(peak, days[index]);
        }

        if (peak <= 0)
        {
            return;
        }

        var accent = Accent ?? Brushes.DodgerBlue;
        var idle = BarBrush ?? Brushes.DimGray;
        var corner = new CornerRadius(2, 2, 0, 0);
        var slot = BarWidth + BarSpacing;
        var lastIndex = Math.Min(days.Count, DayCount) - 1;

        for (var index = 0; index <= lastIndex; index++)
        {
            // A day with any activity keeps a visible stub rather than vanishing.
            var ratio = Math.Clamp(days[index] / peak, 0d, 1d);
            var height = days[index] > 0
                ? Math.Max(2d, ratio * Bounds.Height)
                : 0d;
            if (height <= 0)
            {
                continue;
            }

            var rect = new Rect(index * slot, Bounds.Height - height, BarWidth, height);
            context.DrawRectangle(index == lastIndex ? accent : idle, null, new RoundedRect(rect, corner));
        }
    }
}
