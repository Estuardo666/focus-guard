using System.Text.Json;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record ProfileCommand(Guid? ProfileId = null, string? Revision = null, Guid? OperationId = null,
    string? Name = null, string? AccentHex = null, string? Language = null, RuleAction? Action = null,
    ApplicationMatchKind? MatchKind = null, string? Value = null, Guid? RuleId = null,
    int ApplicationOffset = 0, int WebsiteOffset = 0, Guid? CatalogId = null,
    string? CycleSettingsRevision = null, int BreakApplicationOffset = 0, int BreakWebsiteOffset = 0,
    Guid? PresetId = null);

internal sealed partial class BridgeRequestParser
{
    private static bool Exact(JsonElement parameters, params string[] keys) =>
        HasExactProperties(parameters, new HashSet<string>(keys, StringComparer.Ordinal));

    private static bool Text(JsonElement parameters, string name, int limit, out string value)
    {
        value = string.Empty;
        if (parameters.GetProperty(name).ValueKind != JsonValueKind.String
            || parameters.GetProperty(name).GetString() is not { } text
            || string.IsNullOrWhiteSpace(text) || text.Length > limit) return false;
        value = text;
        return true;
    }

    private static bool Accent(JsonElement parameters, out string? accent)
    {
        accent = null;
        var element = parameters.GetProperty("accentHex");
        if (element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String || !Profile.IsAccentHex(element.GetString())) return false;
        accent = element.GetString(); return true;
    }

    private static bool Action(JsonElement parameters, out RuleAction action)
    {
        action = default;
        return parameters.GetProperty("action").ValueKind == JsonValueKind.String
            && parameters.GetProperty("action").GetString() is "Allow" or "Block"
            && Enum.TryParse(parameters.GetProperty("action").GetString(), out action);
    }

