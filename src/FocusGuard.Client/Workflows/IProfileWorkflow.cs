using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

// Keep the existing profile/preset storage boundary during the incremental
// extraction. Typed editing operations add reusable client-side coordination.
public interface IProfileWorkflow : IProfileStore
{
    Task<CyclePresetMutationResult> TrySaveCyclePresetAsync(StoredCyclePreset preset, string? expectedRevision, CancellationToken cancellationToken = default);

    Task<CyclePresetMutationResult> TryDeleteCyclePresetAsync(Guid id, string expectedRevision, CancellationToken cancellationToken = default);

    Task<CyclePresetMutationResult> TryDuplicateCyclePresetAsync(Guid id, string expectedRevision, string name, CancellationToken cancellationToken = default);

    Task<ValidationResult<StoredProfile>> CreateAsync(
        string name, IReadOnlyList<ApplicationRule> applicationRules,
        IReadOnlyList<WebsiteRule> websiteRules, string? accentHex,
        CancellationToken cancellationToken = default);

    Task<ValidationResult<StoredProfile>> RenameAsync(
        StoredProfile profile, string name, CancellationToken cancellationToken = default);

    Task<ValidationResult<StoredProfile>> SetAccentAsync(
        StoredProfile profile, string? accentHex, CancellationToken cancellationToken = default);

    Task<bool> DeleteReviewedAsync(StoredProfile profile, CancellationToken cancellationToken = default);

    Task<StoredProfile> DuplicateAsync(
        StoredProfile profile, string copyNameFormat, string numberSuffixFormat,
        CancellationToken cancellationToken = default);

    Task<ValidationResult<StoredProfile>> AddApplicationRuleAsync(
        StoredProfile profile, RuleAction action, ApplicationMatchKind matchKind, string value,
        CancellationToken cancellationToken = default);

    Task<ValidationResult<StoredProfile>> AddWebsiteRuleAsync(
        StoredProfile profile, RuleAction action, string pattern,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveApplicationRuleAsync(
        StoredProfile profile, Guid ruleId, CancellationToken cancellationToken = default);

    Task<bool> RemoveWebsiteRuleAsync(
        StoredProfile profile, Guid ruleId, CancellationToken cancellationToken = default);

    Task<ProfileCycleSettingsSnapshot?> LoadRevisionedProfileCycleSettingsAsync(
        ProfileId id, CancellationToken cancellationToken = default);

    Task<ProfileCycleSettingsMutationResult> TrySaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings, string expectedRevision, CancellationToken cancellationToken = default);
}
