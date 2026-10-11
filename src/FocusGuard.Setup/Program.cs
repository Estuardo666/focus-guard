using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FocusGuard.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--smoke-restart-manager-filter", StringComparer.Ordinal))
        {
            return SetupForm.RunRestartManagerFilterSmokeTest();
        }

        using var package = EmbeddedPackage.Open(AppContext.BaseDirectory);
        var packageHashIndex = Array.IndexOf(args, "--package-file-hash");
        if (packageHashIndex >= 0)
        {
            if (packageHashIndex + 2 >= args.Length)
            {
                throw new ArgumentException("Package hash validation requires a relative file and receipt path.");
            }

            var packageRoot = Path.GetFullPath(package.PackageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var packagedFile = Path.GetFullPath(Path.Combine(packageRoot, args[packageHashIndex + 1]));
            if (!packagedFile.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(packagedFile))
            {
                throw new InvalidDataException("Package hash validation target is unsafe or missing.");
            }

            var receiptPath = Path.GetFullPath(args[packageHashIndex + 2]);
            File.WriteAllText(receiptPath, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagedFile))));
            return 0;
        }

        if (args.Contains("--package-self-test", StringComparer.Ordinal))
        {
            EmbeddedPackage.ValidateRequiredFiles(package.PackageDirectory);
            return 0;
        }

        if (args.Contains("--smoke-open-installed", StringComparer.Ordinal))
        {
            var operations = new SetupOperations(package.PackageDirectory, Console.WriteLine);
            var open = operations.OpenInstalledFocusGuardAsync();
            RunWithMessagePump(open);
            return open.GetAwaiter().GetResult() ? 0 : 5;
        }

        if (args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            using var form = new SetupForm(package.PackageDirectory);
            form.Show();
            Application.DoEvents();
            var controls = Descendants(form).ToArray();
            RunWithMessagePump(form.ShowSuccessForSmokeTestAsync());
            controls = Descendants(form).ToArray();
            using var modal = new BlockingProcessesForm([new BlockingProcess(42, "FocusGuard UI")]);
            if (controls.OfType<SetupTitleBar>().SingleOrDefault() is not { Visible: true }
                || controls.OfType<SetupFooter>().SingleOrDefault() is not { Visible: true }
                || controls.OfType<ShieldLockControl>().SingleOrDefault() is not { BackColor: var shieldBackground }
                || shieldBackground != Color.Transparent
                || controls.OfType<IconTextControl>().SingleOrDefault() is not { BackColor: var warningBackground }
                || warningBackground != Color.Transparent
                || controls.OfType<SuccessCheckControl>().SingleOrDefault() is not { Visible: true }
                || Descendants(modal).OfType<ModalCardPanel>().SingleOrDefault() is null
                || modal.FormBorderStyle != FormBorderStyle.None)
            {
                throw new InvalidOperationException("El layout inicial del instalador no superó la prueba visual.");
            }

            form.Hide();
            return 0;
        }

        var version = ReadPackageVersion(package.PackageDirectory);
        if (args.Contains("--smoke-web-ui", StringComparer.Ordinal))
        {
            using var smoke = new WebSetupForm(package.PackageDirectory, version, smoke: true);
            using var timeout = new System.Windows.Forms.Timer { Interval = 30_000 };
            timeout.Tick += (_, _) => smoke.Close();
            timeout.Start();
            Application.Run(smoke);
            return smoke.SmokeSucceeded ? 0 : 3;
        }

        // The React installer is the default. Without the WebView2 runtime (or with --classic) the classic window runs
        // the exact same operations.
        if (!args.Contains("--classic", StringComparer.Ordinal) && WebViewRuntimeAvailable())
        {
            var web = new WebSetupForm(package.PackageDirectory, version);
            Application.Run(web);
            var fallback = web.FallbackRequested;
            web.Dispose();
            if (!fallback) return 0;
        }

        Application.Run(new SetupForm(package.PackageDirectory));
        return 0;
    }

    private static bool WebViewRuntimeAvailable()
    {
        try { return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch (Exception exception) when (exception is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException
                                           or System.Runtime.InteropServices.COMException or DllNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>The version shown to the person, read from the package manifest; "0.0.0" if it cannot be read.</summary>
    private static string ReadPackageVersion(string packageDirectory)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(packageDirectory, "manifest.json")));
            var version = document.RootElement.TryGetProperty("Version", out var value) ? value.GetString() : null;
            return version is not null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+(\.\d+){1,3}$") ? version : "0.0.0";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return "0.0.0";
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RunWithMessagePump(Task task)
    {
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }

        task.GetAwaiter().GetResult();
    }
}

