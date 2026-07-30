using FocusGuard.Desktop;
using FocusGuard.Domain;

namespace FocusGuard.Desktop.Tests;

public sealed class WebsiteRuleEditorViewModelTests
{
    [Fact]
    public void AddRule_NormalizesPatternAndKeepsQueryParametersOutOfDrafts()
    {
        var viewModel = new WebsiteRuleEditorViewModel { PatternInput = "  Example.COM/path/* " };

        viewModel.AddRuleCommand.Execute(null);

        Assert.Single(viewModel.Rules);
        Assert.Equal("example.com/path/*", viewModel.Rules[0].Rule.Pattern);
        Assert.Contains("excluded", viewModel.ValidationMessage);
    }

    [Fact]
    public void AddRule_RejectsEmptyAndQueryOnlyPatterns()
    {
        var viewModel = new WebsiteRuleEditorViewModel { PatternInput = "   " };
        viewModel.AddRuleCommand.Execute(null);
        Assert.Empty(viewModel.Rules);

        viewModel.PatternInput = "example.com/?tracking=1";
        viewModel.AddRuleCommand.Execute(null);
        Assert.Empty(viewModel.Rules);
        Assert.Contains("must be non-empty", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveRule_RemovesSelectedDraftRule()
    {
        var viewModel = new WebsiteRuleEditorViewModel { PatternInput = "example.com" };
        viewModel.AddRuleCommand.Execute(null);
        viewModel.PatternInput = "docs.example.com";
        viewModel.AddRuleCommand.Execute(null);

        viewModel.Rules[0].RemoveCommand.Execute(null);

        Assert.Single(viewModel.Rules);
        Assert.Equal("docs.example.com", viewModel.Rules[0].Rule.Pattern);
    }
}
