using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FocusGuard.Storage;

namespace FocusGuard.Desktop;

/// <summary>Theme-aware line icons for the three built-in cycle rhythms.</summary>
public sealed class CyclePresetIcon : Control
{
    public static readonly StyledProperty<Guid> PresetIdProperty =
        AvaloniaProperty.Register<CyclePresetIcon, Guid>(nameof(PresetId));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CyclePresetIcon, IBrush?>(nameof(Stroke));

    static CyclePresetIcon() => AffectsRender<CyclePresetIcon>(PresetIdProperty, StrokeProperty);

    public Guid PresetId
    {
        get => GetValue(PresetIdProperty);
        set => SetValue(PresetIdProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        var pen = new Pen(Stroke, 1.8d, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        if (PresetId == BuiltInCyclePresets.ExtendedFlowId)
        {
            DrawFlow(context, pen, center);
        }
        else if (PresetId == BuiltInCyclePresets.ClassicId)
        {
            DrawTomato(context, pen, center);
        }
        else
        {
            DrawDeepFocus(context, pen, center);
        }
    }

    private static void DrawDeepFocus(DrawingContext context, Pen pen, Point center)
    {
        context.DrawEllipse(null, pen, center, 9d, 9d);
        context.DrawEllipse(null, pen, center, 3d, 3d);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI / 4d;
            var start = new Point(center.X + Math.Cos(angle) * 11d, center.Y + Math.Sin(angle) * 11d);
            var end = new Point(center.X + Math.Cos(angle) * 14d, center.Y + Math.Sin(angle) * 14d);
            context.DrawLine(pen, start, end);
        }
    }

    private static void DrawFlow(DrawingContext context, Pen pen, Point center)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(center.X - 14d, center.Y + 2d), false);
        path.CubicBezierTo(
            new Point(center.X - 8d, center.Y - 9d),
            new Point(center.X - 3d, center.Y - 9d),
            new Point(center.X + 1d, center.Y),
            true);
        path.CubicBezierTo(
            new Point(center.X + 5d, center.Y + 9d),
            new Point(center.X + 10d, center.Y + 9d),
            new Point(center.X + 14d, center.Y - 2d),
            true);
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawTomato(DrawingContext context, Pen pen, Point center)
    {
        var bodyCenter = new Point(center.X, center.Y + 2d);
        context.DrawEllipse(null, pen, bodyCenter, 10d, 9d);
        context.DrawLine(pen, new Point(center.X, center.Y - 8d), new Point(center.X, center.Y - 13d));
        context.DrawLine(pen, new Point(center.X, center.Y - 8d), new Point(center.X - 6d, center.Y - 11d));
        context.DrawLine(pen, new Point(center.X, center.Y - 8d), new Point(center.X + 6d, center.Y - 11d));
        context.DrawLine(pen, new Point(center.X, center.Y - 8d), new Point(center.X - 4d, center.Y - 5d));
        context.DrawLine(pen, new Point(center.X, center.Y - 8d), new Point(center.X + 4d, center.Y - 5d));
    }
}
