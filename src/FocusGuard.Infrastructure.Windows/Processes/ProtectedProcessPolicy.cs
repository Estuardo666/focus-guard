namespace FocusGuard.Infrastructure.Windows.Processes;

public sealed class ProtectedProcessPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Registry",
        "smss.exe",
        "csrss.exe",
        "wininit.exe",
        "services.exe",
        "lsass.exe",
        "winlogon.exe",
        "dwm.exe",
        "explorer.exe",
        "taskmgr.exe",
        "regedit.exe",
        "msconfig.exe",
        "narrator.exe",
        "magnify.exe",
        "utilman.exe",
        "sethc.exe",
        "msmpeng.exe",
        "securityhealthservice.exe",
        "securityhealthsystray.exe",
        "recoverydrive.exe",
        "FocusGuard.Desktop.exe",
        "FocusGuard.UI.exe",
        "FocusGuard.Service.exe",
        "FocusGuard.Recovery.exe"
    };

    public bool IsProtected(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return ProtectedNames.Contains(Path.GetFileName(executablePath));
    }
}
