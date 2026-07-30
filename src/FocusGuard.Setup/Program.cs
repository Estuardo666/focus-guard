using System.Diagnostics;
using System.Text;

namespace FocusGuard.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(AppContext.BaseDirectory));
    }
}

internal sealed class SetupForm : Form
{
    private readonly string _packageDirectory;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;
    private readonly TextBox _log;

    public SetupForm(string packageDirectory)
    {
        _packageDirectory = Path.GetFullPath(packageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Text = "FocusGuard Setup";
        MinimumSize = new Size(680, 440);
        Size = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 24, 39);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            AutoSize = true,
            Text = "FocusGuard",
            Font = new Font("Segoe UI Semibold", 24F),
            ForeColor = Color.White,
            Location = new Point(32, 26)
        };
        var subtitle = new Label
        {
            AutoSize = false,
            Text = "Instala el servicio de FocusGuard, la aplicación de escritorio y la integración segura del navegador.",
            ForeColor = Color.FromArgb(190, 200, 220),
            Location = new Point(35, 72),
            Size = new Size(660, 42)
        };
        var package = new Label
        {
            AutoSize = false,
            Text = $"Paquete: {_packageDirectory}",
            ForeColor = Color.FromArgb(160, 175, 200),
            Location = new Point(35, 120),
            Size = new Size(660, 42)
        };

        _installButton = CreateButton("Instalar FocusGuard", 35, 174, InstallAsync);
        _uninstallButton = CreateButton("Desinstalar", 220, 174, UninstallAsync);
        var warning = new Label
        {
            AutoSize = false,
            Text = "La instalación requiere privilegios de administrador. Las sesiones activas o la recuperación pendiente bloquean la desinstalación.",
            ForeColor = Color.FromArgb(255, 190, 80),
            Location = new Point(35, 222),
            Size = new Size(660, 40)
        };

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(28, 35, 52),
            ForeColor = Color.FromArgb(220, 230, 245),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(35, 275),
            Size = new Size(660, 145),
            AccessibleName = "Installer status"
        };

        Controls.AddRange([title, subtitle, package, _installButton, _uninstallButton, warning, _log]);
        Shown += (_, _) => AppendLog("Listo. El instalador solicitará UAC antes de modificar el equipo.");
    }

    private Button CreateButton(string text, int x, int y, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(170, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(124, 107, 242),
            ForeColor = Color.White,
            AccessibleName = text
        };
        button.Click += async (_, _) => await RunActionAsync(action);
        return button;
    }

    private async Task InstallAsync()
    {
        if (!await ClearBlockingProcessesAsync())
        {
            AppendLog("Instalación cancelada: hay programas usando archivos de FocusGuard.");
            return;
        }

        await RunPowerShellAsync("install-focusguard.ps1", $"-PackageDirectory {Quote(_packageDirectory)}");
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

        await RunPowerShellAsync("uninstall-focusguard.ps1", string.Empty);
    }

    /// <summary>
    /// Finds any program holding a FocusGuard file open (Restart Manager -- the same
    /// check Windows Installer runs) and offers to close it, so a locked-file failure
    /// never reaches someone with no way to diagnose it themselves. Retries a few times
    /// since closing one program can be immediately replaced by another (a browser
    /// respawning its native-messaging host, for instance).
    /// </summary>
    private async Task<bool> ClearBlockingProcessesAsync()
    {
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "FocusGuard");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var blocking = await Task.Run(() => RestartManagerPreflight.FindBlockingProcesses(installRoot));
            if (blocking.Count == 0)
            {
                return true;
            }

            AppendLog($"Programas detectados usando archivos de FocusGuard: {string.Join(", ", blocking.Select(process => process.ApplicationName))}.");
            using var dialog = new BlockingProcessesForm(blocking);
            if (dialog.ShowDialog(this) != DialogResult.Yes)
            {
                return false;
            }

            foreach (var process in blocking)
            {
                CloseProcess(process.ProcessId);
            }

            await Task.Delay(500);
        }

        AppendLog("No se pudo liberar todos los archivos de FocusGuard; la instalación puede fallar.");
        return true;
    }

    private void CloseProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            AppendLog($"Cerrando {process.ProcessName} (PID {processId}).");
            if (!process.CloseMainWindow() || !process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            // The process may already have exited on its own between the scan and here.
        }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        _installButton.Enabled = false;
        _uninstallButton.Enabled = false;
        try { await action(); }
        catch (Exception exception) { AppendLog($"ERROR: {exception.Message}"); }
        finally
        {
            _installButton.Enabled = true;
            _uninstallButton.Enabled = true;
        }
    }

    private async Task RunPowerShellAsync(string scriptName, string arguments)
    {
        var scriptPath = Path.Combine(_packageDirectory, scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"No se encontró el script del instalador: {scriptPath}");
        }

        AppendLog($"> {scriptName}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} {arguments}",
            WorkingDirectory = _packageDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => AppendLog(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendLog(eventArgs.Data);
        if (!process.Start()) { throw new InvalidOperationException("No se pudo iniciar el motor de instalación."); }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        AppendLog(process.ExitCode == 0 ? "Operación completada." : $"La operación terminó con código {process.ExitCode}.");
    }

    private void AppendLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) { return; }
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        _log.AppendText(message + Environment.NewLine);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
