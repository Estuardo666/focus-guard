namespace FocusGuard.Domain;

public enum RuleAction
{
    Allow,
    Block
}

public enum ApplicationMatchKind
{
    ExactPath,
    Folder,
    Sha256,
    FileName
}

public sealed record ApplicationRule(
    Guid Id,
    RuleAction Action,
    ApplicationMatchKind MatchKind,
    string Value)
{
    public static ValidationResult<ApplicationRule> Create(
        RuleAction action,
        ApplicationMatchKind matchKind,
        string? value)
    {
        var normalized = value?.Trim();
        if (action is not RuleAction.Allow and not RuleAction.Block)
        {
            return ValidationResult<ApplicationRule>.Failure(
                new ValidationError("application_rule.action.invalid", "Application rule action is invalid."));
        }

        if (matchKind is not ApplicationMatchKind.ExactPath
            and not ApplicationMatchKind.Folder
            and not ApplicationMatchKind.Sha256
            and not ApplicationMatchKind.FileName)
        {
            return ValidationResult<ApplicationRule>.Failure(
                new ValidationError("application_rule.match_kind.invalid", "Application match kind is invalid."));
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ValidationResult<ApplicationRule>.Failure(
                new ValidationError("application_rule.value.required", "Application rule value is required."));
        }

        return ValidationResult<ApplicationRule>.Success(
            new ApplicationRule(Guid.NewGuid(), action, matchKind, normalized));
    }
}

public sealed record WebsiteRule(
    Guid Id,
    RuleAction Action,
    string Pattern)
{
    public static ValidationResult<WebsiteRule> Create(RuleAction action, string? pattern)
    {
        var normalized = pattern?.Trim().ToLowerInvariant();
        if (action is not RuleAction.Allow and not RuleAction.Block)
        {
            return ValidationResult<WebsiteRule>.Failure(
                new ValidationError("website_rule.action.invalid", "Website rule action is invalid."));
        }

        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('?', StringComparison.Ordinal))
        {
            return ValidationResult<WebsiteRule>.Failure(
                new ValidationError("website_rule.pattern.invalid", "Website rule pattern must be non-empty and exclude query parameters."));
        }

        return ValidationResult<WebsiteRule>.Success(
            new WebsiteRule(Guid.NewGuid(), action, normalized));
    }
}

public sealed record EnforcementPolicy(
    Guid SessionId,
    ProfileId ProfileId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsStrict,
    IReadOnlyList<ApplicationRule> ApplicationRules,
    IReadOnlyList<WebsiteRule> WebsiteRules,
    bool EnforcementEnabled = false)
{
    public static EnforcementPolicy Create(
        Session session,
        IEnumerable<ApplicationRule> applicationRules,
        IEnumerable<WebsiteRule> websiteRules,
        bool enforcementEnabled = false) =>
        new(
            session.Id,
            session.ProfileId,
            session.StartUtc,
            session.EndUtc,
            session.StrictMode.Enabled,
            Array.AsReadOnly(applicationRules.ToArray()),
            Array.AsReadOnly(websiteRules.ToArray()),
            enforcementEnabled);
}
