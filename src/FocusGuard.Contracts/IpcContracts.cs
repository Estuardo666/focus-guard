using System.Text;
using System.Text.Json;

namespace FocusGuard.Contracts;

public enum IpcMessageType
{
    StatusRequest,
    StatusResponse,
    ActivateSessionRequest,
    ActivateSessionResponse,
    StopSessionRequest,
    StopSessionResponse,
    ActivityRequest,
    ActivityResponse,
    CaptureRequest,
    CaptureResponse,
    SessionSummaryRequest,
    SessionSummaryResponse,
    ResolveCapturesRequest,
    ResolveCapturesResponse
}

public sealed record IpcEnvelope(
    int ProtocolVersion,
    IpcMessageType MessageType,
    Guid CorrelationId,
    string PayloadJson);

public sealed record StatusRequest;

public sealed record StatusResponse(
    bool ServiceReady,
    bool ExtensionReady,
    string SessionState,
    long RemainingSeconds,
    Guid? SessionId,
    Guid? ProfileId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc,
    bool StrictMode,
    bool EnforcementConnected);

public sealed record ActivateSessionRequest(
    Guid ProfileId,
    int DurationSeconds,
    bool StrictMode,
    int RecoveryDelaySeconds,
    RecoveryCredential? RecoveryCredential = null,
    IReadOnlyList<ApplicationPolicyRule>? ApplicationRules = null,
    IReadOnlyList<WebsitePolicyRule>? WebsiteRules = null,
    bool EnforcementEnabled = false,
    // Carried at activation rather than looked up later: the service owns no profile
    // store, and the end-of-session summary has to name the profile that just ran.
    string? ProfileName = null);

public sealed record ApplicationPolicyRule(Guid Id, string Action, string MatchKind, string Value);

public sealed record WebsitePolicyRule(Guid Id, string Action, string Pattern);

public sealed record ActivateSessionResponse(
    bool Accepted,
    string Reason,
    Guid? SessionId,
    DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc);

public sealed record StopSessionRequest;

public sealed record StopSessionResponse(bool Accepted, string Reason);

public sealed record IpcValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static IpcValidationResult Success { get; } = new(Array.Empty<string>());
}

public sealed record IpcCodecResult<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static IpcCodecResult<T> Success(T value) => new(value, Array.Empty<string>());

    public static IpcCodecResult<T> Failure(params string[] errors) => new(default, errors);
}

