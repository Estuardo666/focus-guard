using FocusGuard.Client.Workflows;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeRulePresentation(string Name, string? Path, BridgeIcon? Icon, string Resolution);
internal sealed record BridgeApplicationRule(string Id, string Action, string MatchKind, string Value,
    BridgeRulePresentation? Presentation = null);
internal sealed record BridgeWebsiteRule(string Id, string Action, string Pattern);
internal sealed record BridgeBreakApplicationRule(string Id, string MatchKind, string Value);
internal sealed record BridgeBreakWebsiteRule(string Id, string Pattern);
internal sealed record BridgeProfileDetail(string Id, string Name, string? Description, string? AccentHex,
    string Revision, IReadOnlyList<BridgeApplicationRule> Applications, int ApplicationOffset, int ApplicationTotal,
    IReadOnlyList<BridgeWebsiteRule> Websites, int WebsiteOffset, int WebsiteTotal, bool EditingEnabled,
    string? DefaultCyclePresetId, string? DefaultCyclePresetName, string CycleSettingsRevision, IReadOnlyList<BridgeBreakApplicationRule> BreakApplications,
    int BreakApplicationOffset, int BreakApplicationTotal, IReadOnlyList<BridgeBreakWebsiteRule> BreakWebsites,
    int BreakWebsiteOffset, int BreakWebsiteTotal, bool CycleSettingsEditingEnabled);
internal sealed record BridgeProfileMutation(string? ProfileId, string? Revision);

internal sealed partial class NativeBridgeDispatcher
{
    private readonly ProfileEditorWorkflow? _editor = profileStore is IRevisionedProfileStore atomic ? new(atomic) : null;
    private readonly IRevisionedProfileCycleSettingsStore? _cycleSettingsEditor = profileStore as IRevisionedProfileCycleSettingsStore;
    private readonly HashSet<Guid> _profileOperationIds = [];

