using System.IO.Compression;
using System.Reflection;

namespace FocusGuard.Setup;

internal sealed class EmbeddedPackage : IDisposable
{
    internal const string ResourceName = "FocusGuard.Payload.zip";
    private const int MaximumEntries = 50_000;
    private const long MaximumUncompressedBytes = 2L * 1024 * 1024 * 1024;
    private readonly string? _temporaryDirectory;

    private EmbeddedPackage(string packageDirectory, string? temporaryDirectory)
    {
        PackageDirectory = packageDirectory;
        _temporaryDirectory = temporaryDirectory;
    }

    internal string PackageDirectory { get; }

    internal static EmbeddedPackage Open(string adjacentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adjacentDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        using var payload = assembly.GetManifestResourceStream(ResourceName);
        if (payload is null)
        {
            return new EmbeddedPackage(Path.GetFullPath(adjacentDirectory), null);
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "FocusGuard.Setup",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            Extract(payload, temporaryDirectory);
            ValidateRequiredFiles(temporaryDirectory);
            return new EmbeddedPackage(temporaryDirectory, temporaryDirectory);
        }
        catch
        {
            TryDelete(temporaryDirectory);
            throw;
        }
    }

    internal static void ValidateRequiredFiles(string packageDirectory)
    {
        string[] required =
        [
            "install-focusguard.ps1",
            "uninstall-focusguard.ps1",
            "manifest.json",
            "New-FocusGuardIpcPolicy.ps1",
            "Get-FocusGuardInteractiveOwnerSid.ps1",
            @"publish\FocusGuard.Service\FocusGuard.Service.exe",
            @"publish\FocusGuard.Recovery\FocusGuard.Recovery.exe",
            @"publish\FocusGuard.Desktop\FocusGuard.Desktop.exe",
            @"publish\FocusGuard.ProcessAgent\FocusGuard.ProcessAgent.exe",
            @"publish\FocusGuard.NativeMessaging\FocusGuard.NativeMessaging.exe",
            @"publish\FocusGuard.Host.Windows\FocusGuard.UI.exe",
            @"publish\FocusGuard.Host.Windows\FocusGuard.UI.dll",
            @"publish\FocusGuard.Host.Windows\ui\index.html",
            @"publish\FocusGuard.Host.Windows\ui\host.js",
            @"publish\FocusGuard.Host.Windows\ui\host.css",
            @"publish\FocusGuard.Host.Windows\ui\chunks\ScheduleEditor.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\PreferencesPanel.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\ProfileEditor.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\SessionHistoryDetail.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\DashboardStart.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\CompactFocus.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\SessionEndExperience.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\focus-time.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\WorkflowViews.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\StrictReviewPanel.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\strict.js",
            @"publish\FocusGuard.Host.Windows\ui\chunks\count-label.js",
            @"native-messaging\focusguard.native.json",
            @"browser-extension\distribution.json"
        ];
        foreach (var relativePath in required)
        {
            if (!File.Exists(Path.Combine(packageDirectory, relativePath)))
            {
                throw new InvalidDataException($"Embedded installer payload is incomplete: {relativePath}");
            }
        }
    }

    public void Dispose()
    {
        if (_temporaryDirectory is not null)
        {
            TryDelete(_temporaryDirectory);
        }
    }

    private static void Extract(Stream payload, string destination)
    {
        var root = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("Embedded installer payload contains too many entries.");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumUncompressedBytes)
            {
                throw new InvalidDataException("Embedded installer payload is larger than the supported limit.");
            }

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Embedded installer payload contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
