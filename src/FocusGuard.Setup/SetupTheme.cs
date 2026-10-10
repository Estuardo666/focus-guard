using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FocusGuard.Setup;

internal static class SetupTheme
{
    public static readonly Color Surface0 = Color.FromArgb(14, 19, 24);
    public static readonly Color Surface1 = Color.FromArgb(17, 23, 30);
    public static readonly Color Footer = Color.FromArgb(13, 19, 25);
    public static readonly Color BackgroundStart = Color.FromArgb(17, 26, 35);
    public static readonly Color BackgroundEnd = Color.FromArgb(12, 17, 23);
    public static readonly Color TextPrimary = Color.FromArgb(237, 242, 245);
    public static readonly Color TextSecondary = Color.FromArgb(174, 185, 196);
    public static readonly Color TextTertiary = Color.FromArgb(127, 140, 153);
    public static readonly Color Border = Color.FromArgb(52, 65, 80);
    public static readonly Color Divider = Color.FromArgb(38, 255, 255, 255);
    public static readonly Color Accent = Color.FromArgb(102, 125, 255);
    public static readonly Color AccentEnd = Color.FromArgb(144, 115, 232);
    public static readonly Color AccentHover = Color.FromArgb(120, 140, 255);
    public static readonly Color AccentPressed = Color.FromArgb(86, 109, 235);
    public static readonly Color AccentPressedEnd = Color.FromArgb(125, 97, 211);
    public static readonly Color AccentDisabled = Color.FromArgb(52, 61, 76);
    public static readonly Color AccentDisabledEnd = Color.FromArgb(62, 66, 80);
    public static readonly Color Success = Color.FromArgb(105, 224, 174);
    public static readonly Color SuccessGlow = Color.FromArgb(76, 105, 224, 174);
    public static readonly Color Warning = Color.FromArgb(236, 190, 105);
    public static readonly Color ElevatedSurface = Color.FromArgb(22, 29, 38);
    public static readonly Color ModalScrim = Color.FromArgb(178, 7, 11, 15);
    public static readonly Color SecondarySurface = Color.FromArgb(32, 41, 52);
    public static readonly Color SecondaryHover = Color.FromArgb(37, 48, 60);
    public static readonly Color SecondaryPressed = Color.FromArgb(25, 33, 41);
    public static readonly Color BorderStrong = Color.FromArgb(52, 65, 80);
    public static readonly Color FocusRing = Color.FromArgb(113, 136, 255);
    public static readonly Color BottomGlow = Color.FromArgb(38, 102, 125, 255);
    public const int MotionFast = 140;
    public const int MotionStandard = 200;

    private static readonly PrivateFontCollection Fonts = LoadFonts();

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        Fonts.Families.Length > 0
            ? new Font(Fonts.Families[0], size, style, GraphicsUnit.Point)
            : new Font("Segoe UI", size, style, GraphicsUnit.Point);

    private static PrivateFontCollection LoadFonts()
    {
        var fonts = new PrivateFontCollection();
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            var bytes = new byte[stream.Length];
            _ = stream.Read(bytes, 0, bytes.Length);
            var memory = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            fonts.AddMemoryFont(memory, bytes.Length);
            Marshal.FreeCoTaskMem(memory);
        }

        return fonts;
    }
}

internal static class SetupTransitions
{
    public static async Task CrossFadeAsync(
        Control host,
        Action mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mutation);
        if (!host.IsHandleCreated || host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0)
        {
            mutation();
            return;
        }

        using var before = Capture(host);
        mutation();
        host.PerformLayout();
        host.Refresh();
        using var after = Capture(host);
        if (!SetupMotion.IsEnabled)
        {
            return;
        }

        using var layer = new CrossFadeLayer(before, after)
        {
            Dock = DockStyle.Fill
        };
        host.Controls.Add(layer);
        layer.BringToFront();
        await SetupMotion.RunAsync(
            SetupTheme.MotionStandard,
            progress => layer.Progress = progress,
            cancellationToken);
        host.Controls.Remove(layer);
        host.Refresh();
    }

    private static Bitmap Capture(Control control)
    {
        var bitmap = new Bitmap(
            Math.Max(1, control.ClientSize.Width),
            Math.Max(1, control.ClientSize.Height),
            PixelFormat.Format32bppPArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }
}