    private async ValueTask<(string? Error, object? Result)> HandleProfileCommandAsync(BridgeRequest request, CancellationToken token)
    {
        if (profileStore is null) return ("AuthorizationUnavailable", null);
        var input = request.ProfileCommand!;
        if (request.Method == "profile.get")
        {
            var profile = await profileStore.LoadProfileAsync(new(input.ProfileId!.Value), token).ConfigureAwait(false);
            if (profile is null) return ("ProfileMissing", null);
            if (!Profile.ValidateName(profile.Name).IsValid || profile.ApplicationRules.Count > 10000 || profile.WebsiteRules.Count > 10000)
                return ("InvalidProfile", null);
            var applications = new List<BridgeApplicationRule>();
            foreach (var rule in profile.ApplicationRules.OrderBy(rule => rule.Id).Skip(input.ApplicationOffset).Take(6))
                applications.Add(new(rule.Id.ToString("D"), rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value,
                    await PresentRuleAsync(rule, request.DocumentToken, token).ConfigureAwait(false)));
            var websites = profile.WebsiteRules.OrderBy(rule => rule.Id).Skip(input.WebsiteOffset).Take(6)
                .Select(rule => new BridgeWebsiteRule(rule.Id.ToString("D"), rule.Action.ToString(), rule.Pattern)).ToArray();
            var cycleSnapshot = _cycleSettingsEditor is null
                ? null
                : await _cycleSettingsEditor.LoadRevisionedProfileCycleSettingsAsync(profile.Id, token).ConfigureAwait(false);
            if (_cycleSettingsEditor is not null && cycleSnapshot is null) return ("ProfileMissing", null);
            var cycleSettings = cycleSnapshot?.Settings ?? new ProfileCycleSettings(profile.Id, null, [], []);
            var cycleSettingsRevision = cycleSnapshot?.Revision ?? ProfileCycleSettingsRevision.Compute(cycleSettings);
            var defaultPreset = cycleSettings.DefaultPresetId is { } defaultPresetId
                ? await profileStore.LoadCyclePresetAsync(defaultPresetId, token).ConfigureAwait(false)
                : null;
            var breakApplications = cycleSettings.ApplicationRules.OrderBy(rule => rule.Id)
                .Skip(input.BreakApplicationOffset).Take(6)
                .Select(rule => new BridgeBreakApplicationRule(rule.Id.ToString("D"), rule.MatchKind.ToString(), rule.Value)).ToArray();
            var breakWebsites = cycleSettings.WebsiteRules.OrderBy(rule => rule.Id)
                .Skip(input.BreakWebsiteOffset).Take(6)
                .Select(rule => new BridgeBreakWebsiteRule(rule.Id.ToString("D"), rule.Pattern)).ToArray();
            return (null, new BridgeProfileDetail(profile.Id.Value.ToString("D"), profile.Name, profile.Description,
                profile.AccentHex, ProfileRevision.Compute(profile), applications, input.ApplicationOffset, profile.ApplicationRules.Count,
                websites, input.WebsiteOffset, profile.WebsiteRules.Count,
                _editor is not null && authorization.Authorize(HostControlOperation.WriteProfiles).State == HostAuthorizationState.Authorized,
                cycleSettings.DefaultPresetId?.ToString("D"), defaultPreset?.Name, cycleSettingsRevision,
                breakApplications, input.BreakApplicationOffset, cycleSettings.ApplicationRules.Count,
                breakWebsites, input.BreakWebsiteOffset, cycleSettings.WebsiteRules.Count,
                _cycleSettingsEditor is not null && authorization.Authorize(HostControlOperation.WriteProfiles).State == HostAuthorizationState.Authorized));
        }

        var isCycleSettingsMutation = request.Method.StartsWith("profile.break.", StringComparison.Ordinal)
            || request.Method == "profile.cycle.defaultPreset.set";
        if (_editor is null || isCycleSettingsMutation && _cycleSettingsEditor is null) return ("EditorUnavailable", null);
        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (!CanStart(status)) return ("SessionAlreadyActive", null);
        if (request.Method == "profile.application.addFromCatalog"
            && !TryGetCatalogApplication(request.DocumentToken, input.CatalogId!.Value, out _))
            return ("CatalogChanged", null);
        if (request.Method == "profile.application.addFromCatalog")
        {
            var stored = await profileStore.LoadProfileAsync(new(input.ProfileId!.Value), token).ConfigureAwait(false);
            if (stored is null) return ("ProfileMissing", null);
            if (ProfileRevision.Compute(stored) != input.Revision) return ("ProfileConflict", null);
            var path = CatalogPath(request.DocumentToken, input.CatalogId!.Value);
            if (stored.ApplicationRules.Any(rule => string.Equals(rule.Value, path, StringComparison.OrdinalIgnoreCase)))
                return ("RuleAlreadyExists", null);
        }
        if (_profileOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_profileOperationIds.Add(input.OperationId!.Value)) return ("DuplicateOperation", null);
        // A changed profile invalidates any earlier local start review. The service
        // still owns its immutable preparation; it cannot be consumed through this page.
        _normalReview = null;
        _cycleReview = null;
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        if (isCycleSettingsMutation) return await HandleBreakRuleMutationAsync(request.Method, input, token).ConfigureAwait(false);
        var id = input.ProfileId is { } guid ? new ProfileId(guid) : null;
        var result = request.Method switch
        {
            "profile.create" => await _editor.CreateAsync(input.Name!, input.AccentHex, token).ConfigureAwait(false),
            "profile.rename" => await _editor.RenameAsync(id!, input.Revision!, input.Name!, token).ConfigureAwait(false),
            "profile.accent" => await _editor.SetAccentAsync(id!, input.Revision!, input.AccentHex, token).ConfigureAwait(false),
            "profile.duplicate" => await _editor.DuplicateAsync(id!, input.Revision!, input.Language!, token).ConfigureAwait(false),
            "profile.delete" => await _editor.DeleteAsync(id!, input.Revision!, token).ConfigureAwait(false),
            "profile.application.add" => await _editor.AddApplicationAsync(id!, input.Revision!, input.Action!.Value, input.MatchKind!.Value, input.Value!, token).ConfigureAwait(false),
            "profile.application.addFromCatalog" => await _editor.AddApplicationAsync(id!, input.Revision!, input.Action!.Value,
                ApplicationMatchKind.ExactPath, CatalogPath(request.DocumentToken, input.CatalogId!.Value), token).ConfigureAwait(false),
            "profile.website.add" => await _editor.AddWebsiteAsync(id!, input.Revision!, input.Action!.Value, input.Value!, token).ConfigureAwait(false),
            "profile.application.remove" => await _editor.RemoveApplicationAsync(id!, input.Revision!, input.RuleId!.Value, token).ConfigureAwait(false),
            "profile.website.remove" => await _editor.RemoveWebsiteAsync(id!, input.Revision!, input.RuleId!.Value, token).ConfigureAwait(false),
            _ => new ProfileMutationResult(ProfileMutationStatus.InvalidInput)
        };
        if (result.Status != ProfileMutationStatus.Saved)
            return (result.Status == ProfileMutationStatus.Conflict ? "ProfileConflict" : result.Status.ToString(), null);
        return (null, new BridgeProfileMutation(result.Profile?.Id.Value.ToString("D"),
            result.Profile is { } saved ? ProfileRevision.Compute(saved) : null));
    }

