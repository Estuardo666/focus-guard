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

    [Theory]
    [InlineData("en", "light/dark mode", "color palette", "orb style", "Mist or Ocean")]
    [InlineData("es", "modo claro/oscuro", "paleta de color", "estilo del orbe", "Bruma u Océano")]
    public void SharedSettingsDialogsClarifyThatColorPaletteAndOrbStyleStayLocal(string cultureName,
        string appearanceMode, string colorPalette, string orbStyle, string paletteExamples)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var scopeKeys = new[]
            {
                "SettingsSyncConsentDetail", "SettingsTransferShareDetail", "SettingsTransferImportDetail",
                "PortableSettingsExportDetail", "PortableSettingsImportDetail", "SettingsSyncResetDetail",
                "SettingsLocalOverridesDetail",
            };

            foreach (var key in scopeKeys)
            {
                var detail = HostWindowStrings.Get(key);
                Assert.Contains(colorPalette, detail, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(orbStyle, detail, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains(appearanceMode, HostWindowStrings.Get("SettingsSyncConsentDetail"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(appearanceMode, HostWindowStrings.Get("SettingsTransferThemeShare"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(appearanceMode, HostWindowStrings.Get("SettingsTransferThemeImport"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(appearanceMode, HostWindowStrings.Get("PortableSettingsTheme"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(appearanceMode, HostWindowStrings.Get("SettingsLocalOverridesKeepTheme"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(paletteExamples, HostWindowStrings.Get("SettingsSyncConsentDetail"), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
