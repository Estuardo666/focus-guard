using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FocusGuard.Desktop;

public sealed partial class StrictConfirmationView : UserControl
{
    public StrictConfirmationView()
    {
        InitializeComponent();
        DataContext = new StrictConfirmationViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
