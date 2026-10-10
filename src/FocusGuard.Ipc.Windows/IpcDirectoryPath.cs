namespace FocusGuard.Ipc.Windows;

internal static class IpcDirectoryPath
{
    public static string Normalize(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Absolute IPC deployment directory required.");
        var full = Path.GetFullPath(directory);
        var rootLength = Path.GetPathRoot(full)!.Length;
        while (full.Length > rootLength && Path.EndsInDirectorySeparator(full)) full = full[..^1];
        return full;
    }
}
