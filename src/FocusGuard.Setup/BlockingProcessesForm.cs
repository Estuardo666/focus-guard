namespace FocusGuard.Setup;

/// <summary>
/// Shown when Restart Manager finds a program holding a FocusGuard file open. This is the
/// screen that replaces a cryptic "clrjit.dll is being used by another process" error with
/// something a non-technical person can act on without asking anyone: a named list, and a
/// button that closes it for them.
/// </summary>
public sealed class BlockingProcessesForm : Form
{
    private readonly Button _closeAndContinueButton;

    public BlockingProcessesForm(IReadOnlyList<BlockingProcess> processes)
    {
        Text = "Programas en uso";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 360);
        BackColor = Color.FromArgb(18, 24, 39);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var message = new Label
        {
            AutoSize = false,
            Text = "Estos programas están usando archivos de FocusGuard, así que la instalación no puede continuar todavía:",
            ForeColor = Color.FromArgb(220, 228, 245),
            Location = new Point(24, 20),
            Size = new Size(410, 42)
        };

        var list = new ListBox
        {
            Location = new Point(24, 68),
            Size = new Size(410, 150),
            BackColor = Color.FromArgb(28, 35, 52),
            ForeColor = Color.FromArgb(230, 235, 248),
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (var process in processes)
        {
            list.Items.Add($"{process.ApplicationName}  ·  PID {process.ProcessId}");
        }

        var hint = new Label
        {
            AutoSize = false,
            Text = "Si alguno tiene trabajo sin guardar, ciérralo tú mismo primero. En caso contrario, FocusGuard puede cerrarlos por ti y seguir con la instalación.",
            ForeColor = Color.FromArgb(190, 200, 220),
            Location = new Point(24, 228),
            Size = new Size(410, 48)
        };

        _closeAndContinueButton = new Button
        {
            Text = "Cerrar estos programas y continuar",
            Location = new Point(24, 285),
            Size = new Size(260, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(66, 133, 244),
            ForeColor = Color.White,
            DialogResult = DialogResult.Yes
        };
        _closeAndContinueButton.FlatAppearance.BorderSize = 0;

        var cancelButton = new Button
        {
            Text = "Cancelar",
            Location = new Point(300, 285),
            Size = new Size(134, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 48, 68),
            ForeColor = Color.White,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderSize = 0;

        AcceptButton = _closeAndContinueButton;
        CancelButton = cancelButton;
        Controls.AddRange([message, list, hint, _closeAndContinueButton, cancelButton]);
    }
}
