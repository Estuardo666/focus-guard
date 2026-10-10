using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

/// <summary>
/// A non-activating corner notification that stays above the taskbar clock and dismisses
/// itself. Session summaries can open their full view when clicked; phase notices simply
/// dismiss. It does not depend on Windows toast registration.
/// </summary>
public sealed partial class SessionToastWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(9);
    private readonly DispatcherTimer _dismiss;

    public SessionToastWindow()
        : this(0)
    {
    }

    public SessionToastWindow(int focusedMinutes)
        : this(
            UiStrings.SummaryToastTitle,
            string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.SummaryToastBody,
                focusedMinutes),
            "\uE73E")
    {
    }

    internal SessionToastWindow(string title, string body, string iconGlyph)
    {
        InitializeComponent();
        ToastTitle.Text = title;
        ToastBody.Text = body;
        ToastIcon.Text = iconGlyph;
        _dismiss = new DispatcherTimer { Interval = Lifetime };
        _dismiss.Tick += (_, _) => Close();
    }

    /// <summary>Raised when the user clicked the card and wants the full summary.</summary>
    public event EventHandler? SummaryRequested;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionInWorkingAreaCorner();
        Card.Opacity = 1;
        Card.RenderTransform = TransformOperations.Parse("translateY(0px)");
        _dismiss.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismiss.Stop();
        base.OnClosed(e);
    }

    private void CardPressed(object? sender, PointerPressedEventArgs e)
    {
        SummaryRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void PositionInWorkingAreaCorner()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        // Working area rather than bounds, so the card never lands under the taskbar.
        var area = screen.WorkingArea;
        var width = (int)(Bounds.Width * screen.Scaling);
        var height = (int)(Bounds.Height * screen.Scaling);
        Position = new PixelPoint(
            area.X + area.Width - width - (int)(12 * screen.Scaling),
            area.Y + area.Height - height - (int)(12 * screen.Scaling));
    }
}
