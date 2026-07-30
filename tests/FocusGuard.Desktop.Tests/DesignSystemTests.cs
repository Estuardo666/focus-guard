using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FocusGuard.Desktop.Tests;

public sealed partial class DesignSystemTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void DarkZenPaletteMatchesTheApprovedSemanticTokens()
    {
        var document = LoadDesktopXaml("Styles", "Tokens.axaml");
        var colors = document
            .Descendants(Avalonia + "Color")
            .ToDictionary(
                element => (string)element.Attribute(X + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FgSurface0Color"] = "#0E1318",
            ["FgSurface1Color"] = "#11171E",
            ["FgMainSurfaceColor"] = "#151C24",
            ["FgSurface2Color"] = "#19212B",
            ["FgSurface3Color"] = "#1D2631",
            ["FgFieldSurfaceColor"] = "#111820",
            ["FgBorderColor"] = "#2A3542",
            ["FgTextPrimaryColor"] = "#EDF2F5",
            ["FgTextSecondaryColor"] = "#AEB9C4",
            ["FgTextTertiaryColor"] = "#7F8C99",
            ["FgAccentColor"] = "#667DFF",
            ["FgSuccessColor"] = "#67B99A",
            ["FgWarningColor"] = "#D8A657",
            ["FgDangerColor"] = "#D87878",
            ["FgFocusRingColor"] = "#7188FF",
        };

        foreach (var (key, value) in expected)
        {
            Assert.Equal(value, colors[key]);
        }

        Assert.Equal(
            ["#667DFF", "#766FF2", "#9073E8"],
            GradientColors(document, "FgPrimaryActionGradient"));
        Assert.Equal(
            ["#63B7B0", "#6F8CFF", "#A174E8"],
            GradientColors(document, "FgProgressGradient"));
    }

    [Fact]
    public void RoundedGeometryIsCentralizedAcrossControlsSurfacesAndPopups()
    {
        var tokens = LoadDesktopXaml("Styles", "Tokens.axaml");
        var radii = tokens
            .Descendants(Avalonia + "CornerRadius")
            .ToDictionary(
                element => (string)element.Attribute(X + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("10", radii["FgRadiusSmall"]);
        Assert.Equal("14", radii["FgRadiusMedium"]);
        Assert.Equal("20", radii["FgRadiusLarge"]);
        Assert.Equal("28", radii["FgRadiusExtraLarge"]);
        Assert.Equal("999", radii["FgRadiusPill"]);

        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        AssertThemeSetter(controls, "{x:Type Button}", "CornerRadius", "{DynamicResource FgRadiusMedium}");
        AssertCornerStyle(controls, "TextBox", "FgRadiusMedium");
        AssertCornerStyle(controls, "ComboBox", "FgRadiusMedium");
        AssertCornerStyle(controls, "ComboBoxItem", "FgRadiusMedium");
        AssertCornerStyle(controls, "ListBox", "FgRadiusLarge");
        Assert.Contains(
            FindTheme(controls, "FgPresetItem").Descendants(Avalonia + "Border"),
            border =>
                (string?)border.Attribute("CornerRadius")
                == "{DynamicResource FgRadiusMedium}");
        AssertCornerStyle(controls, "NumericUpDown", "FgRadiusMedium");
        AssertCornerStyle(controls, "FlyoutPresenter", "FgRadiusLarge");
        AssertCornerStyle(controls, "ToolTip", "FgRadiusMedium");
        AssertCornerStyle(controls, "Border.card", "FgRadiusLarge");
        AssertCornerStyle(controls, "Border.strictModal", "FgRadiusExtraLarge");
        AssertCornerStyle(controls, "ProgressBar", "FgRadiusPill");

        var desktopRoot = Path.Combine(FindRepositoryRoot(), "src", "FocusGuard.Desktop");
        foreach (var path in Directory.EnumerateFiles(desktopRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(HardCodedCornerAttribute(), source);
            Assert.DoesNotMatch(HardCodedCornerSetter(), source);
        }
    }

    [Fact]
    public void PrimaryActionsAndTimerRingsUseTheirZenGradients()
    {
        var controls = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "Styles",
                "Controls.axaml"));
        var mainWindow = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "MainWindow.axaml"));

        Assert.Contains(
            "Background\" Value=\"{DynamicResource FgPrimaryActionGradient}",
            controls,
            StringComparison.Ordinal);
        Assert.Contains(
            "Stroke=\"{DynamicResource FgProgressGradient}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Stroke=\"{DynamicResource ProfileAccentGradient}\"",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedControlTemplatesDoNotPaintNestedRectangles()
    {
        var document = LoadDesktopXaml("Styles", "Controls.axaml");
        var source = document.ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain(
            "Button:pointerover /template/ ContentPresenter",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Button:pressed /template/ ContentPresenter",
            source,
            StringComparison.Ordinal);

        var navigationTheme = FindTheme(document, "FgNavButton");
        Assert.DoesNotContain(
            "ProfileAccentSoft",
            navigationTheme.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);

        var toggleTheme = FindTheme(document, "FgToggleSwitch");
        Assert.Contains(
            toggleTheme.Descendants(),
            element => (string?)element.Attribute(X + "Name") == "PART_MovingKnobs");
        Assert.Contains(
            toggleTheme.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "Track");

        var checkTheme = FindTheme(document, "FgCheckBox");
        Assert.Contains(
            checkTheme.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "Box");

        AssertStyleSetter(
            document,
            "Button.recoveryCopy",
            "HorizontalContentAlignment",
            "Center");
        AssertStyleSetter(
            document,
            "Button.recoveryCopy",
            "VerticalContentAlignment",
            "Center");
        AssertStyleSetter(
            document,
            "Button.primary",
            "CornerRadius",
            "{DynamicResource FgRadiusMedium}");
    }

    [Fact]
    public void HaloScreenMotionAndHeaderAlignmentMatchTheReviewedDesign()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var ringGlow = FindStyle(controls, "Border.ringGlow");
        var breathingGlow = FindStyle(controls, "Border.ringGlow.pulsing");

        Assert.NotNull(ringGlow.Descendants(Avalonia + "BlurEffect").SingleOrDefault());
        AssertStyleSetter(
            controls,
            "Border.ringGlow",
            "BorderBrush",
            "{DynamicResource FgProgressGradient}");
        // 2.8s alternating is a 5.6s round trip, the cycle the style guide calls ideal.
        Assert.Contains(
            breathingGlow.Descendants(Avalonia + "Animation"),
            animation => (string?)animation.Attribute("Duration") == "0:0:2.8");
        Assert.Contains(
            breathingGlow.Descendants(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Opacity"
                && (string?)setter.Attribute("Value") == "0.48");

        // The halo has to spill outside the ring, not only hug it, so a second wider and
        // softer layer breathes across a larger opacity range than the near one.
        var outerGlow = FindStyle(controls, "Border.ringGlowOuter");
        Assert.NotNull(outerGlow.Descendants(Avalonia + "BlurEffect").SingleOrDefault());
        var breathingOuter = FindStyle(controls, "Border.ringGlowOuter.pulsing");
        Assert.Contains(
            breathingOuter.Descendants(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Opacity"
                && (string?)setter.Attribute("Value") == "0.32");

        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        Assert.Equal(
            4,
            mainWindow
                .Descendants(Avalonia + "ScrollViewer")
                .Count(element => (string?)element.Attribute("Classes") == "screen"));
        Assert.Equal(
            2,
            mainWindow
                .Descendants(Avalonia + "ToggleSwitch")
                .Count(element =>
                    (string?)element.Attribute("Theme")
                    == "{StaticResource FgToggleSwitch}"));
        Assert.Equal(
            2,
            mainWindow
                .Descendants(Avalonia + "CheckBox")
                .Count(element =>
                    (string?)element.Attribute("Theme")
                    == "{StaticResource FgCheckBox}"));

        var profileButton = Assert.Single(
            mainWindow.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute(X + "Name") == "ProfileButton");
        Assert.Equal("10,6", (string?)profileButton.Attribute("Padding"));

        var headerSubtitle = Assert.Single(
            mainWindow.Descendants(Avalonia + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding HeaderSubtitle}");
        Assert.Equal("10,0,0,0", (string?)headerSubtitle.Attribute("Margin"));
        Assert.Equal("Left", (string?)headerSubtitle.Attribute("HorizontalAlignment"));
        Assert.Equal("Left", (string?)headerSubtitle.Attribute("TextAlignment"));
    }

    [Fact]
    public void KeyframeAnimationsUseOnlyPropertiesWithRegisteredAvaloniaAnimators()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");

        Assert.DoesNotContain(
            controls.Descendants(Avalonia + "Animation").Descendants(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Property") == "RenderTransform");
    }

    [Fact]
    public void DedicatedThemesAreIsolatedFromGlobalStatePainting()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var selectors = controls
            .Descendants(Avalonia + "Style")
            .Select(style => (string?)style.Attribute("Selector"))
            .Where(selector => selector is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(
            controls.Descendants(Avalonia + "ControlTheme"),
            theme => (string?)theme.Attribute(X + "Key") == "{x:Type Button}");
        Assert.DoesNotContain("Button:pointerover", selectors);
        Assert.DoesNotContain("Button:pressed", selectors);
        Assert.DoesNotContain("Button:disabled", selectors);
        Assert.DoesNotContain("Button:focus-visible", selectors);
        Assert.DoesNotContain("ListBoxItem:pointerover", selectors);
        Assert.DoesNotContain("ListBoxItem:selected", selectors);

        var navigationTheme = FindTheme(controls, "FgNavButton");
        Assert.Contains(
            navigationTheme.Descendants(Avalonia + "Style"),
            style =>
                (string?)style.Attribute("Selector")
                    == "^:pointerover /template/ Border#Root"
                && style
                    .Elements(Avalonia + "Setter")
                    .Any(setter =>
                        (string?)setter.Attribute("Property") == "Background"
                        && (string?)setter.Attribute("Value")
                            == "{DynamicResource FgNavHover}"));
        Assert.Contains(
            navigationTheme.Descendants(Avalonia + "Style"),
            style =>
                (string?)style.Attribute("Selector")
                    == "^.active /template/ TextBlock.icon"
                && style
                    .Elements(Avalonia + "Setter")
                    .Any(setter =>
                        (string?)setter.Attribute("Property") == "Foreground"
                        && (string?)setter.Attribute("Value")
                            == "{DynamicResource ProfileAccent}"));
    }

    [Fact]
    public void CalmMotionContractUsesApprovedDurationsAndDoesNotDisableTheShell()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var standardButton = Assert.Single(
            controls.Descendants(Avalonia + "ControlTheme"),
            theme => (string?)theme.Attribute(X + "Key") == "{x:Type Button}");

        Assert.Contains(
            standardButton.Descendants(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "RenderTransform"
                && (string?)setter.Attribute("Value") == "scale(1.015)");
        Assert.Contains(
            standardButton.Descendants(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "RenderTransform"
                && (string?)setter.Attribute("Value") == "scale(0.985)");

        var screen = FindStyle(controls, "ScrollViewer.screen");
        Assert.Equal(
            "translateY(6px)",
            screen
                .Elements(Avalonia + "Setter")
                .Single(setter =>
                    (string?)setter.Attribute("Property") == "RenderTransform")
                .Attribute("Value")
                ?.Value);
        Assert.Contains(
            screen.Descendants(Avalonia + "DoubleTransition"),
            transition => (string?)transition.Attribute("Duration") == "0:0:0.26");

        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var dialog = Assert.Single(
            mainWindow.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "StrictReviewDialog");
        Assert.Equal("scale(0.97)", (string?)dialog.Attribute("RenderTransform"));
        Assert.Contains(
            dialog.Descendants(Avalonia + "DoubleTransition"),
            transition => (string?)transition.Attribute("Duration") == "0:0:0.28");

        var codeBehind = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "MainWindow.axaml.cs"));
        Assert.DoesNotContain("shell.IsEnabled =", codeBehind, StringComparison.Ordinal);
    }

    private static string[] GradientColors(XDocument document, string key)
    {
        var gradient = Assert.Single(
            document.Descendants(Avalonia + "LinearGradientBrush"),
            element => (string?)element.Attribute(X + "Key") == key);
        return gradient
            .Elements(Avalonia + "GradientStop")
            .Select(element => (string)element.Attribute("Color")!)
            .ToArray();
    }

    private static void AssertCornerStyle(XDocument document, string selector, string resourceKey)
    {
        var style = Assert.Single(
            document.Descendants(Avalonia + "Style"),
            element => (string?)element.Attribute("Selector") == selector);
        Assert.Contains(
            style.Elements(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "CornerRadius"
                && (string?)setter.Attribute("Value") == $"{{DynamicResource {resourceKey}}}");
    }

    private static XElement FindTheme(XDocument document, string key) =>
        Assert.Single(
            document.Descendants(Avalonia + "ControlTheme"),
            element => (string?)element.Attribute(X + "Key") == key);

    private static XElement FindStyle(XDocument document, string selector) =>
        Assert.Single(
            document.Descendants(Avalonia + "Style"),
            element => (string?)element.Attribute("Selector") == selector);

    private static void AssertStyleSetter(
        XDocument document,
        string selector,
        string property,
        string value)
    {
        var style = FindStyle(document, selector);
        Assert.Contains(
            style.Elements(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == property
                && (string?)setter.Attribute("Value") == value);
    }

    private static void AssertThemeSetter(
        XDocument document,
        string key,
        string property,
        string value)
    {
        var theme = FindTheme(document, key);
        Assert.Contains(
            theme.Elements(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == property
                && (string?)setter.Attribute("Value") == value);
    }

    private static XDocument LoadDesktopXaml(params string[] pathParts) =>
        XDocument.Load(
            Path.Combine(
                [FindRepositoryRoot(), "src", "FocusGuard.Desktop", .. pathParts]));

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

    [GeneratedRegex("""CornerRadius\s*=\s*"\d""", RegexOptions.CultureInvariant)]
    private static partial Regex HardCodedCornerAttribute();

    [GeneratedRegex("""Property="CornerRadius"\s+Value="\d""", RegexOptions.CultureInvariant)]
    private static partial Regex HardCodedCornerSetter();
}
