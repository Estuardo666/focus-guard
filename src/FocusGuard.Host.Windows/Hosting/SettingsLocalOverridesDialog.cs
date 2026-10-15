using System.Globalization;
using FocusGuard.Sync;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>Reviews the current per-device values before choosing which fields should stay local.</summary>
internal sealed class SettingsLocalOverridesDialog : Form
{
    private readonly SettingsOutbox reviewedState;
    private readonly CheckBox keepTheme;
    private readonly CheckBox keepLanguage;
    private readonly CheckBox keepDuration;

    private SettingsLocalOverridesDialog(SettingsOutbox reviewedState)
    {
        this.reviewedState = reviewedState ?? throw new ArgumentNullException(nameof(reviewedState));
        reviewedState.Validate();

        Text = HostWindowStrings.Get("SettingsLocalOverridesTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 430);
        BackColor = Color.FromArgb(26, 23, 36);
        ForeColor = Color.FromArgb(238, 234, 246);

        var shared = reviewedState.Draft ?? reviewedState.Confirmed.EffectiveValues;
        var local = reviewedState.LocalOverrides.Apply(shared);
        var currentOverrides = reviewedState.LocalOverrides;

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
            Text = HostWindowStrings.Get("SettingsLocalOverridesTitle"),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(595, 0),
            Text = HostWindowStrings.Get("SettingsLocalOverridesDetail"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);

        var choices = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++)
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3f));

        keepTheme = AddChoice(choices, 0, "SettingsLocalOverridesKeepTheme",
            Format("SettingsLocalOverridesThemeValues", ThemeName(local.Theme), ThemeName(shared.Theme)),
            currentOverrides.Theme is not null);
        keepLanguage = AddChoice(choices, 1, "SettingsLocalOverridesKeepLanguage",
            Format("SettingsLocalOverridesLanguageValues", LanguageName(local.UiLanguage), LanguageName(shared.UiLanguage)),
            currentOverrides.UiLanguage is not null);
        keepDuration = AddChoice(choices, 2, "SettingsLocalOverridesKeepDuration",
            Format("SettingsLocalOverridesDurationValues", local.DefaultDurationMinutes, shared.DefaultDurationMinutes),
            currentOverrides.DefaultDurationMinutes is not null);
        layout.Controls.Add(choices, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BackColor
        };
        var save = new Button
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("SettingsLocalOverridesSave"),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(167, 143, 224),
            ForeColor = Color.FromArgb(31, 24, 48),
            Padding = new Padding(12, 5, 12, 5),
            DialogResult = DialogResult.OK
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
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
    }

    public LocalSettingsOverrides Selection => CreateSelection(reviewedState,
        keepTheme.Checked, keepLanguage.Checked, keepDuration.Checked);

    public static LocalSettingsOverrides? ShowForReview(IWin32Window owner, SettingsOutbox reviewedState)
    {
        using var dialog = new SettingsLocalOverridesDialog(reviewedState);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selection : null;
    }

    /// <summary>Convert the reviewed checkboxes to the exact values they displayed, or null for global-follow.</summary>
    internal static LocalSettingsOverrides CreateSelection(SettingsOutbox reviewedState,
        bool keepTheme, bool keepLanguage, bool keepDuration)
    {
        ArgumentNullException.ThrowIfNull(reviewedState);
        reviewedState.Validate();
        var shared = reviewedState.Draft ?? reviewedState.Confirmed.EffectiveValues;
        var local = reviewedState.LocalOverrides.Apply(shared);
        return new LocalSettingsOverrides(
            keepTheme ? local.Theme : null,
            keepLanguage ? local.UiLanguage : null,
            keepDuration ? local.DefaultDurationMinutes : null);
    }

    private CheckBox AddChoice(TableLayoutPanel panel, int row, string checkKey, string valuesText, bool isChecked)
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(34, 30, 46),
            Padding = new Padding(12, 7, 10, 5),
            Margin = new Padding(0, 0, 0, 7)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var caption = HostWindowStrings.Get(checkKey);
        var check = new CheckBox
        {
            AutoSize = true,
            Text = caption,
            AccessibleName = caption,
            Checked = isChecked,
            ForeColor = ForeColor,
            BackColor = section.BackColor,
            Margin = new Padding(0, 0, 0, 3)
        };
        var values = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = valuesText,
            AccessibleDescription = valuesText,
            ForeColor = Color.FromArgb(180, 174, 194),
            BackColor = section.BackColor,
            Margin = new Padding(24, 0, 0, 0)
        };
        section.Controls.Add(check, 0, 0);
        section.Controls.Add(values, 0, 1);
        panel.Controls.Add(section, 0, row);
        return check;
    }

    private static string ThemeName(string theme) => HostWindowStrings.Get(theme switch
    {
        "dark" => "SettingsLocalOverridesDark",
        "light" => "SettingsLocalOverridesLight",
        _ => throw new ArgumentException("Invalid reviewed theme.", nameof(theme))
    });

    private static string LanguageName(string language) => HostWindowStrings.Get(language switch
    {
        "en" => "SettingsLocalOverridesEnglish",
        "es" => "SettingsLocalOverridesSpanish",
        _ => throw new ArgumentException("Invalid reviewed language.", nameof(language))
    });

    private static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentUICulture, HostWindowStrings.Get(key), values);
}
