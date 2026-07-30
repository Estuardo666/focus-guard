using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class WebsiteRuleMatcherTests
{
    [Fact]
    public void Evaluate_MatchesBareDomainAndSubdomainPatterns()
    {
        var rules = new[]
        {
            new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "example.com"),
            new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "*.social.example")
        };

        Assert.Equal(RuleAction.Block, WebsiteRuleMatcher.Evaluate(rules, "https://example.com/path?tracking=1"));
        Assert.Equal(RuleAction.Block, WebsiteRuleMatcher.Evaluate(rules, "https://news.social.example/feed"));
        Assert.Equal(RuleAction.Allow, WebsiteRuleMatcher.Evaluate(rules, "https://other.example.net"));
    }

    [Fact]
    public void Evaluate_AllowsExplicitExceptionAndBrowserInternalPages()
    {
        var rules = new[]
        {
            new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "example.com"),
            new WebsiteRule(Guid.NewGuid(), RuleAction.Allow, "example.com/work/*")
        };

        Assert.Equal(RuleAction.Allow, WebsiteRuleMatcher.Evaluate(rules, "https://example.com/work/today"));
        Assert.Equal(RuleAction.Allow, WebsiteRuleMatcher.Evaluate(rules, "chrome://settings"));
    }

    [Fact]
    public void Matches_SupportsExplicitSchemeWildcardPattern()
    {
        Assert.True(WebsiteRuleMatcher.Matches("*://*.example.com/*", "http://docs.example.com/guide"));
        Assert.False(WebsiteRuleMatcher.Matches("*://*.example.com/*", "https://example.net/guide"));
    }

    [Theory]
    [InlineData("facebook.com", "https://www.facebook.com/")]
    [InlineData("instagram.com", "https://www.instagram.com/accounts/login/")]
    [InlineData("example.com/work/*", "https://www.example.com/work/today")]
    public void Matches_BareDomainAlsoMatchesCanonicalWwwHost(string pattern, string url)
    {
        Assert.True(WebsiteRuleMatcher.Matches(pattern, url));
    }

    [Fact]
    public void Matches_BareDomainDoesNotMatchUnrelatedSubdomains()
    {
        Assert.False(WebsiteRuleMatcher.Matches("example.com", "https://news.example.com/"));
    }
}
