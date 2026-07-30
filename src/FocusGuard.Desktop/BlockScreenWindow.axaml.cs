using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace FocusGuard.Desktop;

/// <summary>
/// Full-screen block screen for a blocked application, one window per monitor.
///
/// Alt+F4 and Esc are swallowed, but the Windows key is deliberately left alone:
/// hijacking the system keyboard is what makes people uninstall this kind of app.
/// </summary>
public sealed partial class BlockScreenWindow : Window
{
    private readonly DispatcherTimer _tick;
    private readonly nint _previousForegroundWindow;
    private BlockScreenViewModel? _viewModel;

    public BlockScreenWindow()
        : this(new BlockScreenViewModel(
            "Application.exe",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(50),
            isStrict: false))
    {
    }

    public BlockScreenWindow(BlockScreenViewModel viewModel, nint previousForegroundWindow = 0)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _previousForegroundWindow = previousForegroundWindow;
        DataContext = viewModel;
        viewModel.Dismissed += ViewModelDismissed;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => viewModel.Tick();
    }

    /// <summary>
    /// Opens one screen per monitor. The primary screen owns the interactive block; the
    /// others just darken, so the copy is not repeated across a multi-monitor desk.
    /// </summary>
    public static IReadOnlyList<BlockScreenWindow> ShowForAllScreens(
        BlockScreenViewModel viewModel,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);

        var previous = CaptureForegroundWindow();
        var windows = new List<BlockScreenWindow>();
        var screens = owner.Screens.All;
        var primary = owner.Screens.Primary ?? (screens.Count > 0 ? screens[0] : null);
        foreach (var screen in screens)
        {
            var isPrimary = primary is null || ReferenceEquals(screen, primary);
            var window = new BlockScreenWindow(viewModel, isPrimary ? previous : 0)
            {
                Position = screen.Bounds.Position
            };
            if (!isPrimary)
            {
                window.BlockContent.IsVisible = false;
            }

            windows.Add(window);
        }

        // Closing any one screen closes them all: the block is a single decision.
        foreach (var window in windows)
        {
            var siblings = windows;
            window.Closed += (_, _) =>
            {
                foreach (var sibling in siblings)
                {
                    if (!ReferenceEquals(sibling, window) && sibling.IsVisible)
                    {
                        sibling.Close();
                    }
                }
            };
            window.Show();
        }

        return windows;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _tick.Start();
        BlockContent.Opacity = 1;
        BlockContent.RenderTransform = TransformOperations.Parse("translateY(0px)");
        BackButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        if (_viewModel is not null)
        {
            _viewModel.Dismissed -= ViewModelDismissed;
            _viewModel = null;
        }

        RestorePreviousForegroundWindow();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc must not dismiss the screen: it would turn the block into a keystroke.
        if (e.Key is Key.Escape or Key.F4)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void ViewModelDismissed(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Close);

    private void BackClick(object? sender, RoutedEventArgs e) => Close();

    private void CaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BlockScreenViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.SaveCaptureCommand.CanExecute(null))
        {
            viewModel.SaveCaptureCommand.Execute(null);
        }
    }

    private void RestorePreviousForegroundWindow()
    {
        if (_previousForegroundWindow == 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        SetForegroundWindow(_previousForegroundWindow);
    }

    private static nint CaptureForegroundWindow() =>
        OperatingSystem.IsWindows() ? GetForegroundWindow() : 0;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