internal sealed class CrossFadeLayer : Control
{
    private readonly Image _before;
    private readonly Image _after;
    private float _progress;

    public CrossFadeLayer(Image before, Image after)
    {
        _before = before;
        _after = after;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0F, 1F);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        Draw(eventArgs.Graphics, _before, 1F - Progress);
        Draw(eventArgs.Graphics, _after, Progress);
    }

    private void Draw(Graphics graphics, Image image, float opacity)
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
        graphics.DrawImage(
            image,
            ClientRectangle,
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
    }
}

internal static class SetupMotion
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool IsEnabled =>
        SystemInformation.IsMenuAnimationEnabled
        && IsClientAreaAnimationEnabled();

    public static async Task RunAsync(
        int durationMilliseconds,
        Action<float> frame,
        CancellationToken cancellationToken = default,
        Func<float, float>? easing = null)
    {
        if (!IsEnabled || durationMilliseconds <= 0)
        {
            frame(1F);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (_, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                timer.Stop();
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            var linear = Math.Clamp(
                (float)stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds,
                0F,
                1F);
            var eased = (easing ?? EaseFocus)(linear);
            frame(eased);
            if (linear >= 1F)
            {
                timer.Stop();
                completion.TrySetResult();
            }
        };
        frame(0F);
        timer.Start();
        await completion.Task;
    }

    public static float EaseHover(float progress) =>
        CubicBezier(progress, 0.22F, 1F, 0.36F, 1F);

    public static float EaseFocus(float progress) =>
        CubicBezier(progress, 0.16F, 1F, 0.3F, 1F);

    private static bool IsClientAreaAnimationEnabled()
    {
        var enabled = true;
        return SystemParametersInfo(
            SpiGetClientAreaAnimation,
            0,
            ref enabled,
            0)
            ? enabled
            : true;
    }

    private static float CubicBezier(
        float progress,
        float x1,
        float y1,
        float x2,
        float y2)
    {
        var lower = 0F;
        var upper = 1F;
        var parameter = progress;
        for (var iteration = 0; iteration < 10; iteration++)
        {
            parameter = (lower + upper) / 2F;
            var x = BezierCoordinate(parameter, x1, x2);
            if (x < progress)
            {
                lower = parameter;
            }
            else
            {
                upper = parameter;
            }
        }

        return BezierCoordinate(parameter, y1, y2);
    }

    private static float BezierCoordinate(float parameter, float first, float second)
    {
        var inverse = 1F - parameter;
        return (3F * inverse * inverse * parameter * first)
            + (3F * inverse * parameter * parameter * second)
            + (parameter * parameter * parameter);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref bool value,
        uint flags);
}

internal sealed class SetupTitleBar : Panel
{
    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    private readonly Form _form;

