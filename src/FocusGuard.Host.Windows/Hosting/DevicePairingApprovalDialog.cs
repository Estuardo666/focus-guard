namespace FocusGuard.Host.Windows.Hosting;

/// <summary>Native confirmation gate for public device-key approval; the WebView cannot approve by itself.</summary>
internal sealed class DevicePairingApprovalDialog : Form
{
    private readonly CheckBox compared;
    private readonly Button approve;

    public DevicePairingApprovalDialog(HostPendingPairingSummary pending)
    {
        Text = HostWindowStrings.Get("DevicePairApprovalTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(660, 390);
        BackColor = Color.FromArgb(26, 23, 36);
        ForeColor = Color.FromArgb(238, 234, 246);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 9,
            BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DevicePairApprovalTitle"),
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Text = HostWindowStrings.Get("DevicePairApprovalDetail"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 14)
        }, 0, 1);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"{pending.Platform} · {pending.ClientVersion} · {pending.DeviceId}",
            ForeColor = Color.FromArgb(220, 213, 234),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 2);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DevicePairFingerprint"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 3);
        var fingerprint = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            ShortcutsEnabled = true,
            Text = pending.Fingerprint,
            AccessibleName = HostWindowStrings.Get("DevicePairFingerprint"),
            Font = new Font("Consolas", 11, FontStyle.Regular),
            BackColor = Color.FromArgb(17, 15, 25),
            ForeColor = Color.FromArgb(244, 240, 250),
            Margin = new Padding(0, 0, 0, 16)
        };
        layout.Controls.Add(fingerprint, 0, 4);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DevicePairRequestCode"),
            ForeColor = Color.FromArgb(201, 195, 214),
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 5);
        var requestCode = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            ShortcutsEnabled = true,
            Text = pending.PairingId,
            AccessibleName = HostWindowStrings.Get("DevicePairRequestCode"),
            Font = new Font("Consolas", 10, FontStyle.Regular),
            BackColor = Color.FromArgb(17, 15, 25),
            ForeColor = Color.FromArgb(244, 240, 250),
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(requestCode, 0, 6);
        compared = new CheckBox
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DevicePairCompared"),
            ForeColor = ForeColor,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 0, 16)
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        approve = new Button
        {
            AutoSize = true,
            Text = HostWindowStrings.Get("DevicePairApprove"),
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
        compared.CheckedChanged += (_, _) => approve.Enabled = compared.Checked;
        buttons.Controls.Add(approve);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(compared, 0, 7);
        layout.Controls.Add(buttons, 0, 8);
        Controls.Add(layout);
        AcceptButton = approve;
        CancelButton = cancel;
    }
}
