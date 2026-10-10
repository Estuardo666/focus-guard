using System.Text.Json;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record CycleParameters(Guid ProfileId, Guid? PresetId, FocusCycleConfiguration? Configuration, bool EnforcementEnabled);
internal sealed record CyclePresetParameters(Guid? PresetId, Guid? SourcePresetId, string? Revision, string? Name,
    FocusCycleConfiguration? Configuration, Guid? OperationId);
internal sealed partial class BridgeRequestParser
{
    private static bool TryParseCyclePresetCommand(string? method, JsonElement parameters, out CyclePresetParameters? command)
    {
        command = null;
        if (method == "cyclePreset.save")
        {
            if (!Exact(parameters, "presetId", "revision", "name", "configuration", "operationId")
                || !TryGuid(parameters, "presetId", out var id) || !TryGuid(parameters, "operationId", out var operation)
                || !Text(parameters, "name", 80, out var name) || !PresetRevision(parameters.GetProperty("revision"), true, out var revision)
                || !TryPresetConfiguration(parameters.GetProperty("configuration"), out var configuration)) return false;
            command = new(id, null, revision, name, configuration, operation); return true;
        }
        if (method == "cyclePreset.delete")
        {
            if (!Exact(parameters, "presetId", "revision", "operationId") || !TryGuid(parameters, "presetId", out var id)
                || !TryGuid(parameters, "operationId", out var operation) || !PresetRevision(parameters.GetProperty("revision"), false, out var revision)) return false;
            command = new(id, null, revision, null, null, operation); return true;
        }
        if (method == "cyclePreset.duplicate")
        {
            if (!Exact(parameters, "sourcePresetId", "revision", "name", "operationId")
                || !TryGuid(parameters, "sourcePresetId", out var source) || !TryGuid(parameters, "operationId", out var operation)
                || !Text(parameters, "name", 80, out var name) || !PresetRevision(parameters.GetProperty("revision"), false, out var revision)) return false;
            command = new(null, source, revision, name, null, operation); return true;
        }
        return false;
    }

    private static bool PresetRevision(JsonElement element, bool allowNull, out string? revision)
    {
        revision = null;
        if (allowNull && element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value || !ProfileRevision.IsValid(value)) return false;
        revision = value; return true;
    }

    private static bool TryPresetConfiguration(JsonElement value, out FocusCycleConfiguration? configuration)
    {
        configuration = null;
        if (value.ValueKind != JsonValueKind.Object) return false;
        var json = JsonSerializer.Serialize(new { profileId = "11111111111141118111111111111111", presetId = (string?)null,
            configuration = value, enforcementEnabled = false });
        using var document = JsonDocument.Parse(json);
        return TryParseCycleCommand("session.prepareCycle", document.RootElement.GetProperty("params"),
            out var parsed, out _, out _, out _) && (configuration = parsed!.Configuration) is not null;
    }

    private static bool TryParseCycleCommand(string? method, JsonElement parameters, out CycleParameters? cycle,
        out Guid? preparation, out Guid? operation, out int offset)
    {
        cycle = null; preparation = null; operation = null; offset = 0;
        if (method == "cycles.list" && Exact(parameters, "offset") && parameters.GetProperty("offset").TryGetInt32(out offset)
            && offset is >= 0 and <= 10000) return true;
        if (method != "session.prepareCycle" || !Exact(parameters, "profileId", "presetId", "configuration", "enforcementEnabled")
            || !TryGuid(parameters, "profileId", out var profile)
            || parameters.GetProperty("enforcementEnabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False) return false;
        Guid? preset = null; FocusCycleConfiguration? config = null;
        if (parameters.GetProperty("presetId").ValueKind != JsonValueKind.Null)
        {
            if (!TryGuid(parameters, "presetId", out var presetId) || parameters.GetProperty("configuration").ValueKind != JsonValueKind.Null) return false;
            preset = presetId;
        }
        else
        {
            var value = parameters.GetProperty("configuration");
            if (value.ValueKind != JsonValueKind.Object || !Exact(value, "focusSeconds", "shortBreakSeconds", "longBreakSeconds", "cyclesBeforeLongBreak", "terminationMode", "cycleCount", "totalDurationSeconds")
                || !value.GetProperty("focusSeconds").TryGetInt32(out var focus) || focus is < 60 or > 10800
                || !value.GetProperty("shortBreakSeconds").TryGetInt32(out var shortBreak) || shortBreak is < 60 or > 3600
                || !value.GetProperty("longBreakSeconds").TryGetInt32(out var longBreak) || longBreak is < 60 or > 7200
                || !value.GetProperty("cyclesBeforeLongBreak").TryGetInt32(out var frequency) || frequency is < 2 or > 12
                || value.GetProperty("terminationMode").ValueKind != JsonValueKind.String) return false;
            int? count = null; TimeSpan? total = null; CycleTerminationMode mode;
            if (value.GetProperty("terminationMode").GetString() == "CycleCount")
            {
                mode = CycleTerminationMode.CycleCount;
                if (!value.GetProperty("cycleCount").TryGetInt32(out var parsed) || parsed is < 1 or > 24 || value.GetProperty("totalDurationSeconds").ValueKind != JsonValueKind.Null) return false;
                count = parsed;
            }
            else if (value.GetProperty("terminationMode").GetString() == "TotalDuration")
            {
                mode = CycleTerminationMode.TotalDuration;
                if (!value.GetProperty("totalDurationSeconds").TryGetInt32(out var parsed) || parsed is < 60 or > 2592000 || value.GetProperty("cycleCount").ValueKind != JsonValueKind.Null) return false;
                total = TimeSpan.FromSeconds(parsed);
            }
            else return false;
            var created = FocusCycleConfiguration.Create(TimeSpan.FromSeconds(focus), TimeSpan.FromSeconds(shortBreak), TimeSpan.FromSeconds(longBreak), frequency, mode, count, total);
            if (!created.IsValid) return false;
            config = created.Value;
        }
        cycle = new(profile, preset, config, parameters.GetProperty("enforcementEnabled").GetBoolean()); return true;
    }
}
