using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FocusGuard.Desktop;

public sealed partial class TrayFlyoutWindow : Window
{
    private const int WorkAreaInset = 12;

    public TrayFlyoutWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
    }

    internal TrayFlyoutWindow(FocusViewModel viewModel)
        : this() =>
        DataContext = viewModel;

    public event EventHandler? OpenRequested;
    public event EventHandler? CompactRequested;
    public event EventHandler? ReactPilotRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionWithinPrimaryWorkArea();
        Activate();
    }

    private void PositionWithinPrimaryWorkArea()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var workArea = screen.WorkingArea;
        var pixelWidth = (int)Math.Ceiling(Width * screen.Scaling);
        var pixelHeight = (int)Math.Ceiling(Height * screen.Scaling);
        Position = TrayFlyoutPlacement.BottomRight(
            workArea,
            pixelWidth,
            pixelHeight,
            (int)Math.Ceiling(WorkAreaInset * screen.Scaling));
    }

    private void OpenFocusGuardClick(object? sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void CompactClick(object? sender, RoutedEventArgs e) => CompactRequested?.Invoke(this, EventArgs.Empty);

    private void ReactPilotClick(object? sender, RoutedEventArgs e) => ReactPilotRequested?.Invoke(this, EventArgs.Empty);

    private void SettingsClick(object? sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ExitFocusGuardClick(object? sender, RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void FlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }
}

internal static class TrayFlyoutPlacement
{
    internal static PixelPoint BottomRight(
        PixelRect workArea,
        int flyoutWidth,
        int flyoutHeight,
        int inset) =>
        new(
            Math.Max(workArea.X, workArea.Right - flyoutWidth - inset),
            Math.Max(workArea.Y, workArea.Bottom - flyoutHeight - inset));
}
