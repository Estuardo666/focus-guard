using System.Globalization;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

/// <summary>User-local profile editing; never modifies an active service policy.</summary>
public sealed class ProfileWorkflow(IProfileStore store) : IProfileWorkflow
{
    public async Task<ValidationResult<StoredProfile>> CreateAsync(
        string name, IReadOnlyList<ApplicationRule> applicationRules,
        IReadOnlyList<WebsiteRule> websiteRules, string? accentHex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = Profile.Create(name);
        if (!validated.IsValid) return ValidationResult<StoredProfile>.Failure([.. validated.Errors]);
        var profile = new StoredProfile(validated.Value!.Id, validated.Value.Name,
            applicationRules.ToArray(), websiteRules.ToArray(), AccentHex: accentHex);
        var saved = await SaveReviewedAsync(null, profile, cancellationToken).ConfigureAwait(false);
        return ValidationResult<StoredProfile>.Success(saved);
    }

    public async Task<ValidationResult<StoredProfile>> RenameAsync(
        StoredProfile profile, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = Profile.ValidateName(name);
        if (!validated.IsValid) return ValidationResult<StoredProfile>.Failure([.. validated.Errors]);
        var renamed = profile with { Name = validated.Value! };
        var saved = await SaveReviewedAsync(profile, renamed, cancellationToken).ConfigureAwait(false);
        return ValidationResult<StoredProfile>.Success(saved);
    }

    public async Task<ValidationResult<StoredProfile>> SetAccentAsync(
        StoredProfile profile, string? accentHex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accent = string.IsNullOrWhiteSpace(accentHex) ? null : accentHex.Trim().ToUpperInvariant();
        if (accent is not null && !Profile.IsAccentHex(accent))
            return ValidationResult<StoredProfile>.Failure(new ValidationError("profile.accent.invalid", "Profile accent must be #RRGGBB."));
        return ValidationResult<StoredProfile>.Success(await SaveReviewedAsync(profile,
            profile with { AccentHex = accent }, cancellationToken).ConfigureAwait(false));
    }

    public async Task<bool> DeleteReviewedAsync(StoredProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store is IRevisionedProfileStore atomic)
        {
            var result = await atomic.TryDeleteProfileAsync(profile.Id, ProfileRevision.Compute(profile), cancellationToken).ConfigureAwait(false);
            if (result.Status != ProfileMutationStatus.Saved) throw new ProfileWriteConflictException(result.Status);
            return true;
        }
        return await store.DeleteProfileAsync(profile.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredProfile> DuplicateAsync(
        StoredProfile profile, string copyNameFormat, string numberSuffixFormat,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store is IRevisionedProfileStore atomic)
        {
            var result = await atomic.TryDuplicateProfileAsync(profile.Id, ProfileRevision.Compute(profile),
                copyNameFormat, numberSuffixFormat, cancellationToken).ConfigureAwait(false);
            if (result.Status != ProfileMutationStatus.Saved) throw new ProfileWriteConflictException(result.Status);
            return result.Profile!;
        }
        var profiles = await store.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        var duplicateId = ProfileId.New();
        var duplicate = new StoredProfile(duplicateId,
            UniqueCopyName(profile.Name, profiles, copyNameFormat, numberSuffixFormat),
            profile.ApplicationRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray(),
            profile.WebsiteRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray(),
            profile.Description, profile.AccentHex);
        await store.SaveProfileAsync(duplicate, cancellationToken).ConfigureAwait(false);
        var cycle = await store.LoadProfileCycleSettingsAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (cycle is not null)
        {
            await store.SaveProfileCycleSettingsAsync(cycle with
            {
                ProfileId = duplicateId,
                ApplicationRules = cycle.ApplicationRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray(),
                WebsiteRules = cycle.WebsiteRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray()
            }, cancellationToken).ConfigureAwait(false);
        }
        return duplicate;
    }

