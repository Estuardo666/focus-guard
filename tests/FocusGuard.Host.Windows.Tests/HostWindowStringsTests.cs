using System.Globalization;
using FocusGuard.Host.Windows.Hosting;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostWindowStringsTests
{
    [Theory]
    [InlineData("en", "Kept on this PC", "priority", "sessions")]
    [InlineData("es", "Se conserva en esta PC", "prioridad", "sesiones")]
    public void SharedResetConfirmationExplainsLocalOverridesRemainActive(string cultureName,
        string localOverrideLabel, string priorityWord, string sessionsWord)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var template = HostWindowStrings.Get("SettingsSyncResetDetail");
            var rendered = string.Format(CultureInfo.CurrentUICulture, template,
                "Light", "en", 50, "Dark", "es", 20);

            Assert.Contains(localOverrideLabel, rendered);
            Assert.Contains(priorityWord, rendered, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(sessionsWord, rendered, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
