using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FocusGuard.Desktop;

/// <summary>
/// A full-screen dim used only behind the session summary, when the user asked for the
/// more ceremonious ending. It enforces nothing and swallows no input of its own.
/// </summary>
public sealed partial class BlockScrimWindow : Window
{
    public BlockScrimWindow() => InitializeComponent();
}
