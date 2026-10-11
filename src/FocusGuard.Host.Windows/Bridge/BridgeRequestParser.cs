using System.Text;
using System.Text.Json;
using System.Globalization;
using FocusGuard.Contracts;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed partial class BridgeRequestParser
{
    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "version",
        "requestId",
        "documentToken",
        "method",
        "params"
    };

    public bool TryParse(string json, out BridgeRequest? request, out string errorCode)
    {
        request = null;
        errorCode = "InvalidRequest";
        if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > BridgeLimits.MaximumRequestBytes)
        {
            errorCode = "RequestTooLarge";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root, RootProperties))
            {
                return false;
            }

            if (!root.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var parsedVersion)
                || parsedVersion != BridgeLimits.ProtocolVersion)
            {
                errorCode = "UnsupportedVersion";
                return false;
            }

            if (!TryGuid(root, "requestId", out var requestId)
                || !TryDocumentToken(root, out var token)
                || !root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object)
            {
                errorCode = "MethodOrParametersRejected";
                return false;
            }

            var method = methodElement.GetString();
            if (method == "status.get" && HasExactProperties(parameters, EmptyProperties))
                request = new(requestId, token, method);
            else if (method == "profiles.list" && HasExactProperties(parameters, ProfileProperties)
                && parameters.GetProperty("offset").TryGetInt32(out var offset) && offset is >= 0 and <= 10000)
                request = new(requestId, token, method, offset);
            else if (method == "applications.list" && (Exact(parameters, "query", "offset")
                || Exact(parameters, "query", "offset", "refresh") && parameters.GetProperty("refresh").ValueKind is JsonValueKind.True or JsonValueKind.False)
                && parameters.GetProperty("query").ValueKind == JsonValueKind.String
                && parameters.GetProperty("query").GetString() is { Length: <= 80 } query
                && parameters.GetProperty("offset").TryGetInt32(out var catalogOffset) && catalogOffset is >= 0 and <= 2000)
                request = new(requestId, token, method, catalogOffset, CatalogQuery: query,
                    CatalogRefresh: parameters.TryGetProperty("refresh", out var refresh) && refresh.GetBoolean());
            else if (method == "session.prepareNormal" && HasExactProperties(parameters, NormalProperties)
                && TryGuid(parameters, "profileId", out var profileId)
                && parameters.GetProperty("durationSeconds").TryGetInt32(out var duration)
                && duration is >= 60 and <= 43200 && duration % 60 == 0
                && parameters.GetProperty("enforcementEnabled").ValueKind is JsonValueKind.True or JsonValueKind.False)
                request = new(requestId, token, method, NormalTimer: new(profileId, duration,
                    parameters.GetProperty("enforcementEnabled").GetBoolean()));
            else if (method == "session.startNormal" && HasExactProperties(parameters, StartProperties)
                && TryGuid(parameters, "preparationId", out var preparationId)
                && TryGuid(parameters, "operationId", out var operationId)
                && TryBoundedText(parameters, "objective", 240, out var objective))
                request = new(requestId, token, method, PreparationId: preparationId, OperationId: operationId, Objective: objective);
            else if (method == "session.startCycle" && Exact(parameters, "preparationId", "operationId", "objective")
                && TryGuid(parameters, "preparationId", out var cyclePreparationId)
                && TryGuid(parameters, "operationId", out var cycleOperationId)
                && TryBoundedText(parameters, "objective", 240, out var cycleObjective))
                request = new(requestId, token, method, PreparationId: cyclePreparationId,
                    OperationId: cycleOperationId, Objective: cycleObjective);
            else if (TryParseProfileCommand(method, parameters, out var profileCommand))
                request = new(requestId, token, method!, OperationId: profileCommand!.OperationId, ProfileCommand: profileCommand);
            else if (TryParseCyclePresetCommand(method, parameters, out var presetCommand))
                request = new(requestId, token, method!, OperationId: presetCommand!.OperationId, CyclePreset: presetCommand);
            else if (method == "schedules.list" && HasExactProperties(parameters, EmptyProperties))
                request = new(requestId, token, method!);
            else if (method == "today.get" && TryParseToday(parameters, out var today))
                request = new(requestId, token, method!, Today: today);
            else if (TryParseScheduleCommand(method, parameters, out var schedule))
                request = new(requestId, token, method!, OperationId: schedule!.OperationId, Schedule: schedule);
            else if (TryParseScheduleCommand(method, parameters, out var scheduleCommand))
                request = new(requestId, token, method!, OperationId: scheduleCommand!.OperationId, Schedule: scheduleCommand);
            else if (method == "session.summary" && Exact(parameters, "sessionId") && TryGuid(parameters, "sessionId", out var summaryId))
                request = new(requestId, token, method!, SummarySessionId: summaryId);
            else if (method == "session.intent.get" && Exact(parameters, "sessionId") && TryGuid(parameters, "sessionId", out var sessionIntentId))
                request = new(requestId, token, method!, SessionIntentId: sessionIntentId);
            else if (TryParseSessionReflection(method, parameters, out var sessionReflection))
                request = new(requestId, token, method!, SessionReflection: sessionReflection);
            else if (TryParseCaptureCommand(method, parameters, out var captureCommand))
                request = new(requestId, token, method!, OperationId: captureCommand!.OperationId, CaptureAction: captureCommand);
            else if (TryParsePreferenceCommand(method, parameters, out var preferences))
                request = new(requestId, token, method!, Preferences: preferences);
            else if (TryParseAppCommand(method, parameters, out var appCommand))
                request = new(requestId, token, method!, AppCommand: appCommand);
            else if (TryParseWindowCommand(method, parameters, out var windowCommand))
                request = new(requestId, token, method!, WindowCommand: windowCommand);
            else if (TryParseStrictSessionCommand(method, parameters, out var strictSession, out var strictActivation,
                out var strictCancel, out var strictSaveCode, out var recoveryInfo))
                request = new(requestId, token, method!, StrictSession: strictSession,
                    StrictActivation: strictActivation, StrictCancelPreparationId: strictCancel,
                    StrictSaveRecoveryCodeId: strictSaveCode, RecoveryInfo: recoveryInfo);

            else if (TryParseActiveSession(method, parameters, out var activeSession, out var activeOperation))
                request = new(requestId, token, method!, OperationId: activeOperation, ActiveSession: activeSession);
            else if (TryParseCycleCommand(method, parameters, out var cycle, out var cyclePreparation, out var cycleOperation, out var cycleOffset))
                request = new(requestId, token, method!, cycleOffset, PreparationId: cyclePreparation, OperationId: cycleOperation, Cycle: cycle);
            else
            {
                errorCode = "MethodOrParametersRejected";
                return false;
            }
            errorCode = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static readonly HashSet<string> EmptyProperties = new(StringComparer.Ordinal);