    private static bool TryParseProfileCommand(string? method, JsonElement parameters, out ProfileCommand? command)
    {
        command = null;
        if (method == "profile.get" && Exact(parameters, "profileId", "applicationOffset", "websiteOffset", "breakApplicationOffset", "breakWebsiteOffset")
            && TryGuid(parameters, "profileId", out var readId)
            && parameters.GetProperty("applicationOffset").TryGetInt32(out var apps) && apps is >= 0 and <= 10000
            && parameters.GetProperty("websiteOffset").TryGetInt32(out var sites) && sites is >= 0 and <= 10000
            && parameters.GetProperty("breakApplicationOffset").TryGetInt32(out var breakApps) && breakApps is >= 0 and <= 10000
            && parameters.GetProperty("breakWebsiteOffset").TryGetInt32(out var breakSites) && breakSites is >= 0 and <= 10000)
        { command = new(readId, ApplicationOffset: apps, WebsiteOffset: sites, BreakApplicationOffset: breakApps, BreakWebsiteOffset: breakSites); return true; }

        if (method == "profile.create" && Exact(parameters, "name", "accentHex", "operationId")
            && Text(parameters, "name", 80, out var createName) && Accent(parameters, out var createAccent)
            && TryGuid(parameters, "operationId", out var createOperation))
        { command = new(OperationId: createOperation, Name: createName, AccentHex: createAccent); return true; }

        var breakFields = method switch
        {
            "profile.break.application.add" => new[] { "matchKind", "value" },
            "profile.break.website.add" => ["pattern"],
            "profile.break.application.remove" or "profile.break.website.remove" => ["ruleId"],
            "profile.cycle.defaultPreset.set" => ["presetId"],
            _ => null
        };
        if (breakFields is not null
            && Exact(parameters, ["profileId", "cycleSettingsRevision", "operationId", .. breakFields])
            && TryGuid(parameters, "profileId", out var breakProfileId)
            && TryGuid(parameters, "operationId", out var breakOperation)
            && Text(parameters, "cycleSettingsRevision", 64, out var cycleRevision)
            && ProfileRevision.IsValid(cycleRevision))
        {
            var breakCommand = new ProfileCommand(ProfileId: breakProfileId, OperationId: breakOperation,
                CycleSettingsRevision: cycleRevision);
            if (method == "profile.break.application.add"
                && Text(parameters, "matchKind", 16, out var breakKind)
                && breakKind is "ExactPath" or "Folder" or "Sha256" or "FileName"
                && Enum.TryParse<ApplicationMatchKind>(breakKind, out var parsedBreakKind)
                && Text(parameters, "value", 1024, out var breakValue))
                command = breakCommand with { MatchKind = parsedBreakKind, Value = breakValue };
            else if (method == "profile.break.website.add" && Text(parameters, "pattern", 1024, out var breakPattern))
                command = breakCommand with { Value = breakPattern };
            else if (method is "profile.break.application.remove" or "profile.break.website.remove"
                && TryGuid(parameters, "ruleId", out var breakRuleId))
                command = breakCommand with { RuleId = breakRuleId };
            else if (method == "profile.cycle.defaultPreset.set")
            {
                var presetElement = parameters.GetProperty("presetId");
                if (presetElement.ValueKind == JsonValueKind.Null) command = breakCommand with { PresetId = null };
                else if (TryGuid(parameters, "presetId", out var defaultPresetId)) command = breakCommand with { PresetId = defaultPresetId };
            }
            return command is not null;
        }

        var fields = method switch
        {
            "profile.rename" => new[] { "name" },
            "profile.accent" => ["accentHex"],
            "profile.duplicate" => ["language"],
            "profile.delete" => ["confirmed"],
            "profile.application.add" => ["action", "matchKind", "value"],
            "profile.application.addFromCatalog" => ["action", "catalogId"],
            "profile.website.add" => ["action", "pattern"],
            "profile.application.remove" or "profile.website.remove" => ["ruleId"],
            _ => null
        };
        if (fields is null || !Exact(parameters, ["profileId", "revision", "operationId", .. fields])
            || !TryGuid(parameters, "profileId", out var id) || !TryGuid(parameters, "operationId", out var operation)
            || !Text(parameters, "revision", 64, out var revision) || !ProfileRevision.IsValid(revision)) return false;

        var baseCommand = new ProfileCommand(id, revision, operation);
        if (method == "profile.rename" && Text(parameters, "name", 80, out var name)) command = baseCommand with { Name = name };
        else if (method == "profile.accent" && Accent(parameters, out var accent)) command = baseCommand with { AccentHex = accent };
        else if (method == "profile.duplicate" && Text(parameters, "language", 2, out var language) && language is "es" or "en") command = baseCommand with { Language = language };
        else if (method == "profile.delete" && parameters.GetProperty("confirmed").ValueKind == JsonValueKind.True) command = baseCommand;
        else if (method is "profile.application.remove" or "profile.website.remove" && TryGuid(parameters, "ruleId", out var rule)) command = baseCommand with { RuleId = rule };
        else if (method == "profile.website.add" && Action(parameters, out var websiteAction) && Text(parameters, "pattern", 1024, out var pattern)) command = baseCommand with { Action = websiteAction, Value = pattern };
        else if (method == "profile.application.addFromCatalog" && Action(parameters, out var catalogAction)
            && TryGuid(parameters, "catalogId", out var catalogId)) command = baseCommand with { Action = catalogAction, CatalogId = catalogId };
        else if (method == "profile.application.add" && Action(parameters, out var appAction)
            && Text(parameters, "matchKind", 16, out var kind) && kind is "ExactPath" or "Folder" or "Sha256" or "FileName"
            && Enum.TryParse<ApplicationMatchKind>(kind, out var parsedKind) && Text(parameters, "value", 1024, out var value))
            command = baseCommand with { Action = appAction, MatchKind = parsedKind, Value = value };
        return command is not null;
    }
}
