using FocusGuard.Domain;

namespace FocusGuard.Domain.Tests;

public sealed class ApplicationRuleMatcherTests
{
    [Fact]
    public void ExactPath_IsCaseInsensitiveAndNormalizesSeparators()
    {
        var rule = new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, @"C:\Apps\Focus.exe");
        var candidate = new ApplicationCandidate("c:/apps/focus.exe");

        Assert.True(ApplicationRuleMatcher.Matches(rule, candidate));
    }

    [Fact]
    public void Folder_DoesNotMatchSiblingPrefix()
    {
        var rule = new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.Folder, @"C:\Apps");

        Assert.True(ApplicationRuleMatcher.Matches(rule, new ApplicationCandidate(@"C:\Apps\Focus.exe")));
        Assert.False(ApplicationRuleMatcher.Matches(rule, new ApplicationCandidate(@"C:\AppsBackup\Focus.exe")));
    }

    [Fact]
    public void Sha256_UsesSuppliedSnapshotHash()
    {
        var rule = new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.Sha256, "ABC123");

        Assert.True(ApplicationRuleMatcher.Matches(rule, new ApplicationCandidate("ignored.exe", " abc123 ")));
        Assert.False(ApplicationRuleMatcher.Matches(rule, new ApplicationCandidate("ignored.exe", "different")));
    }
}