internal sealed class SetupForm : Form
{
    private readonly string _packageDirectory;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;
    private readonly Label _status;
    private readonly TextBox _log;
    private readonly Panel _logPanel;
    private readonly Control _mainContent;
    private readonly Control _successContent;
    private readonly SuccessCheckControl _successCheck;
    private readonly Panel _body;
    private bool _logHasAppeared;
    private readonly SetupOperations _operations;

    public SetupForm(string packageDirectory)
    {
        _packageDirectory = Path.GetFullPath(packageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _operations = new SetupOperations(_packageDirectory, AppendLog);

        SuspendLayout();
        Text = "FocusGuard Setup";
        AccessibleName = "Instalador de FocusGuard";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(915, 659);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SetupTheme.Surface0;
        ForeColor = SetupTheme.TextPrimary;
        Font = SetupTheme.Font(10F);
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        Opacity = SetupMotion.IsEnabled ? 0D : 1D;

        var titleBar = new SetupTitleBar(this) { Dock = DockStyle.Top };
        var footer = CreateFooter();
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(20, 38, 20, 20)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        var hero = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 9,
            Margin = Padding.Empty
        };
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 51F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));

        var shield = new ShieldLockControl
        {
            Anchor = AnchorStyles.None,
            Size = new Size(72, 72),
            AccessibleName = "FocusGuard, protección segura"
        };
        var title = CenteredLabel(
            "FocusGuard",
            SetupTheme.Font(28F),
            SetupTheme.TextPrimary);
        var subtitle = CenteredLabel(
            "Instala el servicio de FocusGuard, la aplicación de escritorio\n"
            + "y la integración segura.",
            SetupTheme.Font(11.5F),
            SetupTheme.TextSecondary);
        subtitle.Padding = new Padding(0, 4, 0, 0);

        var divider = new Panel
        {
            Anchor = AnchorStyles.None,
            BackColor = SetupTheme.Divider,
            Size = new Size(278, 1),
            Margin = Padding.Empty
        };

        var package = CenteredLabel(
            $"Paquete: {FriendlyPackageName(_packageDirectory)}",
            SetupTheme.Font(9.5F),
            SetupTheme.TextTertiary);
        package.AutoEllipsis = true;
        package.AccessibleDescription = _packageDirectory;

        var actions = new TableLayoutPanel
        {
            Anchor = AnchorStyles.None,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Size = new Size(396, 46),
            Margin = Padding.Empty
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _installButton = new GradientButton
        {
            Text = "Instalar",
            AccessibleName = "Instalar FocusGuard",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0)
        };
        _uninstallButton = new OutlineButton
        {
            Text = "Desinstalar",
            AccessibleName = "Desinstalar FocusGuard",
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0)
        };
        _installButton.Click += async (_, _) => await RunActionAsync(InstallAsync);
        _uninstallButton.Click += async (_, _) => await RunActionAsync(UninstallAsync);
        actions.Controls.Add(_installButton, 0, 0);
        actions.Controls.Add(_uninstallButton, 1, 0);

        var warning = new IconTextControl(
            "La instalación requiere privilegios de administrador.",
            "Las sesiones activas o la aplicación en ejecución se cerrarán.")
        {
            Anchor = AnchorStyles.Top,
            Size = new Size(500, 64)
        };

        _status = CenteredLabel(
            "Listo para instalar.",
            SetupTheme.Font(9F),
            SetupTheme.TextTertiary);
        _status.Dock = DockStyle.Top;
        _status.Height = 24;

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SetupTheme.Surface1,
            ForeColor = SetupTheme.TextSecondary,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = SetupTheme.Font(8.75F),
            AccessibleName = "Detalle de la instalación"
        };
        _logPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 104,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = SetupTheme.Surface1,
            Visible = false
        };
        _logPanel.Controls.Add(_log);

        hero.Controls.Add(shield, 0, 0);
        hero.Controls.Add(title, 0, 1);
        hero.Controls.Add(subtitle, 0, 2);
        hero.Controls.Add(divider, 0, 3);
        hero.Controls.Add(package, 0, 4);
        hero.Controls.Add(actions, 0, 5);
        hero.Controls.Add(warning, 0, 6);
        hero.Controls.Add(_status, 0, 7);
        content.Controls.Add(hero, 1, 0);

        _body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        _body.Controls.Add(content);
        _body.Controls.Add(_logPanel);
        _logPanel.BringToFront();
        _mainContent = content;
        (_successContent, _successCheck) = CreateSuccessContent();
        _body.Controls.Add(_successContent);
        _successContent.BringToFront();
        _successContent.Visible = false;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        shell.Controls.Add(titleBar, 0, 0);
        shell.Controls.Add(_body, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        Controls.Add(shell);
        AcceptButton = _installButton;
        Shown += async (_, _) =>
        {
            await SetupMotion.RunAsync(
                SetupTheme.MotionStandard,
                progress => Opacity = progress);
            _installButton.Focus();
        };
        ResumeLayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var baseBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
            ClientRectangle,
            SetupTheme.BackgroundStart,
            SetupTheme.BackgroundEnd,
            90F);
        e.Graphics.FillRectangle(baseBrush, ClientRectangle);

        var glowBounds = new RectangleF(
            -ClientSize.Width * 0.18F,
            ClientSize.Height * 0.42F,
            ClientSize.Width * 1.36F,
            ClientSize.Height * 0.88F);
        using var glowPath = new System.Drawing.Drawing2D.GraphicsPath();
        glowPath.AddEllipse(glowBounds);
        using var glow = new System.Drawing.Drawing2D.PathGradientBrush(glowPath)
        {
            CenterPoint = new PointF(ClientSize.Width / 2F, ClientSize.Height * 1.02F),
            CenterColor = SetupTheme.BottomGlow,
            SurroundColors = [Color.Transparent]
        };
        e.Graphics.FillPath(glow, glowPath);
    }

    private Panel CreateFooter()
    {
        var footer = new SetupFooter
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Ayuda de instalación disponible en la documentación incluida"
        };
        return footer;
    }

    private static Label CenteredLabel(string text, Font font, Color color) =>
        new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent
        };

    private static string FriendlyPackageName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private async Task InstallAsync()
    {
        if (MessageBox.Show(this,
            "La comunicación autenticada necesita permitir a LocalService consultar únicamente metadatos de cada proceso de FocusGuard (0x1000). No concede lectura de memoria, control de procesos ni acceso a credenciales. ¿Autorizas este permiso mientras los clientes de FocusGuard estén abiertos?",
            "Consentimiento de comunicación", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            AppendLog("Instalación cancelada: no se autorizó la consulta de metadatos.");
            return;
        }
        if (!await ClearBlockingProcessesAsync())
        {
            AppendLog("Instalación cancelada: hay programas usando archivos de FocusGuard.");
            return;
        }

        var exitCode = await RunPowerShellAsync(
            "install-focusguard.ps1",
            $"-PackageDirectory {Quote(_packageDirectory)} -ClientMetadataQueryConsent");
        if (exitCode == 0)
        {
            await ShowSuccessAsync();
        }
    }

    private async Task UninstallAsync()
    {
        var result = MessageBox.Show(
            "¿Desinstalar FocusGuard? La recuperación pendiente o una sesión estricta activa detendrá la operación.",
            "Confirmar desinstalación",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }

        if (!await ClearBlockingProcessesAsync())
        {
            AppendLog("Desinstalación cancelada: hay programas usando archivos de FocusGuard.");
            return;
        }

        _ = await RunPowerShellAsync("uninstall-focusguard.ps1", string.Empty);
    }

    private async Task<bool> ClearBlockingProcessesAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var blocking = await _operations.FindBlockersAsync();
            if (blocking.Count == 0)
            {
                return true;
            }

            using var dialog = new BlockingProcessesForm(blocking);
            if (dialog.ShowDialog(this) != DialogResult.Yes)
            {
                return false;
            }

            foreach (var process in blocking)
            {
                _operations.CloseProcess(process.ProcessId);
            }

            await Task.Delay(500);
        }

        AppendLog("No se pudo liberar todos los archivos de FocusGuard; la instalación puede fallar.");
        return true;
    }

    internal static int RunRestartManagerFilterSmokeTest()
    {
        var service = new BlockingProcess(42, "FocusGuard Service");
        var nativeMessaging = new BlockingProcess(43, "FocusGuard.NativeMessaging");
        var application = new BlockingProcess(44, "FocusGuard UI");
        var mixed = SetupOperations.GetProcessesToClose([service, nativeMessaging, application]);
        var serviceOnly = SetupOperations.GetProcessesToClose([service]);
        var nativeMessagingOnly = SetupOperations.GetProcessesToClose([nativeMessaging]);
        return mixed.Count == 1 && mixed[0].ProcessId == application.ProcessId
            && serviceOnly.Count == 0 && nativeMessagingOnly.Count == 0 ? 0 : 1;
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        _installButton.Enabled = false;
        _uninstallButton.Enabled = false;
        _status.Text = "Preparando la operación…";
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            AppendLog($"ERROR: {exception.Message}");
        }
        finally
        {
            _installButton.Enabled = true;
            _uninstallButton.Enabled = true;
        }
    }

    private Task<int> RunPowerShellAsync(string scriptName, string arguments) => _operations.RunPowerShellAsync(scriptName, arguments);

    internal Task ShowSuccessForSmokeTestAsync() => ShowSuccessAsync();

    private (Control Content, SuccessCheckControl Check) CreateSuccessContent()
    {
        var check = new SuccessCheckControl
        {
            Anchor = AnchorStyles.None,
            Size = new Size(104, 104),
            AccessibleName = "Instalación completada correctamente"
        };
        var title = CenteredLabel(
            "FocusGuard está listo",
            SetupTheme.Font(26F, FontStyle.Bold),
            SetupTheme.TextPrimary);
        var description = CenteredLabel(
            "La aplicación, el servicio y la integración segura se instalaron correctamente.",
            SetupTheme.Font(10.5F),
            SetupTheme.TextSecondary);
        description.Padding = new Padding(14, 0, 14, 0);

        var actions = new TableLayoutPanel
        {
            Anchor = AnchorStyles.None,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Size = new Size(448, 46)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66F));
        var exit = new OutlineButton
        {
            Text = "Salir",
            AccessibleName = "Salir del instalador",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0)
        };
        exit.Click += (_, _) => Close();
        var open = new GradientButton
        {
            Text = "Aceptar y abrir FocusGuard",
            AccessibleName = "Aceptar y abrir FocusGuard",
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0)
        };
        open.Click += async (_, _) => await OpenInstalledFocusGuardAsync();
        actions.Controls.Add(exit, 0, 0);
        actions.Controls.Add(open, 1, 0);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20, 70, 20, 28),
            AccessibleName = "Instalación completada"
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var detail = CenteredLabel(
            "Ya puedes iniciar tu primera sesión de enfoque.",
            SetupTheme.Font(9.25F),
            SetupTheme.TextTertiary);
        panel.Controls.Add(check, 0, 0);
        panel.Controls.Add(title, 0, 1);
        panel.Controls.Add(description, 0, 2);
        panel.Controls.Add(detail, 0, 3);
        panel.Controls.Add(actions, 0, 4);
        return (panel, check);
    }

    private async Task ShowSuccessAsync()
    {
        await SetupTransitions.CrossFadeAsync(_body, () =>
        {
            _logPanel.Visible = false;
            _mainContent.Visible = false;
            _successContent.Visible = true;
            _successContent.BringToFront();
        });
        _successCheck.Progress = 0F;
        await SetupMotion.RunAsync(SetupTheme.MotionStandard, progress =>
        {
            _successCheck.Progress = progress;
            _successContent.Invalidate(true);
        });
    }

    private async Task OpenInstalledFocusGuardAsync()
    {
        if (await _operations.OpenInstalledFocusGuardAsync())
        {
            Close();
            return;
        }

        await ReturnToMainContentAsync();
    }

    private Task ReturnToMainContentAsync() =>
        SetupTransitions.CrossFadeAsync(_body, () =>
        {
            _successContent.Visible = false;
            _mainContent.Visible = true;
            _logPanel.Visible = true;
            _logPanel.BringToFront();
        });

    private void AppendLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        _log.AppendText(message + Environment.NewLine);
        _status.Text = message;
        if (!_logHasAppeared && !_successContent.Visible)
        {
            _logHasAppeared = true;
            _ = SetupTransitions.CrossFadeAsync(
                _body,
                () =>
                {
                    _logPanel.Visible = true;
                    _logPanel.BringToFront();
                });
        }
    }

    private static string Quote(string value) => SetupOperations.Quote(value);
}
