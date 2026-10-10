using System.Globalization;
using System.Xml.Linq;

namespace FocusGuard.Desktop.Tests;

public sealed partial class DesignSystemTests
{
    [Theory]
    [InlineData("Theme.Light.axaml")]
    [InlineData("Theme.Dark.axaml")]
    public void SmallTextAndHealthWarningsMeetWcagAa(string theme)
    {
        var document = LoadDesktopXaml("Styles", theme);
        foreach (var foreground in new[] { "FgTextPrimary", "FgTextSecondary", "FgTextTertiary" })
        {
            foreach (var background in new[] { "FgMainSurface", "FgSurface1", "FgSurface2", "FgSurface3", "FgFieldSurface" })
            {
                AssertReadable(BrushColor(document, foreground), BrushColor(document, background), $"{theme}: {foreground}/{background}");
            }
        }

        foreach (var state in new[] { "Success", "Warning", "Danger", "Info" })
        {
            AssertReadable(BrushColor(document, $"Fg{state}"), BrushColor(document, $"Fg{state}Soft"), $"{theme}: {state}");
        }
    }

    [Theory]
    [InlineData("Theme.Light.axaml")]
    [InlineData("Theme.Dark.axaml")]
    public void EnabledPrimaryActionLabelsMeetWcagAcrossTheirGradients(string theme)
    {
        var document = LoadDesktopXaml("Styles", theme);
        var foreground = BrushColor(document, "FgPrimaryButtonForeground");
        foreach (var key in new[] { "FgPrimaryActionGradient", "FgPrimaryActionHoverGradient", "FgPrimaryActionPressedGradient" })
        {
            var colors = GradientColors(document, key).Select(Rgb).ToArray();
            for (var segment = 1; segment < colors.Length; segment++)
            {
                // Endpoints alone miss a low-contrast interior in a multicolour CTA.
                for (var sample = 0; sample <= 100; sample++)
                {
                    var amount = sample / 100.0;
                    var blended = colors[segment - 1].Zip(colors[segment], (a, b) => a + ((b - a) * amount)).ToArray();
                    var contrast = Contrast(SrgbLuminance(Rgb(foreground)), SrgbLuminance(blended));
                    Assert.True(contrast >= 4.5, $"{theme}: {key} segment {segment}, sample {sample}: {contrast:F2}:1");
                }
            }
        }
    }

    [Fact]
    public void DestinationsScrollWithoutDisplacingSettings()
    {
        var window = LoadDesktopXaml("MainWindow.axaml");
        var sidebar = Assert.Single(window.Descendants(Avalonia + "Border"), e => (string?)e.Attribute(X + "Name") == "NavigationSidebar");
        var scroll = Assert.Single(sidebar.Descendants(Avalonia + "ScrollViewer"));
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal(4, scroll.Descendants(Avalonia + "Button").Count());
        var settings = Assert.Single(sidebar.Descendants(Avalonia + "Button"), e => (string?)e.Attribute("Click") == "SettingsNavigationClick");
        Assert.DoesNotContain(settings.Ancestors(), e => e == scroll);
        Assert.Equal("Bottom", (string?)settings.Attribute("DockPanel.Dock"));
        Assert.Equal("{Binding IsSettingsVisible}", (string?)settings.Attribute("Classes.selected"));
        Assert.Equal(scroll, sidebar.Descendants(Avalonia + "DockPanel").Single().Elements().Last());

        var tokens = LoadDesktopXaml("Styles", "Tokens.axaml");
        var target = double.Parse(ResourceValue(tokens, "Double", "FgNavItemHeight"), CultureInfo.InvariantCulture);
        Assert.True(target >= 40, "Navigation must meet the UX specification's 40 DIP target.");
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        AssertStyleSetter(controls, "Button.navItem", "BorderThickness", "1");
        AssertStyleSetter(controls, "Button.navItem", "BorderBrush", "Transparent");
    }

    private static string BrushColor(XDocument document, string key) =>
        (string)document.Descendants(Avalonia + "SolidColorBrush").Single(e => (string?)e.Attribute(X + "Key") == key).Attribute("Color")!;

    private static double[] Rgb(string hex) => Enumerable.Range(0, 3)
        .Select(i => int.Parse(hex.AsSpan(1 + (i * 2), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0)
        .ToArray();

    private static double SrgbLuminance(double[] rgb)
    {
        var linear = rgb.Select(c => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)).ToArray();
        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    private static void AssertReadable(string foreground, string background, string context)
    {
        var contrast = Contrast(SrgbLuminance(Rgb(foreground)), SrgbLuminance(Rgb(background)));
        Assert.True(contrast >= 4.5, $"{context}: {contrast:F2}:1, expected at least 4.5:1.");
    }
}
