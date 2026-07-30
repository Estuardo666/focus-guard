namespace FocusGuard.Domain;

public sealed record ProfileId(Guid Value)
{
    public static ProfileId New() => new(Guid.NewGuid());
}

public sealed record Profile
{
    private Profile(ProfileId id, string name, string? description, string? accentHex)
    {
        Id = id;
        Name = name;
        Description = description;
        AccentHex = accentHex;
    }

    public ProfileId Id { get; }

    public string Name { get; }

    /// <summary>
    /// Optional one-line summary the header shows under the profile name. When it is
    /// absent the header falls back to a generated rule count, so this stays nullable.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Per-profile accent as <c>#RRGGBB</c>. Null means "use the app default", which
    /// is what every profile created before accents existed reports.
    /// </summary>
    public string? AccentHex { get; }

    public static ValidationResult<Profile> Create(
        string? name,
        string? description = null,
        string? accentHex = null)
    {
        var normalized = name?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ValidationResult<Profile>.Failure(
                new ValidationError("profile.name.required", "Profile name is required."));
        }

        if (normalized.Length > 80)
        {
            return ValidationResult<Profile>.Failure(
                new ValidationError("profile.name.too_long", "Profile name must be 80 characters or fewer."));
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > 160 })
        {
            return ValidationResult<Profile>.Failure(
                new ValidationError(
                    "profile.description.too_long",
                    "Profile description must be 160 characters or fewer."));
        }

        var normalizedAccent = string.IsNullOrWhiteSpace(accentHex) ? null : accentHex.Trim();
        if (normalizedAccent is not null && !IsAccentHex(normalizedAccent))
        {
            return ValidationResult<Profile>.Failure(
                new ValidationError("profile.accent.invalid", "Profile accent must be #RRGGBB."));
        }

        return ValidationResult<Profile>.Success(
            new Profile(ProfileId.New(), normalized, normalizedDescription, normalizedAccent?.ToUpperInvariant()));
    }

    /// <summary>Accepts only <c>#RRGGBB</c>: the accent is fed straight to a brush parser.</summary>
    public static bool IsAccentHex(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
