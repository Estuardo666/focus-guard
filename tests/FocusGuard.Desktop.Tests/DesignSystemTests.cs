using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FocusGuard.Desktop.Tests;

public sealed partial class DesignSystemTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void NativeWindowsTitleBarReplacesTheCustomCaption()
    {
        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var window = Assert.IsType<XElement>(mainWindow.Root);

        Assert.Equal("Full", (string?)window.Attribute("WindowDecorations"));
        Assert.Equal("False", (string?)window.Attribute("ExtendClientAreaToDecorationsHint"));
        Assert.DoesNotContain(
            window.Descendants(),
            element => (string?)element.Attribute("PointerPressed") == "TitleBarPointerPressed");
        Assert.DoesNotContain(
            window.Descendants(Avalonia + "Button"),
            button => ((string?)button.Attribute("Theme"))?.Contains(
                "CaptionButton",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void GuidedProfileCreationUsesExplicitAccessibleSuggestionControls()
    {
        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var suggestion = Assert.Single(
            mainWindow.Descendants(Avalonia + "ToggleButton"),
            element => ((string?)element.Attribute("Classes"))?.Contains(
                    "suggestion",
                    StringComparison.Ordinal) == true);

        Assert.Equal("{Binding IsSelected}", (string?)suggestion.Attribute("IsChecked"));
        Assert.Equal("{Binding DomainSummary}", (string?)suggestion.Attribute("ToolTip.Tip"));
        Assert.Contains(
            suggestion.Descendants(Avalonia + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding DomainSummary}");

        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var style = Assert.Single(
            controls.Descendants(Avalonia + "Style"),
            element => (string?)element.Attribute("Selector") == "ToggleButton.suggestion");
        // Compact chips, but never below the 32 px minimum pointer target Windows asks for.
        var minHeight = Assert.Single(
            style.Elements(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Property") == "MinHeight");
        Assert.True(double.Parse((string)minHeight.Attribute("Value")!, CultureInfo.InvariantCulture) >= 32);
    }

    [Fact]
    public void DarkThemeUsesGraphiteSurfacesAndMintActions()
    {
        var document = LoadDesktopXaml("Styles", "Theme.Dark.axaml");
        var colors = document
            .Descendants(Avalonia + "Color")
            .ToDictionary(
                element => (string)element.Attribute(X + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);

        // Every value below is a literal Catppuccin Mocha swatch. Anything invented
        // outside the palette is exactly the drift this test exists to catch.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FgSurface0Color"] = "#141517",
            ["FgSurface1Color"] = "#191A1D",
            ["FgMainSurfaceColor"] = "#222326",
            ["FgSurface3Color"] = "#383B40",
            ["FgFieldSurfaceColor"] = "#191A1D",
            ["FgBorderColor"] = "#383B40",
            ["FgBorderStrongColor"] = "#50545B",
            ["FgTextPrimaryColor"] = "#F0F2F4",
            ["FgTextSecondaryColor"] = "#B9BDC4",
            ["FgTextTertiaryColor"] = "#B9BDC4",
            ["FgAccentColor"] = "#C6EDDF",
            ["FgAccentSecondaryColor"] = "#89B4FA",
            ["FgSuccessColor"] = "#A6E3A1",
            ["FgWarningColor"] = "#F9E2AF",
            ["FgDangerColor"] = "#F38BA8",
            ["FgInfoColor"] = "#89B4FA",
            ["FgFocusRingColor"] = "#B4DCCC",
        };

        foreach (var (key, value) in expected)
        {
            Assert.Equal(value, colors[key]);
        }

        Assert.Equal(
            ["#C6EDDF", "#C6EDDF", "#C6EDDF"],
            GradientColors(document, "FgPrimaryActionGradient"));
        Assert.Equal(
            ["#94E2D5", "#89B4FA", "#C6EDDF"],
            GradientColors(document, "FgProgressGradient"));
    }

    /// <summary>
    /// The ring's two tick states have to stay distinguishable, or "this minute has not
    /// been spent yet" stops reading at a glance.
    /// </summary>
    [Fact]
    public void BothFlavoursSeparateLitAndSpentRingTicks()
    {
        foreach (var flavour in new[] { "Theme.Dark.axaml", "Theme.Light.axaml" })
        {
            var document = LoadDesktopXaml("Styles", flavour);
            var brushes = document
                .Descendants(Avalonia + "SolidColorBrush")
                .ToDictionary(
                    element => (string)element.Attribute(X + "Key")!,
                    element => (string)element.Attribute("Color")!,
                    StringComparer.Ordinal);

            Assert.True(
                Math.Abs(Luminance(brushes["FgRingTick"]) - Luminance(brushes["FgRingTickSpent"])) > 0.1,
                $"{flavour}: lit and spent ticks are too close in luminance to tell apart.");
        }
    }

    /// <summary>
    /// A profile recolours the ring and nothing else. If ProfileAccent leaks back into
    /// surfaces, borders or primary actions, switching profile repaints the whole app.
    /// </summary>
    [Fact]
    public void ProfileAccentIsConfinedToTheRing()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var accented = controls
            .Descendants(Avalonia + "Style")
            .Where(style => style
                .Descendants(Avalonia + "Setter")
                .Any(setter => ((string?)setter.Attribute("Value"))?.Contains(
                    "ProfileAccent",
                    StringComparison.Ordinal) == true))
            .Select(style => (string?)style.Attribute("Selector") ?? string.Empty)
            .ToArray();

        Assert.Equal(
            ["Border.ringGlow", "Border.ringGlowOuter"],
            accented);
    }

    /// <summary>
    /// A key present in one palette and missing from the other is invisible until someone
    /// switches theme and hits an unresolved resource, so the two files are compared by key.
    /// </summary>
    [Fact]
    public void LatteDefinesEveryTokenMochaDefinesAndInvertsTheSurfaces()
    {
        var dark = LoadDesktopXaml("Styles", "Theme.Dark.axaml");
        var light = LoadDesktopXaml("Styles", "Theme.Light.axaml");

        Assert.Equal(ThemeKeys(dark), ThemeKeys(light));

        var lightColors = light
            .Descendants(Avalonia + "Color")
            .ToDictionary(
                element => (string)element.Attribute(X + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);

        // The ambient surface has to be light and the body text dark, or the light theme
        // is only a name. Luminance is compared rather than exact hexes so the palette can
        // still be tuned without rewriting the test. The text thresholds are looser than
        // pure black would need: Latte's body text is a soft slate by design.
        Assert.True(Luminance(lightColors["FgSurface0Color"]) > 0.82);
        Assert.True(Luminance(lightColors["FgMainSurfaceColor"]) > 0.9);
        Assert.True(Luminance(lightColors["FgTextPrimaryColor"]) < 0.4);
        Assert.True(Luminance(lightColors["FgTextSecondaryColor"]) < 0.55);

        // Both shells paint a gradient rather than a flat slab.
        foreach (var document in new[] { dark, light })
        {
            foreach (var key in new[] { "FgAppBackgroundGradient", "FgContentGradient", "FgSidebarGradient" })
            {
                Assert.True(GradientColors(document, key).Length >= 2);
            }
        }

        // The backdrop lives in one reusable component mounted once at the window root,
        // so no screen can paint a backdrop of its own and make the light jump.
        var background = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "FocusBackground.axaml"));
        Assert.Contains(
            "Background=\"{DynamicResource FgAppBackgroundGradient}\"",
            background,
            StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource FgAmbientGlow}\"", background, StringComparison.Ordinal);

        var mainWindow = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "MainWindow.axaml"));
        Assert.Contains("x:Name=\"GlobalAmbientBackground\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("FgAppBackgroundGradient", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"{DynamicResource FgSidebarGradient}\"", mainWindow, StringComparison.Ordinal);
    }

    private static HashSet<string> ThemeKeys(XDocument document) =>
        document
            .Root!
            .Elements()
            .Select(element => (string?)element.Attribute(X + "Key"))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Rough relative luminance, enough to tell a light palette from a dark one.</summary>
    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..];
        }

        var red = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = int.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255.0;
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
    public void PrimaryActionsAndLinearProgressUseSemanticColors()
    {
        var controls = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "Styles",
                "Controls.axaml"));
        var focusWorkspace = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "FocusWorkspaceView.axaml"));

        Assert.Contains(
            "Background\" Value=\"{DynamicResource FgPrimaryActionGradient}",
            controls,
            StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{DynamicResource ProfileAccentGradient}\"",
            focusWorkspace,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TimerCycleSwitcherUsesOneThemeDrivenSlidingSelectionSurface()
    {
        var workspace = LoadDesktopXaml("FocusWorkspaceView.axaml");
        var track = Assert.Single(
            workspace.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute("Classes") == "segmentedTrack");
        var thumb = Assert.Single(
            track.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute("Classes") == "segmentedThumb");

        Assert.Equal("True", (string?)track.Attribute("ClipToBounds"));
        Assert.Equal("{Binding IsCycleMode}", (string?)thumb.Attribute("Classes.cycles"));
        Assert.Equal("{Binding IsReduceMotionEnabled}", (string?)thumb.Attribute("Classes.reduceMotion"));

        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        var thumbStyle = Assert.Single(
            controls.Descendants(Avalonia + "Style"),
            style => (string?)style.Attribute("Selector") == "Border.segmentedThumb");
        Assert.Contains(
            thumbStyle.Descendants(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Background"
                && (string?)setter.Attribute("Value") == "{DynamicResource FgAccentSurface}");
        Assert.Single(thumbStyle.Descendants(Avalonia + "TransformOperationsTransition"));

        // Half the track's inner width, so the two positions are genuinely equal and the
        // thumb lands flush against either edge instead of near it.
        var tokens = LoadDesktopXaml("Styles", "Tokens.axaml");
        Assert.Equal("320", ResourceValue(tokens, "Double", "FgSegmentedWidth"));
        Assert.Equal("156", ResourceValue(tokens, "Double", "FgSegmentedThumbWidth"));
        Assert.Contains(
            FindStyle(controls, "Border.segmentedThumb.cycles").Descendants(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Value") == "translateX(156px)");
    }

    [Fact]
    public void CycleSelectorAndActiveStatusMatchTheOwnerApprovedComposition()
    {
        var workspace = LoadDesktopXaml("FocusWorkspaceView.axaml");

        // Cycles are a vertical list of cards, one per saved program, so every option is
        // legible at once instead of only whichever one the old shelf had selected.
        var card = Assert.Single(
            workspace.Descendants(Avalonia + "RadioButton"),
            element => (string?)element.Attribute("Classes") == "cycleCard");
        Assert.Equal("{Binding IsSelected, Mode=TwoWay}", (string?)card.Attribute("IsChecked"));
        Assert.Single(card.Descendants(), element => element.Name.LocalName == "CyclePresetIcon");

        Assert.Contains(
            workspace.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute("Command") == "{Binding CreateCyclePresetCommand}");
        Assert.Contains(
            workspace.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute("Command") == "{Binding EditCyclePresetCommand}");
        Assert.Contains(
            workspace.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute("Command") == "{Binding DeleteCyclePresetCommand}");

        // The same proportion bar draws a cycle in the card and in the running session,
        // so a program looks identical wherever it appears.
        var timelines = workspace
            .Descendants()
            .Where(element => element.Name.LocalName == "CyclePhaseTimeline")
            .ToArray();
        Assert.Equal(2, timelines.Length);
        Assert.All(
            timelines,
            timeline =>
            {
                Assert.Equal("{DynamicResource ProfileAccentGradient}", (string?)timeline.Attribute("FocusBrush"));
                Assert.Equal("{DynamicResource FgCycleBreak}", (string?)timeline.Attribute("BreakBrush"));
            });
        var cardTimeline = Assert.Single(card.Descendants(), element => element.Name.LocalName == "CyclePhaseTimeline");
        Assert.Equal("{Binding Configuration}", (string?)cardTimeline.Attribute("Configuration"));
        Assert.Null(cardTimeline.Attribute("CurrentPhase"));

        var activeTimeline = Assert.Single(
            timelines,
            timeline => (string?)timeline.Attribute("CurrentPhase") == "{Binding CurrentPhaseStatus}");
        Assert.Equal("{Binding SelectedCycleConfiguration}", (string?)activeTimeline.Attribute("Configuration"));

        // The running session reports protection as a compact pill. It is a status, not
        // an action, so nothing inside it is pressable.
        var protectionPill = Assert.Single(
            workspace.Descendants(Avalonia + "Border"),
            element => ((string?)element.Attribute("Classes"))?.StartsWith(
                "protectionPill",
                StringComparison.Ordinal) == true);
        Assert.Empty(protectionPill.Descendants(Avalonia + "Button"));
        Assert.Equal("Polite", (string?)protectionPill.Attribute("AutomationProperties.LiveSetting"));

        // The pseudo-button that announced "Focus active" is gone: it was prominent,
        // looked pressable, and did nothing.
        Assert.DoesNotContain(
            workspace.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute("Classes") == "focusActiveBanner");
    }

    // Every modal is the same object: an eyebrow/title/subtitle header, the body, and a
    // separated footer band holding the actions. Before this each dialog invented its
    // own — a centred medallion here, a corner close button there, buttons in the middle
    // of the content — so moving between them felt like moving between applications.
    [Fact]
    public void EveryModalSharesOneHeaderAndFooterComposition()
    {
        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        foreach (var dialog in new[]
                 {
                     "StrictReviewDialog",
                     "NewProfileDialog",
                     "CyclePresetDialog",
                     "SettingsDialog",
                 })
        {
            var modal = Assert.Single(
                mainWindow.Descendants(Avalonia + "Border"),
                element => (string?)element.Attribute(X + "Name") == dialog);

            Assert.Contains(
                modal.Descendants(Avalonia + "TextBlock"),
                element => (string?)element.Attribute("Classes") == "wizardStep");
            Assert.Contains(
                modal.Descendants(Avalonia + "TextBlock"),
                element => ((string?)element.Attribute("Classes"))?.Contains(
                    "wizardTitle",
                    StringComparison.Ordinal) == true);
            Assert.Contains(
                modal.Descendants(Avalonia + "Border"),
                element => (string?)element.Attribute("Classes") == "wizardFooter");
        }

        // The corner close buttons are gone from the two editors: cancelling belongs
        // beside the action it is the alternative to, not in the opposite corner.
        foreach (var dialog in new[] { "NewProfileDialog", "CyclePresetDialog" })
        {
            var modal = Assert.Single(
                mainWindow.Descendants(Avalonia + "Border"),
                element => (string?)element.Attribute(X + "Name") == dialog);
            Assert.DoesNotContain(
                modal.Descendants(Avalonia + "Button"),
                element => ((string?)element.Attribute("Classes"))?.Contains(
                    "iconOnly",
                    StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void StrictModeIsATwoStepWizardThatStatesItsRealLimits()
    {
        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var dialog = Assert.Single(
            mainWindow.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "StrictReviewDialog");

        // Long profiles and app-closure consequences remain reachable in the fixed window.
        var strictScroll = Assert.Single(dialog.Descendants(Avalonia + "ScrollViewer"));
        Assert.Equal("480", (string?)strictScroll.Attribute("MaxHeight"));
        Assert.Single(
            dialog.Descendants(Avalonia + "StackPanel"),
            element => (string?)element.Attribute("IsVisible") == "{Binding IsStrictStepOne}");
        Assert.Single(
            dialog.Descendants(Avalonia + "StackPanel"),
            element => (string?)element.Attribute("IsVisible") == "{Binding IsStrictStepTwo}");

        // Step one hands over the code as an object, with all three ways to keep it.
        Assert.Single(
            dialog.Descendants(Avalonia + "SelectableTextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding RecoveryCodeDisplay}");
        foreach (var handler in new[]
                 {
                     "DownloadRecoveryCodeClick",
                     "PrintRecoveryCodeClick",
                     "CopyRecoveryCodeClick",
                 })
        {
            Assert.Contains(
                dialog.Descendants(Avalonia + "Button"),
                element => (string?)element.Attribute("Click") == handler);
        }

        // Step two leads with the consequence and confirms with the profile's own name.
        Assert.Contains(
            dialog.Descendants(Avalonia + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding StrictLockTitle}"
                && (string?)element.Attribute("Classes") == "wizardTitle");
        Assert.Single(
            dialog.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute("Classes") == "confirmToken");
        var confirm = Assert.Single(
            dialog.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute("Command") == "{Binding ConfirmStrictCommand}");
        Assert.Equal("{Binding CanConfirmStrict}", (string?)confirm.Attribute("IsEnabled"));
        Assert.Equal("{Binding StrictConfirmButtonText}", (string?)confirm.Attribute("Content"));
        Assert.Equal("danger modalPrimary", (string?)confirm.Attribute("Classes"));

        // Honesty about the threat model is part of the screen, not buried: the limit
        // is stated inline and the full list is one click away.
        Assert.Contains(
            dialog.Descendants(Avalonia + "TextBlock"),
            element => (string?)element.Attribute("Text")
                == "{x:Static resources:UiStrings.StrictAdminLimit}");
        Assert.Contains(
            dialog.Descendants(Avalonia + "Button"),
            element => (string?)element.Attribute("Click") == "ShowStrictLimitsClick");

        // Both warning banners wrap. A plain TextBlock inside Border.warningBanner lays
        // out every line and paints only the first, which silently truncated a safety
        // warning mid-sentence; SelectableTextBlock renders the same layout correctly.
        // The banners must therefore never be "simplified" back to TextBlock.
        foreach (var warning in new[]
                 {
                     "{Binding StrictRecoveryWarning}",
                     "{Binding StrictAppClosureWarning}",
                 })
        {
            var banner = Assert.Single(
                dialog.Descendants(Avalonia + "SelectableTextBlock"),
                element => (string?)element.Attribute("Text") == warning);
            Assert.Equal("Wrap", (string?)banner.Attribute("TextWrapping"));
        }

        Assert.DoesNotContain(
            dialog.Descendants(Avalonia + "TextBlock"),
            element => ((string?)element.Attribute("Classes"))?.Contains(
                "warningText",
                StringComparison.Ordinal) == true);
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

        // One button scale for the whole app. Variants may repaint a button; none of
        // them may resize it, so a modal's Cancel and its primary action are the same
        // object in two colours instead of two different heights side by side.
        var tokens = LoadDesktopXaml("Styles", "Tokens.axaml");
        Assert.Equal("38", ResourceValue(tokens, "Double", "FgButtonHeight"));
        AssertStyleSetter(
            document,
            "Button.compact",
            "Padding",
            "{DynamicResource FgButtonPaddingCompact}");
        foreach (var variant in new[]
                 {
                     "Button.primary",
                     "Button.outline",
                     "Button.compact",
                     "Button.outline.compact",
                     "Button.startAction",
                     "Button.modalPrimary",
                     "Button.danger",
                 })
        {
            var style = FindStyle(document, variant);
            Assert.DoesNotContain(
                style.Descendants(Avalonia + "Setter"),
                setter => (string?)setter.Attribute("Property") is "MinHeight" or "Height");
        }
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
            "{DynamicResource ProfileAccentGradient}");
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
        AssertStyleSetter(
            controls,
            "Border.ringGlowOuter",
            "BorderBrush",
            "{DynamicResource ProfileAccentGradient}");
        var breathingOuter = FindStyle(controls, "Border.ringGlowOuter.pulsing");
        Assert.Contains(
            breathingOuter.Descendants(Avalonia + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "Opacity"
                && (string?)setter.Attribute("Value") == "0.32");

        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var focusWorkspace = LoadDesktopXaml("FocusWorkspaceView.axaml");
        Assert.Single(
            mainWindow.Descendants().Where(element => element.Name.LocalName == "FocusWorkspaceView"),
            element => (string?)element.Attribute("IsVisible") == "{Binding IsFocusVisible}");
        // Strict mode is the only switch the Focus screen offers. Whether enforcement
        // runs at all belongs to Settings, where it is a property of the install rather
        // than a decision to re-make before each session.
        var workspaceToggle = Assert.Single(
            focusWorkspace.Descendants(Avalonia + "ToggleSwitch"),
            element => (string?)element.Attribute("Theme") == "{StaticResource FgToggleSwitch}");
        Assert.Equal("{Binding IsStrictMode}", (string?)workspaceToggle.Attribute("IsChecked"));
        Assert.Contains(
            mainWindow.Descendants(Avalonia + "ToggleSwitch"),
            element => (string?)element.Attribute("IsChecked") == "{Binding IsEnforcementEnabled}");
        // The strict review's two acknowledgement checkboxes are gone. A box that only
        // records a claim verifies nothing; passing the recovery code before the
        // consequences are shown is the deliberate act that replaced them.
        Assert.Empty(mainWindow.Descendants(Avalonia + "CheckBox"));

        var settingsTabs = Assert.Single(
            mainWindow.Descendants(Avalonia + "TabControl"),
            element => (string?)element.Attribute(X + "Name") == "SettingsTabs");
        Assert.Equal(6, settingsTabs.Elements(Avalonia + "TabItem").Count());
        var settingsDialog = Assert.Single(
            settingsTabs.Ancestors(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "SettingsDialog");
        Assert.Equal("Cycle", (string?)settingsDialog.Attribute("KeyboardNavigation.TabNavigation"));

        // Navigation is a persistent column, not a floating header the content scrolls
        // beneath: the header and its pills are gone entirely.
        Assert.DoesNotContain(
            mainWindow.Descendants(Avalonia + "Button"),
            element => ((string?)element.Attribute("Classes"))?.Contains(
                "topNav",
                StringComparison.Ordinal) == true);
        var sidebar = Assert.Single(
            mainWindow.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "NavigationSidebar");
        Assert.Equal("navSidebar", (string?)sidebar.Attribute("Classes"));

        // Profiles live in the home library, leaving the sidebar for destinations.
        Assert.DoesNotContain(sidebar.Descendants(Avalonia + "ItemsControl"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding ProfileSwatches}");
        Assert.Single(focusWorkspace.Descendants(Avalonia + "ItemsControl"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding HomeProfiles}" &&
            (string?)element.Attribute("IsEnabled") == "{Binding CanNavigateToEditors}");

        // Quick start and advanced options scroll above a fixed primary action.
        var configuration = Assert.Single(focusWorkspace.Descendants(Avalonia + "Grid"),
            element => (string?)element.Attribute("IsVisible") == "{Binding IsInactive}");
        Assert.Equal("*,Auto", (string?)configuration.Attribute("RowDefinitions"));
        var scrollRegion = Assert.Single(configuration.Elements(Avalonia + "ScrollViewer"));
        Assert.Single(scrollRegion.Descendants(Avalonia + "Expander"),
            element => (string?)element.Attribute("Header") == "{x:Static resources:UiStrings.AdvancedOptions}");
        Assert.Single(scrollRegion.Descendants(Avalonia + "ItemsControl"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding CyclePresetCards}");
        Assert.Single(configuration.Elements(Avalonia + "StackPanel"),
            element => (string?)element.Attribute("Grid.Row") == "1"
                && element.Descendants(Avalonia + "Button").Any(button =>
                    ((string?)button.Attribute("Classes"))?.Contains("setupStartAction", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void StoppingARunningSessionIsAlwaysOnScreenAndReadsAsDestructive()
    {
        var workspace = LoadDesktopXaml("FocusWorkspaceView.axaml");
        var session = Assert.Single(workspace.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute("Classes") == "sessionRow activeSessionRow");
        var stop = Assert.Single(session.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding StopFocusCommand}");
        Assert.Equal("{Binding CanStop}", (string?)stop.Attribute("IsVisible"));
        Assert.Contains("danger", (string?)stop.Attribute("Classes") ?? string.Empty);
    }

    [Fact]
    public void GeistTypographyAndTimerSpacingMatchTheOwnerRevision()
    {
        var controls = LoadDesktopXaml("Styles", "Controls.axaml");
        AssertStyleSetter(
            controls,
            "Window",
            "FontFamily",
            "avares://FocusGuard.Desktop/Assets/Fonts#Geist");
        AssertStyleSetter(controls, "TextBlock.profileName", "FontWeight", "Bold");

        var tokens = LoadDesktopXaml("Styles", "Tokens.axaml");
        Assert.Equal("31.25", ResourceValue(tokens, "Double", "FgFontProfileName"));
        Assert.Equal("157", ResourceValue(tokens, "Double", "FgTimerCopyWidth"));
        Assert.Equal("43.5", ResourceValue(tokens, "Double", "FgTimerValueFontSize"));
        Assert.Equal("14", ResourceValue(tokens, "Double", "FgTimerCaptionFontSize"));
        Assert.Equal("288", ResourceValue(tokens, "Double", "FgActiveRingDiameter"));

        var mainWindow = LoadDesktopXaml("FocusWorkspaceView.axaml");

        Assert.DoesNotContain(mainWindow.Descendants(), element => element.Name.LocalName == "ClockDial");
        var progress = Assert.Single(mainWindow.Descendants(Avalonia + "ProgressBar"));
        Assert.Equal("{Binding RingSweepAngle}", (string?)progress.Attribute("Value"));
        Assert.Equal("360", (string?)progress.Attribute("Maximum"));
        Assert.Single(mainWindow.Descendants(Avalonia + "StackPanel"), element => (string?)element.Attribute(X + "Name") == "RingDigitsPanel");
        var shell = LoadDesktopXaml("MainWindow.axaml").Root!;
        Assert.Equal("False", (string?)shell.Attribute("CanResize"));
        Assert.Equal("1440", (string?)shell.Attribute("Width"));
        Assert.Equal("840", (string?)shell.Attribute("Height"));
        var small = FocusGuard.Desktop.DesktopWindowLayout.ClientSize(new Avalonia.PixelRect(0, 0, 1366, 720), 1.25);
        Assert.Equal(new Avalonia.Size(1044, 528), small);
        Assert.True(small.Width * 1.25 + 16 <= 1366);
        Assert.True(small.Height * 1.25 + 40 + 48 <= 768);

        var project = XDocument.Load(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "FocusGuard.Desktop.csproj"));
        Assert.Contains(
            project.Descendants("AvaloniaResource"),
            element =>
                (string?)element.Attribute("Include")
                == "../../assets/branding/fonts/Geist-*.ttf");
    }

    [Fact]
    public void TrayFlyoutUsesProfileAccentForTimeAndIcons()
    {
        var flyout = LoadDesktopXaml("TrayFlyoutWindow.axaml");
        var source = flyout.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("Text=\"{Binding RemainingText}\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{Binding ProfileAccentBrush}\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            flyout.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Click") == "SettingsClick");
        Assert.Equal(
            "False",
            (string?)flyout.Root?.Attribute("ShowInTaskbar"));
    }

    [Fact]
    public void ReactPilotLaunchIsExposedBesideClassicCompactModeWithoutAddingATrayOwner()
    {
        var flyout = LoadDesktopXaml("TrayFlyoutWindow.axaml");
        var pilotButton = Assert.Single(flyout.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Click") == "ReactPilotClick");
        Assert.Contains("AutomationProperties.Name=\"{x:Static resources:UiStrings.ReactPilotOpen}\"",
            pilotButton.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var app = LoadDesktopXaml("App.axaml");
        Assert.Single(app.Descendants(), element => element.Name.LocalName == "TrayIcon");
        Assert.Single(app.Descendants(), element => (string?)element.Attribute("Click") == "ReactPilotClick");
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
    public void FocusAndModalViewsUseSemanticColorResourcesOnly()
    {
        foreach (var relativePath in new[]
        {
            "MainWindow.axaml",
            "FocusWorkspaceView.axaml",
            Path.Combine("Styles", "Controls.axaml")
        })
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                relativePath));
            Assert.DoesNotMatch(HardCodedColor(), source);
        }
    }

    [Fact]
    public void SettingsIsCenteredTabbedAndTheShellHasNoPermanentSidebar()
    {
        var mainWindow = LoadDesktopXaml("MainWindow.axaml");
        var settingsDialog = Assert.Single(
            mainWindow.Descendants(Avalonia + "Border"),
            element => (string?)element.Attribute(X + "Name") == "SettingsDialog");
        Assert.Equal("Center", (string?)settingsDialog.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)settingsDialog.Attribute("VerticalAlignment"));

        var settingsTabs = Assert.Single(
            settingsDialog.Descendants(Avalonia + "TabControl"),
            element => (string?)element.Attribute(X + "Name") == "SettingsTabs");
        Assert.Equal(6, settingsTabs.Elements(Avalonia + "TabItem").Count());

        var source = mainWindow.ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("FgSidebarGradient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarPointerPressed", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("GlobalAmbientBackground", StringSplitOptions.None).Length - 1);
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
                            == "{DynamicResource FgAccent}"));
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

    private static string ResourceValue(XDocument document, string elementName, string key) =>
        Assert.Single(
                document.Descendants(X + elementName),
                element => (string?)element.Attribute(X + "Key") == key)
            .Value
            .Trim();

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

    [GeneratedRegex("""#[0-9A-Fa-f]{3,8}""", RegexOptions.CultureInvariant)]
    private static partial Regex HardCodedColor();
}
