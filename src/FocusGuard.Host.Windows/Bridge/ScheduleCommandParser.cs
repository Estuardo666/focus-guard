using System.Text.Json;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record ScheduleCommand(string Action, Guid ScheduleId, string? ExpectedRevision, string? Name, int Days,
    int StartMinute, string? TimeZoneId, Guid? ProfileId, int DurationMinutes,
    bool EnforcementEnabled, bool Enabled, Guid? OperationId);

internal sealed partial class BridgeRequestParser
{
    private static bool TryParseScheduleCommand(string? method, JsonElement parameters, out ScheduleCommand? command)
    {
        command = null;
        if (method == "schedule.save")
        {
            if (!Exact(parameters, "scheduleId", "expectedRevision", "name", "days", "startMinute", "timeZoneId", "profileId", "durationMinutes", "enforcementEnabled", "enabled", "operationId")
                || !TryGuid(parameters, "scheduleId", out var id) || !TryGuid(parameters, "profileId", out var profile)
                || !TryGuid(parameters, "operationId", out var operation) || !Text(parameters, "name", 80, out var name)
                || !Text(parameters, "timeZoneId", 100, out var zone) || !OptionalRevision(parameters.GetProperty("expectedRevision"), out var revision)
                || !parameters.GetProperty("days").TryGetInt32(out var days) || days is < 1 or > 127
                || !parameters.GetProperty("startMinute").TryGetInt32(out var minute) || minute is < 0 or >= 1440
                || !parameters.GetProperty("durationMinutes").TryGetInt32(out var duration) || duration is < 1 or > 720
                || parameters.GetProperty("enforcementEnabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False
                || parameters.GetProperty("enabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False) return false;
            command = new("Save", id, revision, name, days, minute, zone, profile, duration,
                parameters.GetProperty("enforcementEnabled").GetBoolean(), parameters.GetProperty("enabled").GetBoolean(), operation);
            return true;
        }
        if (method == "schedule.enable" && Exact(parameters, "scheduleId", "revision", "enabled", "operationId")
            && TryGuid(parameters, "scheduleId", out var enableId) && TryGuid(parameters, "operationId", out var enableOperation)
            && RequiredRevision(parameters.GetProperty("revision"), out var enableRevision)
            && parameters.GetProperty("enabled").ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            command = new("Enable", enableId, enableRevision, null, 0, 0, null, null, 0,
                false, parameters.GetProperty("enabled").GetBoolean(), enableOperation);
            return true;
        }
        if (method == "schedule.delete" && Exact(parameters, "scheduleId", "revision", "operationId")
            && TryGuid(parameters, "scheduleId", out var deleteId) && TryGuid(parameters, "operationId", out var deleteOperation)
            && RequiredRevision(parameters.GetProperty("revision"), out var deleteRevision))
        {
            command = new("Delete", deleteId, deleteRevision, null, 0, 0, null, null, 0, false, false, deleteOperation);
            return true;
        }
        return false;
    }

    private static bool OptionalRevision(JsonElement value, out string? revision)
    {
        revision = null;
        if (value.ValueKind == JsonValueKind.Null) return true;
        return RequiredRevision(value, out revision);
    }

    private static bool RequiredRevision(JsonElement value, out string? revision)
    {
        revision = null;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } candidate
            || candidate.Length != 64 || candidate.Any(character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))) return false;
        revision = candidate;
        return true;
    }
}
