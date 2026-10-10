using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FocusGuard.Setup;

/// <summary>
/// A branded owner-sized modal shown when Restart Manager finds applications that
/// hold FocusGuard files open. It keeps the process-closing consequence explicit
/// while visually matching the Desktop strict/profile review surfaces.
/// </summary>
public sealed class BlockingProcessesForm : Form
{
    private readonly ModalCardPanel _card;
    private readonly Button _closeAndContinueButton;
    private readonly Button _cancelButton;
    private Bitmap? _blurredOwner;
    private bool _isClosing;

    public BlockingProcessesForm(IReadOnlyList<BlockingProcess> processes)
    {
        Text = "Programas en uso";
        AccessibleName = "Programas que deben cerrarse para continuar";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = SetupTheme.Surface0;
        Font = SetupTheme.Font(10F);
        KeyPreview = true;
        DoubleBuffered = true;
        Opacity = SetupMotion.IsEnabled ? 0D : 1D;

        _card = new ModalCardPanel
        {
            Size = new Size(550, 430),
            Anchor = AnchorStyles.None,
            AccessibleName = "Revisión de programas abiertos"
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(10, 4, 10, 6)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

        var eyebrow = Label(
            "REVISIÓN ANTES DE CONTINUAR",
            SetupTheme.Font(8.5F, FontStyle.Bold),
            SetupTheme.Accent);
        var message = Label(
            "Cierra estos programas para continuar",
            SetupTheme.Font(19F, FontStyle.Bold),
            SetupTheme.TextPrimary);
        message.Padding = new Padding(0, 4, 0, 6);

        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 8),
            BackColor = SetupTheme.Surface1,
            ForeColor = SetupTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = SetupTheme.Font(10F),
            ItemHeight = 30,
            AccessibleName = "Programas abiertos que bloquean la instalación"
        };
        foreach (var process in processes)
        {
            list.Items.Add($"{process.ApplicationName}   ·   PID {process.ProcessId}");
        }

        var hint = Label(
            "Guarda primero cualquier trabajo pendiente. Si continúas, FocusGuard "
            + "intentará cerrar estas aplicaciones y seguirá con la instalación.",
            SetupTheme.Font(9.25F),
            SetupTheme.TextSecondary);
        hint.Padding = new Padding(0, 5, 0, 3);

        var divider = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SetupTheme.Divider,
            Margin = Padding.Empty
        };

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 10, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

        _cancelButton = new OutlineButton
        {
            Text = "Ahora no",
            AccessibleName = "Cancelar y mantener los programas abiertos",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 7, 0)
        };
        _closeAndContinueButton = new GradientButton
        {
            Text = "Cerrar y continuar",
            AccessibleName = "Cerrar los programas indicados y continuar",
            Dock = DockStyle.Fill,
            Margin = new Padding(7, 0, 0, 0)
        };
        _cancelButton.Click += async (_, _) => await CloseWithResultAsync(DialogResult.Cancel);
        _closeAndContinueButton.Click += async (_, _) => await CloseWithResultAsync(DialogResult.Yes);
        actions.Controls.Add(_cancelButton, 0, 0);
        actions.Controls.Add(_closeAndContinueButton, 1, 0);

        content.Controls.Add(eyebrow, 0, 0);
        content.Controls.Add(message, 0, 1);
        content.Controls.Add(list, 0, 2);
        content.Controls.Add(hint, 0, 3);
        content.Controls.Add(divider, 0, 4);
        content.Controls.Add(actions, 0, 5);
        _card.Controls.Add(content);
        Controls.Add(_card);

        AcceptButton = _closeAndContinueButton;
        CancelButton = _cancelButton;
    }

    protected override async void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        if (Owner is not null)
        {
            Bounds = Owner.RectangleToScreen(Owner.ClientRectangle);
            _blurredOwner = CaptureBlurred(Owner);
        }

        CenterCard();
        if (!SetupMotion.IsEnabled)
        {
            _closeAndContinueButton.Focus();
            return;
        }

        await SetupMotion.RunAsync(SetupTheme.MotionStandard, progress =>
        {
            Opacity = progress;
            Invalidate();
        });
        _closeAndContinueButton.Focus();
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        CenterCard();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        if (_blurredOwner is not null)
        {
            eventArgs.Graphics.DrawImage(_blurredOwner, ClientRectangle);
        }
        else
        {
            eventArgs.Graphics.Clear(SetupTheme.Surface0);
        }

        using var scrim = new SolidBrush(SetupTheme.ModalScrim);
        eventArgs.Graphics.FillRectangle(scrim, ClientRectangle);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            _ = CloseWithResultAsync(DialogResult.Cancel);
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blurredOwner?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task CloseWithResultAsync(DialogResult result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _closeAndContinueButton.Enabled = false;
        _cancelButton.Enabled = false;
        var startOpacity = Opacity;
        await SetupMotion.RunAsync(SetupTheme.MotionFast, progress =>
        {
            Opacity = startOpacity * (1D - progress);
        });
        DialogResult = result;
        Close();
    }

    private void CenterCard()
    {
        _card.Left = Math.Max(12, (ClientSize.Width - _card.Width) / 2);
        _card.Top = Math.Max(12, (ClientSize.Height - _card.Height) / 2);
    }

    private static Label Label(string text, Font font, Color color) =>
        new()
        {
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

    private static Bitmap CaptureBlurred(Control owner)
    {
        var width = Math.Max(1, owner.ClientSize.Width);
        var height = Math.Max(1, owner.ClientSize.Height);
        using var capture = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        owner.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));

        var reducedWidth = Math.Max(1, width / 12);
        var reducedHeight = Math.Max(1, height / 12);
        using var reduced = new Bitmap(reducedWidth, reducedHeight, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(reduced))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(capture, new Rectangle(Point.Empty, reduced.Size));
        }

        var blurred = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(blurred))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(reduced, new Rectangle(Point.Empty, blurred.Size));
        }

        return blurred;
    }
}
