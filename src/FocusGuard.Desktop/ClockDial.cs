using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FocusGuard.Desktop;

/// <summary>
/// Draws the timer as a clock face: a stable chromatic ring, sixty indexes, and an
/// active-only elapsed-time head. Text remains outside this control so countdown
/// crossfades do not invalidate the dial geometry.
/// </summary>
public sealed class ClockDial : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ClockDial, bool>(nameof(IsActive));

    public static readonly StyledProperty<double> SweepAngleProperty =
        AvaloniaProperty.Register<ClockDial, double>(nameof(SweepAngle));

    public static readonly StyledProperty<IBrush?> FaceBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(FaceBrush));

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(ProgressBrush));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(TickBrush));

    public static readonly StyledProperty<IBrush?> SpentTickBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(SpentTickBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<ClockDial, IBrush?>(nameof(IndicatorBrush));

    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<ClockDial, double>(nameof(RingThickness), 9d);

    static ClockDial()
    {
        AffectsRender<ClockDial>(
            IsActiveProperty,
            SweepAngleProperty,
            FaceBrushProperty,
            RingBrushProperty,
            ProgressBrushProperty,
            TickBrushProperty,
            SpentTickBrushProperty,
            IndicatorBrushProperty,
            RingThicknessProperty);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    public IBrush? FaceBrush
    {
        get => GetValue(FaceBrushProperty);
        set => SetValue(FaceBrushProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    /// <summary>Colour of the indexes the elapsed arc has already passed.</summary>
    public IBrush? SpentTickBrush
    {
        get => GetValue(SpentTickBrushProperty);
        set => SetValue(SpentTickBrushProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        var outerRadius = Math.Max(0d, Math.Min(Bounds.Width, Bounds.Height) / 2d - RingThickness / 2d - 5d);
        var faceRadius = Math.Max(0d, outerRadius - RingThickness / 2d - 1d);

        var sweep = IsActive ? Math.Clamp(SweepAngle, 0d, 359.999d) : 0d;

        context.DrawEllipse(FaceBrush, null, center, faceRadius, faceRadius);
        context.DrawEllipse(null, new Pen(RingBrush, RingThickness), center, outerRadius, outerRadius);
        DrawClockIndexes(context, center, outerRadius, sweep);

        if (!IsActive)
        {
            return;
        }

        if (sweep > 0.01d)
        {
            DrawProgress(context, center, outerRadius, sweep);
        }

        DrawIndicator(context, PointOnCircle(center, outerRadius, sweep - 90d));
    }

    /// <summary>
    /// Sixty indexes around the face. While a session runs they carry the same reading
    /// as the arc, from the other direction: an index the arc has already swept past is
    /// spent and dims, and the ones still ahead stay lit. Time left is legible at a
    /// glance even before the numbers are read.
    /// </summary>
    private void DrawClockIndexes(DrawingContext context, Point center, double outerRadius, double sweep)
    {
        var indexOuterRadius = outerRadius - RingThickness - 8d;
        var spentPen = new Pen(SpentTickBrush ?? TickBrush, 1d, lineCap: PenLineCap.Round);
        var spentHourPen = new Pen(SpentTickBrush ?? TickBrush, 1.5d, lineCap: PenLineCap.Round);
        var litPen = new Pen(TickBrush, 1d, lineCap: PenLineCap.Round);
        var litHourPen = new Pen(TickBrush, 1.5d, lineCap: PenLineCap.Round);

        for (var index = 0; index < 60; index++)
        {
            var isHour = index % 5 == 0;
            var length = isHour ? 10d : 5d;
            var angle = index * 6d - 90d;
            var start = PointOnCircle(center, indexOuterRadius - length, angle);
            var end = PointOnCircle(center, indexOuterRadius, angle);
            var isSpent = index * 6d <= sweep;
            var pen = isHour
                ? (isSpent ? spentHourPen : litHourPen)
                : (isSpent ? spentPen : litPen);
            context.DrawLine(pen, start, end);
        }
    }

    private void DrawProgress(DrawingContext context, Point center, double radius, double sweep)
    {
        var start = PointOnCircle(center, radius, -90d);
        var end = PointOnCircle(center, radius, sweep - 90d);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(
                end,
                new Size(radius, radius),
                0d,
                sweep > 180d,
                SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, new Pen(ProgressBrush, RingThickness, lineCap: PenLineCap.Round), geometry);
    }

    private void DrawIndicator(DrawingContext context, Point point)
    {
        context.DrawEllipse(ProgressBrush, null, point, 10d, 10d);
        context.DrawEllipse(IndicatorBrush, null, point, 5.5d, 5.5d);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
