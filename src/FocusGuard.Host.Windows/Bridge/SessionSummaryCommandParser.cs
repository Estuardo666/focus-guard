using System.Text.Json;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record CaptureCommand(Guid SessionId, Guid CaptureId, Guid OperationId);
internal sealed record SessionReflectionCommand(Guid SessionId, string Reflection);

internal sealed partial class BridgeRequestParser
{
    private static bool TryParseSessionReflection(string? method, JsonElement parameters, out SessionReflectionCommand? command)
    {
        command = null;
        if (method != "session.reflection.save" || !Exact(parameters, "sessionId", "reflection", "confirmed")
            || !TryGuid(parameters, "sessionId", out var sessionId)
            || parameters.GetProperty("reflection").ValueKind != JsonValueKind.String
            || parameters.GetProperty("reflection").GetString() is not { Length: <= 1000 } reflection
            || parameters.GetProperty("confirmed").ValueKind != JsonValueKind.True) return false;
        command = new(sessionId, reflection);
        return true;
    }

    private static bool TryParseCaptureCommand(string? method, JsonElement parameters, out CaptureCommand? command)
    {
        command = null;
        if (method is not ("capture.open" or "capture.dismiss")
            || !Exact(parameters, "sessionId", "captureId", "operationId", "confirmed")
            || !TryGuid(parameters, "sessionId", out var sessionId)
            || !TryGuid(parameters, "captureId", out var captureId)
            || !TryGuid(parameters, "operationId", out var operationId)
            || parameters.GetProperty("confirmed").ValueKind != JsonValueKind.True) return false;
        command = new(sessionId, captureId, operationId);
        return true;
    }
}
