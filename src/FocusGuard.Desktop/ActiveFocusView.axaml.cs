using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FocusGuard.Desktop;

public sealed partial class ActiveFocusView : UserControl
{
    public ActiveFocusView()
    {
        InitializeComponent();
        DataContext = new ActiveFocusViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
