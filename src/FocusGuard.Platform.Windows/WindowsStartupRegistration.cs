using System.Security;
using Microsoft.Win32;
using FocusGuard.Client.Platform;

namespace FocusGuard.Platform.Windows;

public sealed class WindowsStartupRegistration(string executablePath) : IStartupRegistration
{
    internal const string MinimizedArgument = "--minimized";
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FocusGuard.Desktop";
    private readonly string _command = BuildCommand(executablePath);

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
                return string.Equals(
                    key?.GetValue(ValueName) as string,
                    _command,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is IOException or SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, _command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return IsEnabled == enabled;
        }
        catch (Exception exception) when (
            exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\" {MinimizedArgument}";
    }
}

