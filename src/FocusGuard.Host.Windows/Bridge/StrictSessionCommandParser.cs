using System.Text.Json;
using FocusGuard.Domain;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record StrictSessionParameters(Guid ProfileId, SessionMode Mode, int DurationSeconds,
    Guid? PresetId, FocusCycleConfiguration? Configuration, bool EnforcementEnabled);
internal sealed record StrictSessionActivationParameters(Guid PreparationId, Guid OperationId,
    string TypedConfirmation, bool RecoverySaved, bool UnsavedWorkConfirmed, string Objective);

internal sealed partial class BridgeRequestParser
{
    private static bool TryParseStrictSessionCommand(string? method, JsonElement parameters,
        out StrictSessionParameters? strictSession, out StrictSessionActivationParameters? activation,
        out Guid? cancellation, out Guid? saveCode, out bool recoveryInfo)
    {
        strictSession = null; activation = null; cancellation = null; saveCode = null; recoveryInfo = false;
        if (method == "recovery.info" && Exact(parameters)) { recoveryInfo = true; return true; }
        if (method == "session.strict.saveRecoveryCode" && Exact(parameters, "preparationId", "confirmed")
            && TryGuid(parameters, "preparationId", out var saveId)
            && parameters.GetProperty("confirmed").ValueKind == JsonValueKind.True)
        { saveCode = saveId; return true; }
        if (method == "session.strict.cancel" && Exact(parameters, "preparationId")
            && TryGuid(parameters, "preparationId", out var cancelId))
        { cancellation = cancelId; return true; }
        if (method == "session.strict.activate")
        {
            if (!Exact(parameters, "preparationId", "operationId", "typedConfirmation", "recoverySaved", "unsavedWorkConfirmed", "confirmed", "objective")
                || !TryGuid(parameters, "preparationId", out var preparationId)
                || !TryGuid(parameters, "operationId", out var operationId)
                || !Text(parameters, "typedConfirmation", 80, out var confirmation)
                || !TryBoundedText(parameters, "objective", 240, out var objective)
                || parameters.GetProperty("recoverySaved").ValueKind is not JsonValueKind.True and not JsonValueKind.False
                || parameters.GetProperty("unsavedWorkConfirmed").ValueKind is not JsonValueKind.True and not JsonValueKind.False
                || parameters.GetProperty("confirmed").ValueKind != JsonValueKind.True) return false;
            activation = new(preparationId, operationId, confirmation,
                parameters.GetProperty("recoverySaved").GetBoolean(),
                parameters.GetProperty("unsavedWorkConfirmed").GetBoolean(), objective);
            return true;
        }
        if (method != "session.strict.prepare"
            || !Exact(parameters, "profileId", "mode", "durationSeconds", "presetId", "configuration", "enforcementEnabled")
            || !TryGuid(parameters, "profileId", out var profileId)
            || !Text(parameters, "mode", 16, out var modeText)
            || !Enum.TryParse<SessionMode>(modeText, false, out var mode) || !Enum.IsDefined(mode)
            || !parameters.GetProperty("durationSeconds").TryGetInt32(out var durationSeconds)
            || parameters.GetProperty("enforcementEnabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            return false;

        var presetValue = parameters.GetProperty("presetId");
        var configurationValue = parameters.GetProperty("configuration");
        var enforcement = parameters.GetProperty("enforcementEnabled").GetBoolean();
        if (mode == SessionMode.SimpleTimer)
        {
            if (durationSeconds is < 60 or > 43200 || durationSeconds % 60 != 0
                || presetValue.ValueKind != JsonValueKind.Null || configurationValue.ValueKind != JsonValueKind.Null) return false;
            strictSession = new(profileId, mode, durationSeconds, null, null, enforcement);
            return true;
        }

        if (durationSeconds != 0) return false;
        var cycleJson = JsonSerializer.Serialize(new
        {
            profileId = profileId.ToString("N"),
            presetId = presetValue,
            configuration = configurationValue,
            enforcementEnabled = enforcement
        });
        using var cycleDocument = JsonDocument.Parse(cycleJson);
        if (!TryParseCycleCommand("session.prepareCycle", cycleDocument.RootElement, out var cycle,
            out _, out _, out _) || cycle is null) return false;
        strictSession = new(profileId, mode, 0, cycle.PresetId, cycle.Configuration, enforcement);
        return true;
    }
}
