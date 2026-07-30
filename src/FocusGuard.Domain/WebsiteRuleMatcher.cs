using System.Text.RegularExpressions;

namespace FocusGuard.Domain;

public static class WebsiteRuleMatcher
{
    public static RuleAction Evaluate(IEnumerable<WebsiteRule> rules, string url)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var normalizedUrl = NormalizeUrl(url);
        if (IsBrowserInternal(normalizedUrl))
        {
            return RuleAction.Allow;
        }

        var matchingRules = rules
            .Where(rule => Matches(rule.Pattern, normalizedUrl))
            .ToArray();

        return matchingRules.Any(rule => rule.Action == RuleAction.Allow)
            ? RuleAction.Allow
            : matchingRules.Any(rule => rule.Action == RuleAction.Block)
                ? RuleAction.Block
                : RuleAction.Allow;
    }

    public static bool Matches(string pattern, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var normalizedPattern = NormalizePattern(pattern);
        var normalizedUrl = NormalizeUrl(url);
        var regexPattern = CreateRegexPattern(normalizedPattern);
        return Regex.IsMatch(normalizedUrl, regexPattern, RegexOptions.CultureInvariant);
    }

    private static string CreateRegexPattern(string normalizedPattern)
    {
        var schemeEnd = normalizedPattern.IndexOf("://", StringComparison.Ordinal);
        var hostStart = schemeEnd < 0 ? 0 : schemeEnd + 3;
        var pathStart = normalizedPattern.IndexOf('/', hostStart);
        var hostEnd = pathStart < 0 ? normalizedPattern.Length : pathStart;
        var host = normalizedPattern[hostStart..hostEnd];
        var optionalWww = !host.Contains('*', StringComparison.Ordinal)
            && !host.StartsWith("www.", StringComparison.Ordinal);

        if (!optionalWww)
        {
            return "\\A" + EscapeGlob(normalizedPattern) + "\\z";
        }

        return "\\A"
            + EscapeGlob(normalizedPattern[..hostStart])
            + "(?:www\\.)?"
            + EscapeGlob(host)
            + EscapeGlob(normalizedPattern[hostEnd..])
            + "\\z";
    }

    private static string EscapeGlob(string value) =>
        Regex.Escape(value).Replace("\\*", ".*", StringComparison.Ordinal);

    private static string NormalizePattern(string pattern)
    {
        var normalized = pattern.Trim().ToLowerInvariant();
        if (!normalized.Contains("://", StringComparison.Ordinal))
        {
            normalized = $"*://{normalized.TrimEnd('/')}{(normalized.Contains('/', StringComparison.Ordinal) ? string.Empty : "/*")}";
        }

        return normalized.TrimEnd('/');
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        var queryStart = trimmed.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            trimmed = trimmed[..queryStart];
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool IsBrowserInternal(string url) =>
        url.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("edge:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("edge-extension:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("devtools:", StringComparison.OrdinalIgnoreCase);
}