    public static bool HasNameConflict(IEnumerable<StoredProfile> profiles, string name, ProfileId? except = null) =>
        profiles.Any(profile => profile.Id != except
            && string.Equals(profile.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private static string UniqueCopyName(string sourceName, IEnumerable<StoredProfile> profiles,
        string copyNameFormat, string numberSuffixFormat)
    {
        const int maximumLength = 80;
        var baseName = string.Format(CultureInfo.CurrentCulture, copyNameFormat, sourceName);
        var names = profiles.Select(profile => profile.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var candidate = baseName[..Math.Min(baseName.Length, maximumLength)].TrimEnd();
        if (!names.Contains(candidate)) return candidate;
        for (var number = 2; ; number++)
        {
            var suffix = string.Format(CultureInfo.CurrentCulture, numberSuffixFormat, number);
            var prefixLength = Math.Max(1, maximumLength - suffix.Length);
            candidate = $"{baseName[..Math.Min(baseName.Length, prefixLength)].TrimEnd()}{suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    public async Task<ValidationResult<StoredProfile>> AddApplicationRuleAsync(
        StoredProfile profile, RuleAction action, ApplicationMatchKind matchKind, string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rule = ApplicationRule.Create(action, matchKind, value);
        if (!rule.IsValid) return ValidationResult<StoredProfile>.Failure([.. rule.Errors]);
        var updated = profile with { ApplicationRules = [.. profile.ApplicationRules, rule.Value!] };
        var saved = await SaveReviewedAsync(profile, updated, cancellationToken).ConfigureAwait(false);
        return ValidationResult<StoredProfile>.Success(saved);
    }

    public async Task<ValidationResult<StoredProfile>> AddWebsiteRuleAsync(
        StoredProfile profile, RuleAction action, string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rule = WebsiteRule.Create(action, pattern);
        if (!rule.IsValid) return ValidationResult<StoredProfile>.Failure([.. rule.Errors]);
        var updated = profile with { WebsiteRules = [.. profile.WebsiteRules, rule.Value!] };
        var saved = await SaveReviewedAsync(profile, updated, cancellationToken).ConfigureAwait(false);
        return ValidationResult<StoredProfile>.Success(saved);
    }

    public async Task<bool> RemoveApplicationRuleAsync(
        StoredProfile profile, Guid ruleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rules = profile.ApplicationRules.Where(rule => rule.Id != ruleId).ToArray();
        if (rules.Length == profile.ApplicationRules.Count) return false;
        await SaveReviewedAsync(profile, profile with { ApplicationRules = rules }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RemoveWebsiteRuleAsync(
        StoredProfile profile, Guid ruleId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rules = profile.WebsiteRules.Where(rule => rule.Id != ruleId).ToArray();
        if (rules.Length == profile.WebsiteRules.Count) return false;
        await SaveReviewedAsync(profile, profile with { WebsiteRules = rules }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task SaveProfileAsync(StoredProfile profile, CancellationToken token = default) => store.SaveProfileAsync(profile, token);

    private async Task<StoredProfile> SaveReviewedAsync(StoredProfile? before, StoredProfile after, CancellationToken token)
    {
        if (store is IRevisionedProfileStore atomic)
        {
            var result = await atomic.TrySaveProfileAsync(after, before is null ? null : ProfileRevision.Compute(before), token).ConfigureAwait(false);
            if (result.Status != ProfileMutationStatus.Saved) throw new ProfileWriteConflictException(result.Status);
            return result.Profile!;
        }
        await store.SaveProfileAsync(after, token).ConfigureAwait(false);
        return after;
    }
    public Task<StoredProfile?> LoadProfileAsync(ProfileId id, CancellationToken token = default) => store.LoadProfileAsync(id, token);
    public Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(CancellationToken token = default) => store.ListProfilesAsync(token);
    public Task<bool> DeleteProfileAsync(ProfileId id, CancellationToken token = default) => store.DeleteProfileAsync(id, token);
    public Task SaveCyclePresetAsync(StoredCyclePreset preset, CancellationToken token = default) => store.SaveCyclePresetAsync(preset, token);
    public Task<StoredCyclePreset?> LoadCyclePresetAsync(Guid id, CancellationToken token = default) => store.LoadCyclePresetAsync(id, token);
    public Task<IReadOnlyList<StoredCyclePreset>> ListCyclePresetsAsync(CancellationToken token = default) => store.ListCyclePresetsAsync(token);
    public Task<bool> DeleteCyclePresetAsync(Guid id, CancellationToken token = default) => store.DeleteCyclePresetAsync(id, token);
    public Task<CyclePresetMutationResult> TrySaveCyclePresetAsync(StoredCyclePreset preset, string? expectedRevision, CancellationToken token = default) =>
        store is IRevisionedCyclePresetStore revisioned
            ? revisioned.TrySaveCyclePresetAsync(preset, expectedRevision, token)
            : Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.NotSupported));
    public Task<CyclePresetMutationResult> TryDeleteCyclePresetAsync(Guid id, string expectedRevision, CancellationToken token = default) =>
        store is IRevisionedCyclePresetStore revisioned
            ? revisioned.TryDeleteCyclePresetAsync(id, expectedRevision, token)
            : Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.NotSupported));
    public Task<CyclePresetMutationResult> TryDuplicateCyclePresetAsync(Guid id, string expectedRevision, string name, CancellationToken token = default) =>
        store is IRevisionedCyclePresetStore revisioned
            ? revisioned.TryDuplicateCyclePresetAsync(id, expectedRevision, name, token)
            : Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.NotSupported));
    public Task SaveProfileCycleSettingsAsync(ProfileCycleSettings settings, CancellationToken token = default) => store.SaveProfileCycleSettingsAsync(settings, token);
    public Task<ProfileCycleSettings?> LoadProfileCycleSettingsAsync(ProfileId id, CancellationToken token = default) => store.LoadProfileCycleSettingsAsync(id, token);

    public async Task<ProfileCycleSettingsSnapshot?> LoadRevisionedProfileCycleSettingsAsync(ProfileId id, CancellationToken token = default)
    {
        if (store is IRevisionedProfileCycleSettingsStore revisioned)
            return await revisioned.LoadRevisionedProfileCycleSettingsAsync(id, token).ConfigureAwait(false);
        if (await store.LoadProfileAsync(id, token).ConfigureAwait(false) is null) return null;
        var settings = await store.LoadProfileCycleSettingsAsync(id, token).ConfigureAwait(false)
            ?? new ProfileCycleSettings(id, null, [], []);
        var canonical = ProfileCycleSettingsRevision.Canonical(settings);
        return new(canonical, ProfileCycleSettingsRevision.Compute(canonical));
    }

    public Task<ProfileCycleSettingsMutationResult> TrySaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings, string expectedRevision, CancellationToken token = default) =>
        store is IRevisionedProfileCycleSettingsStore revisioned
            ? revisioned.TrySaveProfileCycleSettingsAsync(settings, expectedRevision, token)
            : Task.FromResult(new ProfileCycleSettingsMutationResult(ProfileCycleSettingsMutationStatus.NotSupported));
}
