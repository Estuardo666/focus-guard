using System.Reflection;
using System.Runtime.InteropServices;
using FocusGuard.Client.Platform;

namespace FocusGuard.Platform.Windows;

/// <summary>Read-only Start menu discovery. Shortcut arguments and targets are never executed.</summary>
public sealed class WindowsApplicationCatalog : IApplicationCatalog
{
    public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token) => Task.Run<IReadOnlyList<InstalledApplication>>(() =>
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<InstalledApplication>();
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) return [];
        var shell = Activator.CreateInstance(type);
        if (shell is null) return [];
        try
        {
            foreach (var root in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
            {
                var directory = Environment.GetFolderPath(root);
                if (!Directory.Exists(directory)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(directory, "*.lnk", options).Take(1000))
                {
                    token.ThrowIfCancellationRequested();
                    object? shortcut = null;
                    try
                    {
                        shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [file]);
                        var target = shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                        if (!string.IsNullOrWhiteSpace(target) && string.Equals(System.IO.Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                            result.Add(new(System.IO.Path.GetFileNameWithoutExtension(file), System.IO.Path.GetFullPath(target)));
                    }
                    catch (Exception ex) when (ex is COMException or TargetInvocationException or ArgumentException) { }
                    finally { if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return result.DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, token);
}

