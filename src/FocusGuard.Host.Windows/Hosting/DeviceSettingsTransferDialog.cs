using System.Globalization;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>Native, opt-in allowlist for one-time account settings transfer.</summary>
internal sealed class DeviceSettingsTransferDialog : Form
{
    private readonly CheckBox theme;
    private readonly CheckBox language;
    private readonly CheckBox duration;
    private readonly Button confirm;
    private readonly bool importing;

    private DeviceSettingsTransferDialog(DesktopSettings current, SharedDesktopSettings? shared)
    {
        importing = shared is not null;
        Text = HostWindowStrings.Get(importing ? "SettingsTransferImportTitle" : "SettingsTransferShareTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(610, 330);
        BackColor = Color.FromArgb(26, 23, 36);
        ForeColor = Color.FromArgb(238, 234, 246);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = HostWindowStrings.Get(importing ? "SettingsTransferImportTitle" : "SettingsTransferShareTitle"),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(550, 0),
            Text = HostWindowStrings.Get(importing ? "SettingsTransferImportDetail" : "SettingsTransferShareDetail"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 1);

        var choices = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = BackColor,
            Margin = new Padding(0, 4, 0, 10)
        };
        theme = CreateChoice(importing
            ? Format("SettingsTransferThemeImport", current.Theme, shared?.Theme)
            : Format("SettingsTransferThemeShare", current.Theme));
        language = CreateChoice(importing
            ? Format("SettingsTransferLanguageImport", current.UiLanguage, shared?.Language)
            : Format("SettingsTransferLanguageShare", current.UiLanguage));
        duration = CreateChoice(importing
            ? Format("SettingsTransferDurationImport", current.LastDurationMinutes, shared?.DurationMinutes)
            : Format("SettingsTransferDurationShare", current.LastDurationMinutes));
        if (!importing || shared?.Theme is not null) choices.Controls.Add(theme);
        if (!importing || shared?.Language is not null) choices.Controls.Add(language);
        if (!importing || shared?.DurationMinutes is not null) choices.Controls.Add(duration);
        layout.Controls.Add(choices, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BackColor
        };
        confirm = new Button
        {
            AutoSize = true,
            Text = HostWindowStrings.Get(importing ? "SettingsTransferImport" : "SettingsTransferShare"),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(167, 143, 224),
            ForeColor = Color.FromArgb(31, 24, 48),
            Padding = new Padding(12, 5, 12, 5),
            DialogResult = DialogResult.OK,
            Enabled = false
        };
        var cancel = new Button
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("Cancel"),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(42, 38, 54),
            ForeColor = ForeColor,
            Padding = new Padding(12, 5, 12, 5),
            DialogResult = DialogResult.Cancel,
            CausesValidation = false
        };
        theme.CheckedChanged += SelectionChanged;
        language.CheckedChanged += SelectionChanged;
        duration.CheckedChanged += SelectionChanged;
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = confirm;
        CancelButton = cancel;
    }

    public SettingsTransferSelection Selection => new(theme.Checked, language.Checked, duration.Checked);

    public static SettingsTransferSelection? ShowShare(IWin32Window owner, DesktopSettings current)
    {
        using var dialog = new DeviceSettingsTransferDialog(current, null);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selection : null;
    }

    public static SettingsTransferSelection? ShowImport(IWin32Window owner, SharedDesktopSettings shared,
        DesktopSettings current)
    {
        using var dialog = new DeviceSettingsTransferDialog(current, shared);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selection : null;
    }

    private CheckBox CreateChoice(string text) => new()
    {
        AutoSize = true,
        Text = text,
        AccessibleName = text,
        ForeColor = ForeColor,
        BackColor = BackColor,
        Margin = new Padding(0, 5, 0, 7)
    };

    private void SelectionChanged(object? sender, EventArgs args) =>
        confirm.Enabled = theme.Checked || language.Checked || duration.Checked;

    private static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentUICulture, HostWindowStrings.Get(key), values);
}
