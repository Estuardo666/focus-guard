namespace FocusGuard.Host.Windows.Hosting;

/// <summary>Displays the one-time recovery kit in a native window only; it is never sent across the WebView bridge.</summary>
internal sealed class RecoveryKitDialog : Form
{
    private readonly TextBox kitText;
    private readonly CheckBox savedConfirmation;
    private readonly Button continueButton;

    public RecoveryKitDialog(string recoveryKit)
    {
        Text = HostWindowStrings.Get("DeviceRecoveryTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 360);
        BackColor = Color.FromArgb(26, 23, 36);
        ForeColor = Color.FromArgb(238, 234, 246);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = false,
            BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DeviceRecoveryHeading"),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 8)
        };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(580, 0),
            Text = HostWindowStrings.Get("DeviceRecoveryDetail"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);

        kitText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ShortcutsEnabled = true,
            AccessibleName = HostWindowStrings.Get("DeviceRecoveryCodeLabel"),
            Text = recoveryKit,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            BackColor = Color.FromArgb(17, 15, 25),
            ForeColor = Color.FromArgb(244, 240, 250),
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(kitText, 0, 2);

        savedConfirmation = new CheckBox
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DeviceRecoverySaved"),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 14)
        };
        layout.Controls.Add(savedConfirmation, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        continueButton = new Button
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DeviceRecoveryContinue"),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(167, 143, 224),
            ForeColor = Color.FromArgb(31, 24, 48),
            Padding = new Padding(12, 5, 12, 5),
            DialogResult = DialogResult.OK,
            Enabled = false
        };
        var cancelButton = new Button
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
        savedConfirmation.CheckedChanged += (_, _) => continueButton.Enabled = savedConfirmation.Checked;
        buttons.Controls.Add(continueButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        AcceptButton = continueButton;
        CancelButton = cancelButton;
        FormClosed += (_, _) => kitText.Clear();
    }
}
