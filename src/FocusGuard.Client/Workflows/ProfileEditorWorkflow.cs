using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

/// <summary>Revisioned user-local editing. Host/UI separately enforce fresh inactive-session guards.</summary>
public sealed class ProfileEditorWorkflow(IRevisionedProfileStore store)
{
    public async Task<ProfileMutationResult> CreateAsync(string name, string? accentHex, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var created = Profile.Create(name, accentHex: accentHex);
        if (!created.IsValid) return new(ProfileMutationStatus.InvalidInput);
        var value = created.Value!;
        return await store.TrySaveProfileAsync(new(value.Id, value.Name, [], [], AccentHex: value.AccentHex), null, token).ConfigureAwait(false);
    }

    public Task<ProfileMutationResult> RenameAsync(ProfileId id, string revision, string name, CancellationToken token = default) =>
        ChangeAsync(id, revision, profile => Profile.ValidateName(name) is { IsValid: true, Value: { } valid }
            ? profile with { Name = valid } : null, token);

    public Task<ProfileMutationResult> SetAccentAsync(ProfileId id, string revision, string? accent, CancellationToken token = default)
    {
        var normalized = string.IsNullOrWhiteSpace(accent) ? null : accent.Trim();
        return ChangeAsync(id, revision, profile => normalized is null || Profile.IsAccentHex(normalized)
            ? profile with { AccentHex = normalized?.ToUpperInvariant() } : null, token);
    }

    public Task<ProfileMutationResult> AddApplicationAsync(ProfileId id, string revision, RuleAction action,
        ApplicationMatchKind kind, string value, CancellationToken token = default) =>
        ChangeAsync(id, revision, profile => ApplicationRule.Create(action, kind, value) is { IsValid: true, Value: { } rule }
            ? profile with { ApplicationRules = [.. profile.ApplicationRules, rule] } : null, token);

    public Task<ProfileMutationResult> AddWebsiteAsync(ProfileId id, string revision, RuleAction action,
        string pattern, CancellationToken token = default) =>
        ChangeAsync(id, revision, profile => WebsiteRule.Create(action, pattern) is { IsValid: true, Value: { } rule }
            ? profile with { WebsiteRules = [.. profile.WebsiteRules, rule] } : null, token);

    public Task<ProfileMutationResult> RemoveApplicationAsync(ProfileId id, string revision, Guid ruleId, CancellationToken token = default) =>
        ChangeAsync(id, revision, profile => profile.ApplicationRules.Any(rule => rule.Id == ruleId)
            ? profile with { ApplicationRules = profile.ApplicationRules.Where(rule => rule.Id != ruleId).ToArray() } : null, token,
            ProfileMutationStatus.RuleMissing);

    public Task<ProfileMutationResult> RemoveWebsiteAsync(ProfileId id, string revision, Guid ruleId, CancellationToken token = default) =>
        ChangeAsync(id, revision, profile => profile.WebsiteRules.Any(rule => rule.Id == ruleId)
            ? profile with { WebsiteRules = profile.WebsiteRules.Where(rule => rule.Id != ruleId).ToArray() } : null, token,
            ProfileMutationStatus.RuleMissing);

    public Task<ProfileMutationResult> DuplicateAsync(ProfileId id, string revision, string language, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (language is not "es" and not "en") return Task.FromResult(new ProfileMutationResult(ProfileMutationStatus.InvalidInput));
        return store.TryDuplicateProfileAsync(id, revision, language == "es" ? "Copia de" : "Copy of", token);
    }

    public Task<ProfileMutationResult> DeleteAsync(ProfileId id, string revision, CancellationToken token = default) =>
        store.TryDeleteProfileAsync(id, revision, token);

    private async Task<ProfileMutationResult> ChangeAsync(ProfileId id, string revision,
        Func<StoredProfile, StoredProfile?> change, CancellationToken token, ProfileMutationStatus invalid = ProfileMutationStatus.InvalidInput)
    {
        token.ThrowIfCancellationRequested();
        if (id.Value == Guid.Empty || !ProfileRevision.IsValid(revision)) return new(ProfileMutationStatus.InvalidInput);
        var current = await store.LoadProfileAsync(id, token).ConfigureAwait(false);
        if (current is null) return new(ProfileMutationStatus.ProfileMissing);
        if (ProfileRevision.Compute(current) != revision) return new(ProfileMutationStatus.Conflict);
        var changed = change(current);
        return changed is null ? new(invalid) : await store.TrySaveProfileAsync(changed, revision, token).ConfigureAwait(false);
    }
}