    private async ValueTask<(string? Error, object? Result)> HandleBreakRuleMutationAsync(
        string method, ProfileCommand input, CancellationToken token)
    {
        var id = new ProfileId(input.ProfileId!.Value);
        var current = await _cycleSettingsEditor!.LoadRevisionedProfileCycleSettingsAsync(id, token).ConfigureAwait(false);
        if (current is null) return ("ProfileMissing", null);
        if (current.Revision != input.CycleSettingsRevision) return ("ProfileConflict", null);
        var settings = current.Settings;
        switch (method)
        {
            case "profile.break.application.add":
            {
                if (settings.ApplicationRules.Count >= 10000) return ("RuleLimitReached", null);
                var validated = ApplicationRule.Create(RuleAction.Block, input.MatchKind!.Value, input.Value!);
                if (!validated.IsValid) return ("InvalidInput", null);
                var rule = validated.Value!;
                if (settings.ApplicationRules.Any(existing => existing.MatchKind == rule.MatchKind
                    && string.Equals(existing.Value, rule.Value, StringComparison.OrdinalIgnoreCase)))
                    return ("RuleAlreadyExists", null);
                settings = settings with { ApplicationRules = [.. settings.ApplicationRules,
                    new BreakApplicationRule(rule.Id, RuleAction.Block, rule.MatchKind, rule.Value)] };
                break;
            }
            case "profile.break.website.add":
            {
                if (settings.WebsiteRules.Count >= 10000) return ("RuleLimitReached", null);
                var validated = WebsiteRule.Create(RuleAction.Block, input.Value!);
                if (!validated.IsValid) return ("InvalidInput", null);
                var rule = validated.Value!;
                if (settings.WebsiteRules.Any(existing => string.Equals(existing.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase)))
                    return ("RuleAlreadyExists", null);
                settings = settings with { WebsiteRules = [.. settings.WebsiteRules,
                    new BreakWebsiteRule(rule.Id, RuleAction.Block, rule.Pattern)] };
                break;
            }
            case "profile.break.application.remove":
            {
                var remaining = settings.ApplicationRules.Where(rule => rule.Id != input.RuleId!.Value).ToArray();
                if (remaining.Length == settings.ApplicationRules.Count) return ("RuleMissing", null);
                settings = settings with { ApplicationRules = remaining };
                break;
            }
            case "profile.break.website.remove":
            {
                var remaining = settings.WebsiteRules.Where(rule => rule.Id != input.RuleId!.Value).ToArray();
                if (remaining.Length == settings.WebsiteRules.Count) return ("RuleMissing", null);
                settings = settings with { WebsiteRules = remaining };
                break;
            }
            case "profile.cycle.defaultPreset.set":
                settings = settings with { DefaultPresetId = input.PresetId };
                break;
            default:
                return ("InvalidInput", null);
        }

        var result = await _cycleSettingsEditor.TrySaveProfileCycleSettingsAsync(settings, current.Revision, token).ConfigureAwait(false);
        if (result.Status != ProfileCycleSettingsMutationStatus.Saved)
        {
            var error = result.Status switch
            {
                ProfileCycleSettingsMutationStatus.Conflict => "ProfileConflict",
                ProfileCycleSettingsMutationStatus.ProfileMissing => "ProfileMissing",
                ProfileCycleSettingsMutationStatus.PresetMissing => "PresetMissing",
                _ => "InvalidInput"
            };
            return (error, null);
        }

        var profile = await profileStore!.LoadProfileAsync(id, token).ConfigureAwait(false);
        return profile is null
            ? ("ProfileMissing", null)
            : (null, new BridgeProfileMutation(profile.Id.Value.ToString("D"), ProfileRevision.Compute(profile)));
    }

    private async Task<BridgeRulePresentation?> PresentRuleAsync(
        ApplicationRule rule,
        string documentToken,
        CancellationToken token)
    {
        if (rule.MatchKind == ApplicationMatchKind.Folder) return new(rule.Value, null, null, "Folder");
        if (rule.MatchKind == ApplicationMatchKind.Sha256) return new(rule.Value, null, null, "Hash");
        // Reading saved rules must not wait for Start-menu/COM discovery. The catalog
        // is loaded only when the user opens application search; until then the UI
        // renders the exact stored rule value without claiming a catalog resolution.
        if (_catalogDocument != documentToken) return null;
        var matches = _catalogApplications.Where(pair => rule.MatchKind == ApplicationMatchKind.ExactPath
            ? string.Equals(pair.Value.Path, rule.Value, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetFileName(pair.Value.Path), rule.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) return new(rule.Value, null, null, "Ambiguous");
        if (matches.Length == 0) return new(rule.Value, null, null, "Unresolved");
        var match = matches[0];
        BridgeIcon? icon = null;
        try { icon = await CatalogIconAsync(match.Key, match.Value.Path, token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        return new(match.Value.Name, match.Value.Path, icon,
            rule.MatchKind == ApplicationMatchKind.ExactPath ? "ExactMatch" : "FileNameMatch");
    }
}