    public SetupTitleBar(Form form)
    {
        _form = form;
        Height = 36;
        BackColor = Color.FromArgb(12, 18, 24);

        Controls.Add(CaptionButton("—", (_, _) => _form.WindowState = FormWindowState.Minimized));
        Controls.Add(CaptionButton("□", (_, _) => ToggleMaximize()));
        Controls.Add(CaptionButton("×", (_, _) => _form.Close(), true));
        MouseDown += DragWindow;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        for (var index = 0; index < Controls.Count; index++)
        {
            Controls[index].Location = new Point(Width - ((Controls.Count - index) * 46), 0);
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        using var font = SetupTheme.Font(8.75F);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            "FocusGuard Setup",
            font,
            new Rectangle(15, 0, Math.Max(0, Width - 153), Height),
            SetupTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private Button CaptionButton(string text, EventHandler click, bool close = false)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(46, 36),
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = BackColor,
            ForeColor = SetupTheme.TextSecondary,
            TabStop = false,
            Font = new Font("Segoe UI", 10F)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = close
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(28, 36, 45);
        button.Click += click;
        return button;
    }

    private void ToggleMaximize() =>
        _form.WindowState = _form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

    private void DragWindow(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(_form.Handle, WmNclButtonDown, HtCaption, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint handle, int message, int wParam, int lParam);
}

internal sealed class SetupFooter : Panel
{
    public SetupFooter()
    {
        BackColor = SetupTheme.Footer;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        using var pen = new Pen(SetupTheme.Divider);
        eventArgs.Graphics.DrawLine(pen, 0, 0, Width, 0);
        using var font = SetupTheme.Font(9F);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            "ⓘ  ¿Necesitas ayuda con la instalación?    Consulta la documentación incluida",
            font,
            ClientRectangle,
            SetupTheme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal abstract class RoundedButton : Button
{
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly Stopwatch _stateClock = new();
    private float _hover;
    private float _press;
    private float _hoverStart;
    private float _pressStart;
    private float _hoverTarget;
    private float _pressTarget;

    protected RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = SetupTheme.TextPrimary;
        Font = SetupTheme.Font(10F);
        Cursor = Cursors.Hand;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        _stateTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _stateTimer.Tick += AnimateState;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        using var path = RoundedRectangle(ClientRectangle, 9);
        Region = new Region(path);
    }

    protected sealed override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = 1F + (0.015F * _hover * (1F - _press)) - (0.015F * _press);
        var width = Width * scale;
        var height = Height * scale;
        var bounds = Rectangle.Round(new RectangleF(
            (Width - width) / 2F,
            (Height - height) / 2F,
            width,
            height));
        bounds.Width = Math.Max(1, bounds.Width - 1);
        bounds.Height = Math.Max(1, bounds.Height - 1);

        if (_hover > 0F && Enabled)
        {
            var shadowBounds = bounds;
            shadowBounds.Offset(0, 4);
            using var shadowPath = RoundedRectangle(shadowBounds, 9);
            using var shadow = new SolidBrush(Color.FromArgb((int)(46 * _hover), 0, 0, 0));
            eventArgs.Graphics.FillPath(shadow, shadowPath);
        }

        using var path = RoundedRectangle(bounds, 9);
        DrawSurface(eventArgs.Graphics, path, bounds, _hover, _press);
        var opacity = Enabled ? 1F - (0.06F * _press) : 0.48F;
        var textColor = Color.FromArgb((int)(255 * opacity), ForeColor);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            bounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(SetupTheme.FocusRing, 1.5F);
            eventArgs.Graphics.DrawPath(focus, path);
        }
    }

    protected abstract void DrawSurface(
        Graphics graphics,
        GraphicsPath path,
        Rectangle bounds,
        float hover,
        float press);

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        TransitionTo(hover: 1F, press: _pressTarget);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        TransitionTo(hover: 0F, press: 0F);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button == MouseButtons.Left)
        {
            TransitionTo(hover: 1F, press: 1F);
        }
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        TransitionTo(ClientRectangle.Contains(eventArgs.Location) ? 1F : 0F, press: 0F);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        base.OnGotFocus(eventArgs);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        base.OnLostFocus(eventArgs);
        TransitionTo(_hoverTarget, press: 0F);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
        {
            TransitionTo(_hoverTarget, press: 1F);
        }
    }

    protected override void OnKeyUp(KeyEventArgs eventArgs)
    {
        base.OnKeyUp(eventArgs);
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
        {
            TransitionTo(_hoverTarget, press: 0F);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected static Color Blend(Color first, Color second, float progress) =>
        Color.FromArgb(
            (int)MathF.Round(first.A + ((second.A - first.A) * progress)),
            (int)MathF.Round(first.R + ((second.R - first.R) * progress)),
            (int)MathF.Round(first.G + ((second.G - first.G) * progress)),
            (int)MathF.Round(first.B + ((second.B - first.B) * progress)));

    private void TransitionTo(float hover, float press)
    {
        _hoverStart = _hover;
        _pressStart = _press;
        _hoverTarget = hover;
        _pressTarget = press;
        if (!SetupMotion.IsEnabled)
        {
            _hover = hover;
            _press = press;
            Invalidate();
            return;
        }

        _stateClock.Restart();
        _stateTimer.Start();
    }

    private void AnimateState(object? sender, EventArgs eventArgs)
    {
        var linear = Math.Clamp(
            (float)_stateClock.Elapsed.TotalMilliseconds / 160F,
            0F,
            1F);
        var eased = SetupMotion.EaseHover(linear);
        _hover = _hoverStart + ((_hoverTarget - _hoverStart) * eased);
        _press = _pressStart + ((_pressTarget - _pressStart) * eased);
        Invalidate();
        if (linear >= 1F)
        {
            _stateTimer.Stop();
        }
    }

    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModalCardPanel : Panel
{
    public ModalCardPanel()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Padding = new Padding(28);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var shadowBounds = new Rectangle(9, 13, Width - 18, Height - 18);
        using var shadowPath = RoundedButton.RoundedRectangle(shadowBounds, 20);
        using var shadow = new SolidBrush(Color.FromArgb(86, 0, 0, 0));
        eventArgs.Graphics.FillPath(shadow, shadowPath);

        var cardBounds = new Rectangle(3, 3, Width - 12, Height - 15);
        using var cardPath = RoundedButton.RoundedRectangle(cardBounds, 20);
        using var surface = new SolidBrush(SetupTheme.ElevatedSurface);
        using var border = new Pen(Color.FromArgb(68, 104, 123, 145));
        eventArgs.Graphics.FillPath(surface, cardPath);
        eventArgs.Graphics.DrawPath(border, cardPath);
    }
}

internal sealed class SuccessCheckControl : Control
{
    private float _progress;

    public SuccessCheckControl()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Progress
    {
        get => _progress;
        set
        {
            _progress = Math.Clamp(value, 0F, 1F);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var center = new PointF(Width / 2F, Height / 2F);
        var glowRadius = 35F + (6F * Progress);
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(
            center.X - glowRadius,
            center.Y - glowRadius,
            glowRadius * 2,
            glowRadius * 2);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb((int)(68 * Progress), SetupTheme.Success),
            SurroundColors = [Color.Transparent]
        };
        eventArgs.Graphics.FillPath(glow, glowPath);

        var ringBounds = new RectangleF(center.X - 27, center.Y - 27, 54, 54);
        using var ring = new Pen(Color.FromArgb((int)(230 * Progress), SetupTheme.Success), 2.5F);
        eventArgs.Graphics.DrawEllipse(ring, ringBounds);

        var points = new[]
        {
            new PointF(center.X - 12, center.Y),
            new PointF(center.X - 3, center.Y + 9),
            new PointF(center.X + 15, center.Y - 11)
        };
        var firstLength = Distance(points[0], points[1]);
        var secondLength = Distance(points[1], points[2]);
        var visibleLength = (firstLength + secondLength) * Progress;
        using var check = new Pen(SetupTheme.Success, 4F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (visibleLength <= firstLength)
        {
            eventArgs.Graphics.DrawLine(
                check,
                points[0],
                Interpolate(points[0], points[1], visibleLength / firstLength));
        }
        else
        {
            eventArgs.Graphics.DrawLine(check, points[0], points[1]);
            eventArgs.Graphics.DrawLine(
                check,
                points[1],
                Interpolate(points[1], points[2], (visibleLength - firstLength) / secondLength));
        }
    }

    private static float Distance(PointF first, PointF second) =>
        MathF.Sqrt(MathF.Pow(second.X - first.X, 2) + MathF.Pow(second.Y - first.Y, 2));

    private static PointF Interpolate(PointF first, PointF second, float progress) =>
        new(
            first.X + ((second.X - first.X) * Math.Clamp(progress, 0F, 1F)),
            first.Y + ((second.Y - first.Y) * Math.Clamp(progress, 0F, 1F)));
}

internal sealed class GradientButton : RoundedButton
{
    protected override void DrawSurface(
        Graphics graphics,
        GraphicsPath path,
        Rectangle bounds,
        float hover,
        float press)
    {
        var start = Enabled
            ? Blend(Blend(SetupTheme.Accent, Color.FromArgb(116, 137, 255), hover), SetupTheme.AccentPressed, press)
            : SetupTheme.AccentDisabled;
        var middle = Enabled
            ? Blend(Blend(Color.FromArgb(118, 111, 242), Color.FromArgb(134, 122, 247), hover), SetupTheme.AccentPressedEnd, press)
            : Blend(SetupTheme.AccentDisabled, SetupTheme.AccentDisabledEnd, 0.5F);
        var end = Enabled
            ? Blend(Blend(SetupTheme.AccentEnd, Color.FromArgb(154, 124, 239), hover), SetupTheme.AccentPressedEnd, press)
            : SetupTheme.AccentDisabledEnd;
        using var brush = new LinearGradientBrush(bounds, start, end, 30F)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [start, middle, end],
                Positions = [0F, 0.52F, 1F]
            }
        };
        graphics.FillPath(brush, path);
        using var border = new Pen(Blend(SetupTheme.Accent, SetupTheme.AccentHover, hover), 1F);
        graphics.DrawPath(border, path);
    }
}

