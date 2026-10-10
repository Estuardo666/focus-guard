using Avalonia.Controls;

namespace FocusGuard.Desktop;

public sealed partial class RuleEditorView : UserControl
{
    public RuleEditorView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            // Keep each rule card readable after icons, remove controls and padding.
            var narrow = e.NewSize.Width < 760;
            Columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            Grid.SetColumn(Applications, narrow ? 0 : 1);
            Grid.SetRow(Applications, narrow ? 1 : 0);
        };
    }
    public void FocusSearch() => ApplicationSearchBox.Focus();
}