public static class IpcProtocol
{
#if FOCUSGUARD_PRODUCTION
    public const string PipeName = "FocusGuard.Control.v1";
#else
    public const string PipeName = "FocusGuard.Development.Control.v1";
#endif
    public const int CurrentVersion = 1;
    public const int MaxPayloadBytes = 64 * 1024;
    public const int MaxJsonDepth = 16;
    public const int MinimumDurationSeconds = 60;
    public const int MaximumDurationSeconds = 30 * 24 * 60 * 60;
    public const int MaximumRecoveryDelaySeconds = 24 * 60 * 60;
    public const int MaximumPolicyRuleCount = 10_000;
    public const int MaximumRuleValueLength = 2_048;
    public const int MaximumCaptureTextLength = 240;
    public const int MaximumCaptureSourceLength = 1_024;
    public const int MaximumProfileNameLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = MaxJsonDepth,
        WriteIndented = false
    };

    public static IpcValidationResult ValidateEnvelope(IpcEnvelope? envelope)
    {
        var errors = new List<string>();
        if (envelope is null)
        {
            return new IpcValidationResult(["Envelope is required."]);
        }

        if (envelope.ProtocolVersion != CurrentVersion)
        {
            errors.Add("Unsupported IPC protocol version.");
        }

        if (!Enum.IsDefined(envelope.MessageType))
        {
            errors.Add("Unsupported IPC message type.");
        }

        if (envelope.CorrelationId == Guid.Empty)
        {
            errors.Add("Correlation ID is required.");
        }

        if (string.IsNullOrWhiteSpace(envelope.PayloadJson))
        {
            errors.Add("Payload is required.");
        }
        else if (Encoding.UTF8.GetByteCount(envelope.PayloadJson) > MaxPayloadBytes)
        {
            errors.Add("Payload exceeds the maximum size.");
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(
                    envelope.PayloadJson,
                    new JsonDocumentOptions
                    {
                        MaxDepth = MaxJsonDepth,
                        CommentHandling = JsonCommentHandling.Disallow,
                        AllowTrailingCommas = false
                    });

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("Payload root must be a JSON object.");
                }
            }
            catch (JsonException)
            {
                errors.Add("Payload must be valid bounded JSON.");
            }
        }

        return errors.Count == 0 ? IpcValidationResult.Success : new IpcValidationResult(errors);
    }

    public static IpcCodecResult<IpcEnvelope> CreateStatusRequest(Guid correlationId) =>
        CreateEnvelope(IpcMessageType.StatusRequest, correlationId, new StatusRequest());

    public static IpcCodecResult<StatusRequest> ReadStatusRequest(IpcEnvelope envelope) =>
        ReadPayload<StatusRequest>(
            envelope,
            IpcMessageType.StatusRequest,
            []);

    public static IpcCodecResult<IpcEnvelope> CreateStatusResponse(
        Guid correlationId,
        StatusResponse response) =>
        CreateEnvelope(IpcMessageType.StatusResponse, correlationId, response);

    public static IpcCodecResult<StatusResponse> ReadStatusResponse(IpcEnvelope envelope) =>
        ReadPayload<StatusResponse>(
            envelope,
            IpcMessageType.StatusResponse,
            [
                "serviceReady",
                "extensionReady",
                "sessionState",
                "remainingSeconds",
                "sessionId",
                "profileId",
                "startUtc",
                "endUtc",
                "strictMode",
                "enforcementConnected"
            ],
            ValidateStatusResponse);

    public static IpcCodecResult<IpcEnvelope> CreateActivateSessionRequest(
        Guid correlationId,
        ActivateSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ValidateActivateSessionRequest(request);
        return validation.IsValid
            ? CreateEnvelope(IpcMessageType.ActivateSessionRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    public static IpcCodecResult<ActivateSessionRequest> ReadActivateSessionRequest(IpcEnvelope envelope)
    {
        return ReadPayload<ActivateSessionRequest>(
            envelope,
            IpcMessageType.ActivateSessionRequest,
            [
                "profileId",
                "durationSeconds",
                "strictMode",
                "recoveryDelaySeconds",
                "recoveryCredential",
                "applicationRules",
                "websiteRules",
                "enforcementEnabled",
                "profileName"
            ],
            ValidateActivateSessionRequest);
    }

    public static IpcCodecResult<IpcEnvelope> CreateActivateSessionResponse(
        Guid correlationId,
        ActivateSessionResponse response) =>
        CreateEnvelope(IpcMessageType.ActivateSessionResponse, correlationId, response);

    public static IpcCodecResult<ActivateSessionResponse> ReadActivateSessionResponse(IpcEnvelope envelope) =>
        ReadPayload<ActivateSessionResponse>(
            envelope,
            IpcMessageType.ActivateSessionResponse,
            ["accepted", "reason", "sessionId", "startUtc", "endUtc"],
            ValidateActivateSessionResponse);

    public static IpcCodecResult<IpcEnvelope> CreateStopSessionRequest(Guid correlationId) =>
        CreateEnvelope(IpcMessageType.StopSessionRequest, correlationId, new StopSessionRequest());

    public static IpcCodecResult<StopSessionRequest> ReadStopSessionRequest(IpcEnvelope envelope) =>
        ReadPayload<StopSessionRequest>(
            envelope,
            IpcMessageType.StopSessionRequest,
            []);

    public static IpcCodecResult<IpcEnvelope> CreateStopSessionResponse(
        Guid correlationId,
        StopSessionResponse response) =>
        CreateEnvelope(IpcMessageType.StopSessionResponse, correlationId, response);

    public static IpcCodecResult<StopSessionResponse> ReadStopSessionResponse(IpcEnvelope envelope) =>
        ReadPayload<StopSessionResponse>(
            envelope,
            IpcMessageType.StopSessionResponse,
            ["accepted", "reason"],
            ValidateStopSessionResponse);

    public static IpcCodecResult<IpcEnvelope> CreateActivityRequest(
        Guid correlationId,
        ActivityRequest request) =>
        request.MaximumCount is > 0 and <= 1000
            ? CreateEnvelope(IpcMessageType.ActivityRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure("Activity count is outside the supported range.");

    public static IpcCodecResult<ActivityRequest> ReadActivityRequest(IpcEnvelope envelope) =>
        ReadPayload<ActivityRequest>(
            envelope,
            IpcMessageType.ActivityRequest,
            ["maximumCount"],
            request => request is not null && request.MaximumCount is > 0 and <= 1000
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Activity count is outside the supported range."]));

    public static IpcCodecResult<IpcEnvelope> CreateActivityResponse(
        Guid correlationId,
        ActivityResponse response) =>
        CreateEnvelope(IpcMessageType.ActivityResponse, correlationId, response);

    public static IpcCodecResult<ActivityResponse> ReadActivityResponse(IpcEnvelope envelope) =>
        ReadPayload<ActivityResponse>(
            envelope,
            IpcMessageType.ActivityResponse,
            ["events"],
            ValidateActivityResponse);

    public static IpcCodecResult<IpcEnvelope> CreateCaptureRequest(
        Guid correlationId,
        CaptureRequest request)
    {
        var validation = ValidateCaptureRequest(request);
        return validation.IsValid
            ? CreateEnvelope(IpcMessageType.CaptureRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    public static IpcCodecResult<CaptureRequest> ReadCaptureRequest(IpcEnvelope envelope) =>
        ReadPayload<CaptureRequest>(
            envelope,
            IpcMessageType.CaptureRequest,
            ["text", "sourceKind", "sourceValue"],
            ValidateCaptureRequest);

    public static IpcCodecResult<IpcEnvelope> CreateCaptureResponse(
        Guid correlationId,
        CaptureResponse response) =>
        CreateEnvelope(IpcMessageType.CaptureResponse, correlationId, response);

    public static IpcCodecResult<CaptureResponse> ReadCaptureResponse(IpcEnvelope envelope) =>
        ReadPayload<CaptureResponse>(
            envelope,
            IpcMessageType.CaptureResponse,
            ["accepted", "reason", "captureId"],
            response => response is not null
                && !string.IsNullOrWhiteSpace(response.Reason)
                && response.Accepted == (response.CaptureId is { } id && id != Guid.Empty)
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Capture response fields are invalid."]));

    public static IpcCodecResult<IpcEnvelope> CreateSessionSummaryRequest(
        Guid correlationId,
        SessionSummaryRequest request) =>
        CreateEnvelope(IpcMessageType.SessionSummaryRequest, correlationId, request);

    public static IpcCodecResult<SessionSummaryRequest> ReadSessionSummaryRequest(IpcEnvelope envelope) =>
        ReadPayload<SessionSummaryRequest>(
            envelope,
            IpcMessageType.SessionSummaryRequest,
            ["sessionId"]);

    public static IpcCodecResult<IpcEnvelope> CreateSessionSummaryResponse(
        Guid correlationId,
        SessionSummaryResponse response) =>
        CreateEnvelope(IpcMessageType.SessionSummaryResponse, correlationId, response);

    public static IpcCodecResult<SessionSummaryResponse> ReadSessionSummaryResponse(IpcEnvelope envelope) =>
        ReadPayload<SessionSummaryResponse>(
            envelope,
            IpcMessageType.SessionSummaryResponse,
            [
                "available",
                "sessionId",
                "outcome",
                "profileName",
                "plannedMinutes",
                "focusedMinutes",
                "blockedAttempts",
                "streakDays",
                "endedAtUtc",
                "captures"
            ],
            ValidateSessionSummaryResponse);

    public static IpcCodecResult<IpcEnvelope> CreateResolveCapturesRequest(
        Guid correlationId,
        ResolveCapturesRequest request)
    {
        var validation = ValidateResolveCapturesRequest(request);
        return validation.IsValid
            ? CreateEnvelope(IpcMessageType.ResolveCapturesRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    public static IpcCodecResult<ResolveCapturesRequest> ReadResolveCapturesRequest(IpcEnvelope envelope) =>
        ReadPayload<ResolveCapturesRequest>(
            envelope,
            IpcMessageType.ResolveCapturesRequest,
            ["captureIds", "resolution"],
            ValidateResolveCapturesRequest);

    public static IpcCodecResult<IpcEnvelope> CreateResolveCapturesResponse(
        Guid correlationId,
        ResolveCapturesResponse response) =>
        CreateEnvelope(IpcMessageType.ResolveCapturesResponse, correlationId, response);

    public static IpcCodecResult<ResolveCapturesResponse> ReadResolveCapturesResponse(IpcEnvelope envelope) =>
        ReadPayload<ResolveCapturesResponse>(
            envelope,
            IpcMessageType.ResolveCapturesResponse,
            ["accepted", "reason", "resolvedCount"],
            response => response is not null
                && !string.IsNullOrWhiteSpace(response.Reason)
                && response.ResolvedCount >= 0
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Resolve captures response fields are invalid."]));

    public static IpcValidationResult ValidateCaptureRequest(CaptureRequest? request)
    {
        if (request is null)
        {
            return new IpcValidationResult(["Capture request is required."]);
        }

        var errors = new List<string>();
        if (!CaptureProtocol.IsSourceKind(request.SourceKind))
        {
            errors.Add("Capture source kind is invalid.");
        }

        // The source is the one field that could smuggle a full URL with query string
        // into storage, so it is bounded and stripped-checked here as well as in Domain.
        if (string.IsNullOrWhiteSpace(request.SourceValue)
            || request.SourceValue.Length > MaximumCaptureSourceLength)
        {
            errors.Add("Capture source is required and bounded.");
        }
        else if (request.SourceKind == CaptureProtocol.WebsiteKind
            && (request.SourceValue.Contains('?', StringComparison.Ordinal)
                || request.SourceValue.Contains('#', StringComparison.Ordinal)
                || !Uri.TryCreate(request.SourceValue, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(uri.Host)))
        {
            errors.Add("Website captures must record a sanitized HTTP(S) address.");
        }

        if (request.Text is not null && request.Text.Length > MaximumCaptureTextLength)
        {
            errors.Add("Capture text exceeds the maximum length.");
        }

        return errors.Count == 0 ? IpcValidationResult.Success : new IpcValidationResult(errors);
    }

    private static IpcValidationResult ValidateResolveCapturesRequest(ResolveCapturesRequest? request)
    {
        if (request is null
            || !CaptureProtocol.IsTerminalResolution(request.Resolution)
            || request.CaptureIds is null
            || request.CaptureIds.Count is 0 or > CaptureProtocol.MaximumCapturesPerSession
            || request.CaptureIds.Any(id => id == Guid.Empty)
            || request.CaptureIds.Distinct().Count() != request.CaptureIds.Count)
        {
            return new IpcValidationResult(["Resolve captures request is invalid."]);
        }

        return IpcValidationResult.Success;
    }

    private static IpcValidationResult ValidateSessionSummaryResponse(SessionSummaryResponse? response)
    {
        if (response is null
            || response.Captures is null
            || response.Captures.Count > CaptureProtocol.MaximumCapturesPerSession
            || response.PlannedMinutes < 0
            || response.FocusedMinutes < 0
            || response.BlockedAttempts < 0
            || response.StreakDays < 0
            || (response.EndedAtUtc is not null && response.EndedAtUtc.Value.Offset != TimeSpan.Zero))
        {
            return new IpcValidationResult(["Session summary response fields are invalid."]);
        }

        // An unavailable summary carries no session; an available one must be complete.
        if (response.Available
            && (response.SessionId is not { } sessionId
                || sessionId == Guid.Empty
                || !CaptureProtocol.IsOutcome(response.Outcome)
                || string.IsNullOrWhiteSpace(response.ProfileName)))
        {
            return new IpcValidationResult(["Available session summary is incomplete."]);
        }

        foreach (var capture in response.Captures)
        {
            if (capture is null
                || capture.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(capture.Text)
                || capture.Text.Length > MaximumCaptureTextLength
                || !CaptureProtocol.IsSourceKind(capture.SourceKind)
                || string.IsNullOrWhiteSpace(capture.SourceValue)
                || capture.SourceValue.Length > MaximumCaptureSourceLength
                || string.IsNullOrWhiteSpace(capture.SourceLabel)
                || capture.CapturedAtUtc.Offset != TimeSpan.Zero
                || capture.Resolution is not (CaptureProtocol.PendingResolution
                    or CaptureProtocol.OpenedResolution
                    or CaptureProtocol.DismissedResolution))
            {
                return new IpcValidationResult(["Session summary contains an invalid capture."]);
            }
        }

        return IpcValidationResult.Success;
    }

    public static byte[] WriteEnvelope(IpcEnvelope envelope)
    {
        var validation = ValidateEnvelope(envelope);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (payload.Length is 0 or > MaxPayloadBytes)
        {
            throw new InvalidDataException("IPC envelope length is outside the supported range.");
        }

        return payload;
    }

    public static IpcCodecResult<IpcEnvelope> ReadEnvelope(ReadOnlySpan<byte> payload)
    {
        var shape = ValidateJsonShape(
            payload,
            ["protocolVersion", "messageType", "correlationId", "payloadJson"]);
        if (!shape.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. shape.Errors]);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<IpcEnvelope>(payload, JsonOptions);
            var validation = ValidateEnvelope(envelope);
            return validation.IsValid
                ? IpcCodecResult<IpcEnvelope>.Success(envelope!)
                : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
        }
        catch (JsonException)
        {
            return IpcCodecResult<IpcEnvelope>.Failure("IPC envelope is invalid.");
        }
    }

    public static IpcValidationResult ValidateActivateSessionRequest(ActivateSessionRequest? request)
    {
        if (request is null)
        {
            return new IpcValidationResult(["Activation request is required."]);
        }

        var errors = new List<string>();
        if (request.ProfileId == Guid.Empty)
        {
            errors.Add("Profile ID is required.");
        }

        if (request.DurationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
        {
            errors.Add("Activation duration must be between one minute and 30 days.");
        }

        if (request.RecoveryDelaySeconds < 0 || request.RecoveryDelaySeconds > MaximumRecoveryDelaySeconds)
        {
            errors.Add("Recovery delay is outside the supported range.");
        }

        if (!request.StrictMode && request.RecoveryDelaySeconds != 0)
        {
            errors.Add("Normal activation cannot include a recovery delay.");
        }

        if (!request.StrictMode && request.RecoveryCredential is not null)
        {
            errors.Add("Normal activation cannot include a recovery credential.");
        }

        if (request.StrictMode
            && (request.RecoveryDelaySeconds < 60
                || !RecoveryCodeService.IsValid(request.RecoveryCredential)))
        {
            errors.Add("Strict activation requires a valid recovery credential and a delay of at least one minute.");
        }

        if (request.ProfileName is { Length: > MaximumProfileNameLength })
        {
            errors.Add("Profile name exceeds the maximum length.");
        }

        ValidatePolicyRules(request, errors);

        return errors.Count == 0 ? IpcValidationResult.Success : new IpcValidationResult(errors);
    }

    private static void ValidatePolicyRules(ActivateSessionRequest request, List<string> errors)
    {
        var applicationRules = request.ApplicationRules ?? [];
        var websiteRules = request.WebsiteRules ?? [];
        if (applicationRules.Count > MaximumPolicyRuleCount
            || websiteRules.Count > MaximumPolicyRuleCount)
        {
            errors.Add("Policy contains too many rules.");
            return;
        }

        var ruleIds = new HashSet<Guid>();
        foreach (var rule in applicationRules)
        {
            if (rule is null
                || rule.Id == Guid.Empty
                || !ruleIds.Add(rule.Id)
                || (rule.Action is not "Allow" and not "Block")
                || (rule.MatchKind is not "ExactPath" and not "Folder" and not "Sha256" and not "FileName")
                || string.IsNullOrWhiteSpace(rule.Value)
                || rule.Value.Length > MaximumRuleValueLength)
            {
                errors.Add("Application policy rule is invalid or duplicated.");
                break;
            }
        }

        foreach (var rule in websiteRules)
        {
            if (rule is null
                || rule.Id == Guid.Empty
                || !ruleIds.Add(rule.Id)
                || (rule.Action is not "Allow" and not "Block")
                || string.IsNullOrWhiteSpace(rule.Pattern)
                || rule.Pattern.Length > MaximumRuleValueLength
                || rule.Pattern.Contains('?', StringComparison.Ordinal))
            {
                errors.Add("Website policy rule is invalid or duplicated.");
                break;
            }
        }
    }

    private static IpcCodecResult<IpcEnvelope> CreateEnvelope<T>(IpcMessageType messageType, Guid correlationId, T payload)
    {
        if (correlationId == Guid.Empty)
        {
            return IpcCodecResult<IpcEnvelope>.Failure("Correlation ID is required.");
        }

        var envelope = new IpcEnvelope(CurrentVersion, messageType, correlationId, JsonSerializer.Serialize(payload, JsonOptions));
        var validation = ValidateEnvelope(envelope);
        return validation.IsValid
            ? IpcCodecResult<IpcEnvelope>.Success(envelope)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    private static IpcValidationResult ValidateExpectedEnvelope(IpcEnvelope envelope, IpcMessageType expectedType)
    {
        var validation = ValidateEnvelope(envelope);
        if (!validation.IsValid)
        {
            return validation;
        }

        return envelope.MessageType == expectedType
            ? IpcValidationResult.Success
            : new IpcValidationResult(["IPC message type does not match the expected contract."]);
    }

    private static IpcCodecResult<T> ReadPayload<T>(
        IpcEnvelope envelope,
        IpcMessageType expectedType,
        string[] expectedProperties,
        Func<T?, IpcValidationResult>? validate = null)
    {
        var envelopeValidation = ValidateExpectedEnvelope(envelope, expectedType);
        if (!envelopeValidation.IsValid)
        {
            return IpcCodecResult<T>.Failure([.. envelopeValidation.Errors]);
        }

        var bytes = Encoding.UTF8.GetBytes(envelope.PayloadJson);
        var shape = ValidateJsonShape(bytes, expectedProperties);
        if (!shape.IsValid)
        {
            return IpcCodecResult<T>.Failure([.. shape.Errors]);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(envelope.PayloadJson, JsonOptions);
            if (value is null)
            {
                return IpcCodecResult<T>.Failure("IPC payload is required.");
            }

            var validation = validate?.Invoke(value) ?? IpcValidationResult.Success;
            return validation.IsValid
                ? IpcCodecResult<T>.Success(value)
                : IpcCodecResult<T>.Failure([.. validation.Errors]);
        }
        catch (JsonException)
        {
            return IpcCodecResult<T>.Failure("IPC payload is invalid.");
        }
    }

    private static IpcValidationResult ValidateStatusResponse(StatusResponse? response)
    {
        if (response is null
            || string.IsNullOrWhiteSpace(response.SessionState)
            || response.RemainingSeconds < 0
            || (response.StartUtc is not null && response.StartUtc.Value.Offset != TimeSpan.Zero)
            || (response.EndUtc is not null && response.EndUtc.Value.Offset != TimeSpan.Zero)
            || (response.SessionId.HasValue != response.ProfileId.HasValue)
            || (response.SessionId.HasValue != response.StartUtc.HasValue)
            || (response.SessionId.HasValue != response.EndUtc.HasValue))
        {
            return new IpcValidationResult(["Status response fields are invalid."]);
        }

        return IpcValidationResult.Success;
    }

    private static IpcValidationResult ValidateActivateSessionResponse(ActivateSessionResponse? response)
    {
        if (response is null
            || string.IsNullOrWhiteSpace(response.Reason)
            || (response.Accepted
                && (response.SessionId is null
                    || response.SessionId == Guid.Empty
                    || response.StartUtc?.Offset != TimeSpan.Zero
                    || response.EndUtc?.Offset != TimeSpan.Zero
                    || response.EndUtc <= response.StartUtc)))
        {
            return new IpcValidationResult(["Activation response fields are invalid."]);
        }

        return IpcValidationResult.Success;
    }

    private static IpcValidationResult ValidateStopSessionResponse(StopSessionResponse? response) =>
        response is not null && !string.IsNullOrWhiteSpace(response.Reason)
            ? IpcValidationResult.Success
            : new IpcValidationResult(["Stop response fields are invalid."]);

    private static IpcValidationResult ValidateActivityResponse(ActivityResponse? response)
    {
        if (response is null || response.Events.Count > 1000)
        {
            return new IpcValidationResult(["Activity response fields are invalid."]);
        }

        foreach (var item in response.Events)
        {
            if (item is null
                || item.Id == Guid.Empty
                || item.TimestampUtc.Offset != TimeSpan.Zero
                || !Enum.IsDefined(item.Kind)
                || string.IsNullOrWhiteSpace(item.Target)
                || item.Target.Length > 512
                || item.Target.Contains('?', StringComparison.Ordinal)
                || item.Target.Contains('#', StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(item.Outcome)
                || item.Outcome.Length > 80)
            {
                return new IpcValidationResult(["Activity response contains an invalid event."]);
            }
        }

        return IpcValidationResult.Success;
    }

    private static IpcValidationResult ValidateJsonShape(
        ReadOnlySpan<byte> payload,
        string[] expectedProperties)
    {
        if (payload.Length is 0 or > MaxPayloadBytes)
        {
            return new IpcValidationResult(["IPC JSON length is outside the supported range."]);
        }

        try
        {
            using var document = JsonDocument.Parse(
                payload.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxJsonDepth
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new IpcValidationResult(["IPC JSON root must be an object."]);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name) || !expectedProperties.Contains(property.Name))
                {
                    return new IpcValidationResult(["IPC JSON shape is invalid."]);
                }
            }

            return expectedProperties.All(names.Contains)
                ? IpcValidationResult.Success
                : new IpcValidationResult(["IPC JSON is missing required properties."]);
        }
        catch (JsonException)
        {
            return new IpcValidationResult(["IPC JSON must be valid bounded JSON."]);
        }
    }
}
