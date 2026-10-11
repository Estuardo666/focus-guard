using System.Diagnostics;
using System.Text;

namespace FocusGuard.Setup;

/// <summary>
/// What the installer actually does, independent of which window shows it: find programs holding FocusGuard files,
/// close the ones the person agreed to close, and run the packaged install/uninstall scripts with a clean PowerShell.
/// </summary>
internal sealed class SetupOperations(string packageDirectory, Action<string?> log)
{
    internal const string FocusGuardServiceDisplayName = "FocusGuard Service";

    internal string PackageDirectory { get; } = Path.GetFullPath(packageDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FocusGuard");

    internal static bool IsInstalled => File.Exists(Path.Combine(InstallRoot, "FocusGuard.Service.exe"));

    /// <summary>Programs that must be closed before files can be replaced. The service and the browser host are stopped by the script itself.</summary>
    internal async Task<IReadOnlyList<BlockingProcess>> FindBlockersAsync()
    {
        var detected = await Task.Run(() => RestartManagerPreflight.FindBlockingProcesses(InstallRoot));
        if (detected.Any(IsFocusGuardServiceBlocker))
            log("FocusGuard Service will be stopped gracefully before replacing installed files.");
        if (detected.Any(IsNativeMessagingBlocker))
            log("Native Messaging will be suspended and stopped by the installer before replacing installed files.");
        var blocking = GetProcessesToClose(detected);
        if (blocking.Count > 0)
            log($"Programas detectados usando archivos de FocusGuard: {string.Join(", ", blocking.Select(process => process.ApplicationName))}.");
        return blocking;
    }

    internal static bool IsFocusGuardServiceBlocker(BlockingProcess process) =>
        string.Equals(process.ApplicationName, FocusGuardServiceDisplayName, StringComparison.OrdinalIgnoreCase);

    internal static bool IsNativeMessagingBlocker(BlockingProcess process) =>
        string.Equals(process.ApplicationName, "FocusGuard.NativeMessaging", StringComparison.OrdinalIgnoreCase)
        || string.Equals(process.ApplicationName, "FocusGuard.NativeMessaging.exe", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<BlockingProcess> GetProcessesToClose(IEnumerable<BlockingProcess> detected) =>
        detected.Where(process => !IsFocusGuardServiceBlocker(process) && !IsNativeMessagingBlocker(process)).ToArray();

    internal void CloseProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            log($"Cerrando {process.ProcessName} (PID {processId}).");
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

    internal Task<int> InstallAsync() =>
        RunPowerShellAsync("install-focusguard.ps1", $"-PackageDirectory {Quote(PackageDirectory)} -ClientMetadataQueryConsent");

    internal Task<int> UninstallAsync() => RunPowerShellAsync("uninstall-focusguard.ps1", string.Empty);

    internal async Task<int> RunPowerShellAsync(string scriptName, string arguments)
    {
        var scriptPath = Path.Combine(PackageDirectory, scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"No se encontró el script del instalador: {scriptPath}");
        }

        var windowsPowerShellRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0");
        log($"> {scriptName}");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(windowsPowerShellRoot, "powershell.exe"),
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} {arguments}",
            WorkingDirectory = PackageDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // An elevated installer must not auto-import a user-planted module.
        // All cmdlets used here are available in the Windows PowerShell system modules.
        startInfo.Environment["PSModulePath"] = Path.Combine(windowsPowerShellRoot, "Modules");
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => log(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => log(eventArgs.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("No se pudo iniciar el motor de instalación.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        log(process.ExitCode == 0
            ? "Operación completada."
            : $"La operación terminó con código {process.ExitCode}.");
        return process.ExitCode;
    }

    /// <summary>Opens the installed app at the signed-in user's integrity level, even though Setup itself runs elevated.</summary>
    internal async Task<bool> OpenInstalledFocusGuardAsync()
    {
        var executable = Path.Combine(InstallRoot, "FocusGuard.UI.exe");
        if (!File.Exists(executable))
        {
            log($"No se encontró FocusGuard en la ruta esperada: {executable}");
            return false;
        }

        try
        {
            var activation = new InstalledAppActivation(() => InteractiveAppLauncher.Open(executable), () => InteractiveAppLauncher.IsPresented(executable));
            var opened = await activation.OpenAsync(TimeSpan.FromSeconds(5));
            if (!opened) log("FocusGuard did not present its window; Setup remains open so opening can be retried.");
            return opened;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            log($"No se pudo abrir FocusGuard: {exception.Message}");
            return false;
        }
    }

    internal static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
