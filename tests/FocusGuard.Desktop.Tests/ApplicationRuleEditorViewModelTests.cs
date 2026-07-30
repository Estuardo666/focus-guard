using FocusGuard.Desktop;
using FocusGuard.Domain;

namespace FocusGuard.Desktop.Tests;

public sealed class ApplicationRuleEditorViewModelTests
{
    [Fact]
    public void AddRule_UsesDomainValidationAndNormalizesValue()
    {
        var viewModel = new ApplicationRuleEditorViewModel { ValueInput = "  C:\\Apps\\Focus.exe  " };

        viewModel.AddRuleCommand.Execute(null);

        Assert.Single(viewModel.Rules);
        Assert.Equal(@"C:\Apps\Focus.exe", viewModel.Rules[0].Rule.Value);
        Assert.Contains("added", viewModel.ValidationMessage);
    }

    [Fact]
    public void AddRule_ReportsValidationWithoutAddingInvalidInput()
    {
        var viewModel = new ApplicationRuleEditorViewModel { ValueInput = "   " };

        viewModel.AddRuleCommand.Execute(null);

        Assert.Empty(viewModel.Rules);
        Assert.Contains("required", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveRule_RemovesOnlyTheSelectedDraftRule()
    {
        var viewModel = new ApplicationRuleEditorViewModel { ValueInput = "first.exe" };
        viewModel.AddRuleCommand.Execute(null);
        viewModel.ValueInput = "second.exe";
        viewModel.AddRuleCommand.Execute(null);

        viewModel.Rules[0].RemoveCommand.Execute(null);

        Assert.Single(viewModel.Rules);
        Assert.Equal("second.exe", viewModel.Rules[0].Rule.Value);
    }
}
