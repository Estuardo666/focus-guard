namespace FocusGuard.Desktop;

internal static class ApplicationPresentation
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, InstalledApplication> Applications = new(StringComparer.OrdinalIgnoreCase);
    public static void Register(IReadOnlyList<InstalledApplication> applications)
    {
        foreach (var app in applications) { Applications[app.Path] = app; Applications.TryAdd(System.IO.Path.GetFileName(app.Path), app); }
    }
    public static string Name(string value) => Applications.TryGetValue(value, out var app) ? app.Name
        : value.Length == 64 && value.All(Uri.IsHexDigit) ? FocusGuard.Desktop.Resources.UiStrings.HashedApplication
        : System.IO.Path.GetFileNameWithoutExtension(value.TrimEnd('\\', '/'));
    public static string PathFor(string value) => Applications.TryGetValue(value, out var app) ? app.Path : value;
}
