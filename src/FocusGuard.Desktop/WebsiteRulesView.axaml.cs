using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FocusGuard.Desktop;

public sealed partial class WebsiteRulesView : UserControl
{
    public WebsiteRulesView()
    {
        InitializeComponent();
        DataContext = new WebsiteRuleEditorViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
