using System.Globalization;
using System.Resources;

namespace FocusGuard.Host.Windows.Hosting;

internal static class HostWindowStrings
{
    private static readonly ResourceManager Resources = new(
        "FocusGuard.Host.Windows.Resources.HostStrings",
        typeof(HostWindowStrings).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
