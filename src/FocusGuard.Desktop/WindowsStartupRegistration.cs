namespace FocusGuard.Desktop;

internal static class DesktopStartupOptions
{
    internal static bool ShouldStartMinimized(IEnumerable<string> arguments) =>
        arguments.Any(argument => string.Equals(
            argument,
            WindowsStartupRegistration.MinimizedArgument,
            StringComparison.OrdinalIgnoreCase));
}
