using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FocusGuard.Desktop;

public sealed partial class CompactFocusWindow : Window
{
    public CompactFocusWindow() { InitializeComponent(); }
    public event EventHandler? OpenRequested;
    private void DragWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void ToggleCollapsed(object? sender, RoutedEventArgs e) => Details.IsVisible = !Details.IsVisible;
    private void CloseCompact(object? sender, RoutedEventArgs e) => Close();
    private void OpenMainWindow(object? sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
}