internal sealed class OutlineButton : RoundedButton
{
    protected override void DrawSurface(
        Graphics graphics,
        GraphicsPath path,
        Rectangle bounds,
        float hover,
        float press)
    {
        var backgroundColor = Enabled
            ? Blend(Blend(SetupTheme.SecondarySurface, SetupTheme.SecondaryHover, hover), SetupTheme.SecondaryPressed, press)
            : SetupTheme.SecondarySurface;
        var borderColor = Enabled
            ? Blend(SetupTheme.Border, SetupTheme.BorderStrong, hover)
            : SetupTheme.Border;
        using var background = new SolidBrush(backgroundColor);
        using var border = new Pen(borderColor, 1F);
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);
    }
}

internal sealed class ShieldLockControl : Control
{
    public ShieldLockControl()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.TranslateTransform(11, 4);
        using var path = new GraphicsPath();
        path.AddBezier(new PointF(25, 1), new PointF(35, 8), new PointF(43, 10), new PointF(51, 13));
        path.AddLine(new PointF(51, 13), new PointF(51, 28));
        path.AddBezier(new PointF(51, 28), new PointF(51, 43), new PointF(42, 55), new PointF(25, 65));
        path.AddBezier(new PointF(25, 65), new PointF(8, 55), new PointF(-1, 43), new PointF(-1, 28));
        path.AddLine(new PointF(-1, 28), new PointF(-1, 13));
        path.AddBezier(new PointF(-1, 13), new PointF(8, 10), new PointF(16, 8), new PointF(25, 1));
        using var pen = new Pen(SetupTheme.Accent, 3.2F)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        eventArgs.Graphics.DrawPath(pen, path);

