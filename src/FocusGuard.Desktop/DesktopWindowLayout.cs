using Avalonia;

namespace FocusGuard.Desktop;

public static class DesktopWindowLayout
{
    public static Size ClientSize(PixelRect workingArea, double scaling)
    {
        if (!double.IsFinite(scaling) || scaling <= 0) throw new ArgumentOutOfRangeException(nameof(scaling));
        // WorkingArea already excludes the taskbar. Reserve native caption and breathing room.
        return new(Math.Max(320, Math.Floor(Math.Min(1440, workingArea.Width / scaling - 48))),
            Math.Max(240, Math.Floor(Math.Min(840, workingArea.Height / scaling - 48))));
    }
}
