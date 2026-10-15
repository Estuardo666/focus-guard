using System.Globalization;
using FocusGuard.Sync;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Host.Windows.Hosting;

internal sealed class PortableSettingsTransferDialog : Form
{
    private readonly CheckBox theme;
    private readonly CheckBox language;
    private readonly CheckBox duration;
    private readonly Button confirm;

    private PortableSettingsTransferDialog(SettingsImportPreview preview)
    {
        Text = HostWindowStrings.Get("PortableSettingsImportTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(590, 315);
        BackColor = Color.FromArgb(26, 23, 36);
        ForeColor = Color.FromArgb(238, 234, 246);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4, BackColor = BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Text = Text, Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = ForeColor, BackColor = BackColor, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(540, 0), Text = HostWindowStrings.Get("PortableSettingsImportDetail"), ForeColor = Color.FromArgb(201, 195, 214), BackColor = BackColor, Margin = new Padding(0, 0, 0, 10) }, 0, 1);
        var choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = BackColor };
        theme = Choice(Format("PortableSettingsTheme", preview.Baseline.Theme, preview.Proposed.Theme));
        language = Choice(Format("PortableSettingsLanguage", preview.Baseline.UiLanguage, preview.Proposed.UiLanguage));
        duration = Choice(Format("PortableSettingsDuration", preview.Baseline.DefaultDurationMinutes, preview.Proposed.DefaultDurationMinutes));
        choices.Controls.Add(theme); choices.Controls.Add(language); choices.Controls.Add(duration);
        layout.Controls.Add(choices, 0, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = BackColor };
        confirm = new Button { AutoSize = true, Text = HostWindowStrings.Get("PortableSettingsImport"), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(167, 143, 224), ForeColor = Color.FromArgb(31, 24, 48), Padding = new Padding(12, 5, 12, 5), DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { AutoSize = true, Text = HostWindowStrings.Get("Cancel"), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 38, 54), ForeColor = ForeColor, Padding = new Padding(12, 5, 12, 5), DialogResult = DialogResult.Cancel, CausesValidation = false };
        theme.CheckedChanged += SelectionChanged; language.CheckedChanged += SelectionChanged; duration.CheckedChanged += SelectionChanged;
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        AcceptButton = confirm; CancelButton = cancel;
    }

    public SettingsFields Selection => (theme.Checked ? SettingsFields.Theme : SettingsFields.None)
        | (language.Checked ? SettingsFields.Language : SettingsFields.None)
        | (duration.Checked ? SettingsFields.Duration : SettingsFields.None);

    public static PortableSettingsFileSelection? ChooseExport(IWin32Window owner, DesktopSettings current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = MessageBox.Show(owner, HostWindowStrings.Get("PortableSettingsExportDetail"), HostWindowStrings.Get("PortableSettingsExportTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes) return null;
        using var dialog = new SaveFileDialog { AddExtension = true, DefaultExt = "json", FileName = "focusguard-settings.json", Filter = HostWindowStrings.Get("PortableSettingsFilter"), OverwritePrompt = true };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        return new PortableSettingsFileSelection(dialog.FileName, SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration);
    }

    public static string? ChooseImportFile(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = false, Filter = HostWindowStrings.Get("PortableSettingsFilter") };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public static SettingsFields? ConfirmImport(IWin32Window owner, SettingsImportPreview preview)
    {
        using var dialog = new PortableSettingsTransferDialog(preview);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selection : null;
    }

    private CheckBox Choice(string text) => new() { AutoSize = true, Text = text, AccessibleName = text, ForeColor = ForeColor, BackColor = BackColor, Margin = new Padding(0, 5, 0, 7) };
    private void SelectionChanged(object? sender, EventArgs args) => confirm.Enabled = Selection != SettingsFields.None;
    private static string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentUICulture, HostWindowStrings.Get(key), values);
}