        using var fill = new SolidBrush(SetupTheme.Accent);
        using var lockBody = RoundedRectangle(new Rectangle(18, 30, 15, 15), 3);
        eventArgs.Graphics.FillPath(fill, lockBody);
        eventArgs.Graphics.DrawArc(pen, new Rectangle(20, 22, 11, 13), 180, 180);
        using var key = new Pen(SetupTheme.Surface0, 1.5F);
        eventArgs.Graphics.DrawLine(key, 25.5F, 36, 25.5F, 40);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class IconTextControl : Control
{
    private readonly string _primaryText;
    private readonly string _secondaryText;

    public IconTextControl(string primaryText, string secondaryText)
    {
        _primaryText = primaryText;
        _secondaryText = secondaryText;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(SetupTheme.TextTertiary, 1.25F);
        eventArgs.Graphics.DrawEllipse(pen, 66, 12, 13, 13);
        eventArgs.Graphics.DrawLine(pen, 70, 16, 72.5F, 19);
        eventArgs.Graphics.DrawLine(pen, 72.5F, 19, 76, 14);
        using var primary = SetupTheme.Font(8.75F);
        using var secondary = SetupTheme.Font(8.75F);
        TextRenderer.DrawText(eventArgs.Graphics, _primaryText, primary, new Point(86, 11), SetupTheme.TextTertiary);
        TextRenderer.DrawText(eventArgs.Graphics, _secondaryText, secondary, new Point(47, 36), SetupTheme.TextTertiary);
    }
}
