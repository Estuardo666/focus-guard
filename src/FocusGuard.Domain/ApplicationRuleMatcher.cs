namespace FocusGuard.Domain;

public sealed record ApplicationCandidate(string ExecutablePath, string? Sha256 = null);

public static class ApplicationRuleMatcher
{
    public static bool Matches(ApplicationRule rule, ApplicationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);

        var candidatePath = NormalizePath(candidate.ExecutablePath);
        var ruleValue = NormalizePath(rule.Value);

        return rule.MatchKind switch
        {
            ApplicationMatchKind.ExactPath => string.Equals(candidatePath, ruleValue, StringComparison.OrdinalIgnoreCase),
            ApplicationMatchKind.Folder => IsWithinFolder(candidatePath, ruleValue),
            ApplicationMatchKind.FileName => string.Equals(GetFileName(candidatePath), rule.Value.Trim(), StringComparison.OrdinalIgnoreCase),
            ApplicationMatchKind.Sha256 => string.Equals(candidate.Sha256?.Trim(), rule.Value.Trim(), StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsWithinFolder(string candidatePath, string folder)
    {
        var normalizedFolder = folder.TrimEnd('/');
        return candidatePath.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string value) =>
        value.Trim().Replace('\\', '/').TrimEnd('/');

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}
