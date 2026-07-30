using System.Globalization;
using System.Xml.Linq;
using Avalonia.Controls;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void SpanishCultureLoadsCriticalFocusAndRecoveryStrings()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-EC");

            Assert.Equal("¿Listo para concentrarte?", UiStrings.ReadyTitle);
            Assert.Equal("Sitios bloqueados", UiStrings.BlockedWebsites);
            Assert.Equal("Quitar", UiStrings.Remove);
            Assert.Contains("recuperación", UiStrings.SaveRecoveryCode);
            Assert.Equal("Abrir FocusGuard", UiStrings.OpenFocusGuard);
            Assert.Equal("Salir de FocusGuard", UiStrings.ExitFocusGuard);
            Assert.Equal("Duplicar perfil", UiStrings.DuplicateProfile);
            Assert.Contains("Escribe", UiStrings.StrictConfirmationPrompt);
            Assert.Equal(UiStrings.StrictPhraseFirstWords, UiStrings.Get("StrictPhraseFirstWords"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(WindowCloseReason.Undefined, true)]
    [InlineData(WindowCloseReason.WindowClosing, true)]
    [InlineData(WindowCloseReason.ApplicationShutdown, false)]
    [InlineData(WindowCloseReason.OSShutdown, false)]
    public void NotificationAreaClosePolicyOnlyHidesOrdinaryWindowClose(
        WindowCloseReason reason,
        bool expected)
    {
        Assert.Equal(expected, NotificationAreaClosePolicy.ShouldHide(reason));
    }

    [Fact]
    public void EnglishCatalogContainsCriticalAccessibilityCopy()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("Blocked applications", UiStrings.BlockedApplications);
            Assert.Contains("future sessions", UiStrings.FutureSessionsOnly);
            Assert.Equal("Duplicate profile", UiStrings.DuplicateProfile);
            Assert.Contains("Type", UiStrings.StrictConfirmationPrompt);
            Assert.Contains("Focus", UiStrings.StrictPhraseSecondWords);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void SpanishCatalogContainsEveryEnglishResourceKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resourceDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "FocusGuard.Desktop",
            "Resources");
        var english = ResourceKeys(Path.Combine(resourceDirectory, "UiStrings.resx"));
        var spanish = ResourceKeys(Path.Combine(resourceDirectory, "UiStrings.es.resx"));

        Assert.Equal(english, spanish);
    }

    private static string[] ResourceKeys(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusGuard.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the FocusGuard repository root.");
    }
}
