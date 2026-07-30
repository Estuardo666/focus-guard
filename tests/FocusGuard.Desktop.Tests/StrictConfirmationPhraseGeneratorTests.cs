using System.Xml.Linq;

namespace FocusGuard.Desktop.Tests;

public sealed class StrictConfirmationPhraseGeneratorTests
{
    [Fact]
    public void GeneratorCombinesExactlyOneWordFromEachReviewedDictionary()
    {
        var generator = new StrictConfirmationPhraseGenerator(maximum => maximum - 1);

        var phrase = generator.Generate();
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["Zen", "Trust"], words);
        Assert.Equal(2, words.Length);
        Assert.Contains(words[0], StrictConfirmationPhraseGenerator.FirstWords);
        Assert.Contains(words[1], StrictConfirmationPhraseGenerator.SecondWords);
        Assert.All(
            StrictConfirmationPhraseGenerator.FirstWords
                .Concat(StrictConfirmationPhraseGenerator.SecondWords),
            word => Assert.DoesNotContain(' ', word));
    }

    [Fact]
    public void MainWindowHostsStrictReviewAsBlurredOverlayInsteadOfInlineCard()
    {
        var document = XDocument.Load(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "MainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootGrid = Assert.Single(document.Root!.Elements(avalonia + "Grid"));
        var shell = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => (string?)element.Attribute(x + "Name") == "ApplicationShell");
        var overlay = Assert.Single(
            rootGrid.Elements(avalonia + "Border"),
            element => (string?)element.Attribute(x + "Name") == "StrictReviewOverlay");

        Assert.NotNull(shell.Descendants(avalonia + "BlurEffect").SingleOrDefault());
        Assert.Contains(
            overlay.Descendants(avalonia + "DoubleTransition"),
            element => (string?)element.Attribute("Property") == "Opacity");
        Assert.DoesNotContain(
            shell.Descendants(),
            element => ((string?)element.Attribute("IsVisible"))?.Contains(
                "IsStrictReviewVisible",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void MainWindowHostsProfileCreationAsModalOverlayInsteadOfInlineScreen()
    {
        var document = XDocument.Load(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FocusGuard.Desktop",
                "MainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootGrid = Assert.Single(document.Root!.Elements(avalonia + "Grid"));
        var shell = Assert.Single(
            rootGrid.Elements(avalonia + "Grid"),
            element => (string?)element.Attribute(x + "Name") == "ApplicationShell");
        var overlay = Assert.Single(
            rootGrid.Elements(avalonia + "Border"),
            element => (string?)element.Attribute(x + "Name") == "NewProfileOverlay");

        Assert.Equal("{Binding IsCreatingProfile}", (string?)overlay.Attribute("IsVisible"));
        Assert.Equal("{Binding IsCreatingProfile}", (string?)overlay.Attribute("IsHitTestVisible"));
        Assert.Equal("NewProfileKeyDown", (string?)overlay.Attribute("KeyDown"));
        Assert.Contains(
            overlay.Descendants(avalonia + "TextBlock"),
            element => ((string?)element.Attribute("Text"))?.Contains("NewProfileTitle", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            shell.Descendants(avalonia + "Border"),
            element => (string?)element.Attribute(x + "Name") == "NewProfileOverlay");
    }

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
