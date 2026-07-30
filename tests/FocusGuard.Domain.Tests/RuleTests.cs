using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class RuleTests
{
    [Fact]
    public void CreateApplicationRule_TrimsValue()
    {
        var result = ApplicationRule.Create(RuleAction.Block, ApplicationMatchKind.FileName, "  distraction.exe ");

        Assert.True(result.IsValid);
        Assert.Equal("distraction.exe", result.Value!.Value);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public void CreateWebsiteRule_NormalizesPatternAndRejectsQueryParameters()
    {
        var valid = WebsiteRule.Create(RuleAction.Block, " Example.COM/path/* ");
        var invalid = WebsiteRule.Create(RuleAction.Block, "example.com/?tracking=1");

        Assert.Equal("example.com/path/*", valid.Value!.Pattern);
        Assert.Contains(invalid.Errors, error => error.Code == "website_rule.pattern.invalid");
    }
}
