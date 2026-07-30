using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FocusGuard.Desktop;

public sealed partial class ApplicationRulesView : UserControl
{
    public ApplicationRulesView()
    {
        InitializeComponent();
        DataContext = new ApplicationRuleEditorViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