/// <summary>Sections the shell may open, matching the React navigation keys; anything else is rejected.</summary>
    internal static readonly HashSet<string> AppSections = new(StringComparer.Ordinal)
    {
        "summary", "profiles", "activity", "schedules", "preferences", "connection", "help"
    };

    private static bool TryParseAppCommand(string? method, JsonElement parameters, out HostAppCommand? command)
    {
        command = null;
        switch (method)
        {
            case "app.compact" when HasExactProperties(parameters, EmptyProperties):
                command = new(HostAppAction.Compact);
                return true;
            case "app.quit" when HasExactProperties(parameters, EmptyProperties):
                command = new(HostAppAction.Quit);
                return true;
            case "surface.close" when HasExactProperties(parameters, EmptyProperties):
                command = new(HostAppAction.CloseSurface);
                return true;
            case "surface.ready" when HasExactProperties(parameters, EmptyProperties):
                command = new(HostAppAction.SurfaceReady);
                return true;
            case "app.startup.get" when HasExactProperties(parameters, EmptyProperties):
                command = new(HostAppAction.StartupGet);
                return true;
            case "app.startup.set" when Exact(parameters, "enabled")
                && parameters.GetProperty("enabled").ValueKind is JsonValueKind.True or JsonValueKind.False:
                command = new(HostAppAction.StartupSet, Enabled: parameters.GetProperty("enabled").GetBoolean());
                return true;
            case "app.open" when Exact(parameters, "section")
                && parameters.GetProperty("section").ValueKind == JsonValueKind.String
                && AppSections.Contains(parameters.GetProperty("section").GetString()!):
                command = new(HostAppAction.Open, parameters.GetProperty("section").GetString());
                return true;
            case "app.open" when Exact(parameters, "section", "sessionId")
                && parameters.GetProperty("section").ValueKind == JsonValueKind.String
                && parameters.GetProperty("section").GetString() == "activity"
                && TryGuid(parameters, "sessionId", out var sessionId):
                command = new(HostAppAction.Open, "activity", sessionId);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseWindowCommand(string? method, JsonElement parameters, out HostWindowCommand? command)
    {
        command = null;
        if (method is not ("window.compact.set" or "window.alwaysOnTop.set" or "window.compactDetails.set")
            || !HasExactProperties(parameters, new HashSet<string>(StringComparer.Ordinal) { "enabled" })
            || parameters.GetProperty("enabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        var enabled = parameters.GetProperty("enabled").GetBoolean();
        command = method switch
        {
            "window.compact.set" => new HostWindowCommand(Compact: enabled),
            "window.alwaysOnTop.set" => new HostWindowCommand(AlwaysOnTop: enabled),
            _ => new HostWindowCommand(DetailsExpanded: enabled)
        };
        return true;
    }

    private static bool TryParseToday(JsonElement parameters, out TodayRequest? today)
    {
        today = null;
        if (!HasExactProperties(parameters, new HashSet<string>(["startUtc", "endUtc"], StringComparer.Ordinal))
            || parameters.GetProperty("startUtc").ValueKind != JsonValueKind.String
            || parameters.GetProperty("endUtc").ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(parameters.GetProperty("startUtc").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
            || !DateTimeOffset.TryParse(parameters.GetProperty("endUtc").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)) return false;
        var candidate = new TodayRequest(start, end);
        if (!IpcProtocol.CreateTodayRequest(Guid.NewGuid(), candidate).IsValid) return false;
        today = candidate; return true;
    }

    private static bool TryParseActiveSession(string? method, JsonElement parameters, out ActiveSessionParameters? session, out Guid operation)
    {
        session = null; operation = Guid.Empty;
        if (method is not ("session.stopNormal" or "session.skipBreak")
            || !Exact(parameters, method == "session.stopNormal" ? ["sessionId", "operationId", "confirmed"] : ["sessionId", "operationId", "confirmed", "phaseIndex"])
            || !TryGuid(parameters, "sessionId", out var id) || !TryGuid(parameters, "operationId", out operation)
            || parameters.GetProperty("confirmed").ValueKind != JsonValueKind.True) return false;
        int? phase = null;
        if (method == "session.skipBreak")
        {
            if (!parameters.GetProperty("phaseIndex").TryGetInt32(out var index) || index is < 0 or > 48) return false;
            phase = index;
        }
        session = new(id, phase); return true;
    }
    private static readonly HashSet<string> ProfileProperties = new(StringComparer.Ordinal) { "offset" };
    private static readonly HashSet<string> NormalProperties = new(StringComparer.Ordinal)
        { "profileId", "durationSeconds", "enforcementEnabled" };
    private static readonly HashSet<string> StartProperties = new(StringComparer.Ordinal)
        { "preparationId", "operationId", "objective" };

    private static bool TryBoundedText(JsonElement root, string propertyName, int maximum, out string text)
    {
        text = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String
            || property.GetString() is not { } value || value.Length > maximum) return false;
        text = value;
        return true;
    }

    private static bool TryGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParseExact(property.GetString(), "N", out value)
            && value != Guid.Empty;
    }

    private static bool TryDocumentToken(JsonElement root, out string token)
    {
        token = string.Empty;
        if (root.TryGetProperty("documentToken", out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { Length: 32 } candidate
            && Guid.TryParseExact(candidate, "N", out var parsed)
            && parsed != Guid.Empty)
        {
            token = candidate;
            return true;
        }

        return false;
    }

    private static bool HasExactProperties(JsonElement element, HashSet<string> expected)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !expected.Contains(property.Name))
            {
                return false;
            }
        }

        return seen.Count == expected.Count;
    }
}
