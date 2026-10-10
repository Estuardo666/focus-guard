using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Domain;

namespace FocusGuard.Contracts;

public static class CycleIpcProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = IpcProtocol.MaxJsonDepth,
        WriteIndented = false
    };

    public static IpcCodecResult<IpcEnvelope> CreatePrepareRequest(
        Guid correlationId,
        PrepareSessionRequest request)
    {
        var validation = ValidatePrepareRequest(request);
        return validation.IsValid
            ? CreateEnvelope(IpcMessageType.PrepareSessionRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    public static IpcCodecResult<PrepareSessionRequest> ReadPrepareRequest(IpcEnvelope envelope) =>
        ReadPayload<PrepareSessionRequest>(
            envelope,
            IpcMessageType.PrepareSessionRequest,
            [
                "profileId", "profileName", "mode", "durationSeconds", "cycleConfiguration",
                "strictMode", "recoveryDelaySeconds", "applicationRules", "websiteRules",
                "enforcementEnabled", "breakApplicationRules", "breakWebsiteRules"
            ],
            ValidatePrepareRequest);

    public static IpcCodecResult<IpcEnvelope> CreatePrepareResponse(
        Guid correlationId,
        PrepareSessionResponse response) =>
        CreateEnvelope(IpcMessageType.PrepareSessionResponse, correlationId, response);

    public static IpcCodecResult<PrepareSessionResponse> ReadPrepareResponse(IpcEnvelope envelope) =>
        ReadPayload<PrepareSessionResponse>(
            envelope,
            IpcMessageType.PrepareSessionResponse,
            [
                "accepted", "reason", "preparationId", "startUtc", "endUtc", "expiresUtc", "mode", "phases"
            ],
            ValidatePrepareResponse);

    public static IpcCodecResult<IpcEnvelope> CreateActivatePreparedRequest(
        Guid correlationId,
        ActivatePreparedSessionRequest request) =>
        CreateEnvelope(IpcMessageType.ActivatePreparedSessionRequest, correlationId, request);

    public static IpcCodecResult<ActivatePreparedSessionRequest> ReadActivatePreparedRequest(IpcEnvelope envelope) =>
        ReadPayload<ActivatePreparedSessionRequest>(
            envelope,
            IpcMessageType.ActivatePreparedSessionRequest,
            ["preparationId", "recoveryCredential"],
            request => request is not null && request.PreparationId != Guid.Empty
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Prepared-session activation request is invalid."]));

    public static IpcCodecResult<IpcEnvelope> CreateActivatePreparedResponse(
        Guid correlationId,
        ActivatePreparedSessionResponse response) =>
        CreateEnvelope(IpcMessageType.ActivatePreparedSessionResponse, correlationId, response);

    public static IpcCodecResult<ActivatePreparedSessionResponse> ReadActivatePreparedResponse(IpcEnvelope envelope) =>
        ReadPayload<ActivatePreparedSessionResponse>(
            envelope,
            IpcMessageType.ActivatePreparedSessionResponse,
            ["accepted", "reason", "sessionId", "startUtc", "endUtc", "mode"],
            response => response is not null && !string.IsNullOrWhiteSpace(response.Reason)
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Prepared-session activation response is invalid."]));

    public static IpcCodecResult<IpcEnvelope> CreateSkipBreakRequest(
        Guid correlationId,
        SkipBreakRequest request)
    {
        var validation = ValidateSkipBreakRequest(request);
        return validation.IsValid
            ? CreateEnvelope(IpcMessageType.SkipBreakRequest, correlationId, request)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    public static IpcCodecResult<SkipBreakRequest> ReadSkipBreakRequest(IpcEnvelope envelope) =>
        ReadPayload<SkipBreakRequest>(
            envelope,
            IpcMessageType.SkipBreakRequest,
            ["phaseIndex"],
            ValidateSkipBreakRequest);

    public static IpcCodecResult<IpcEnvelope> CreateSkipBreakResponse(
        Guid correlationId,
        SkipBreakResponse response) =>
        CreateEnvelope(IpcMessageType.SkipBreakResponse, correlationId, response);

    public static IpcCodecResult<SkipBreakResponse> ReadSkipBreakResponse(IpcEnvelope envelope) =>
        ReadPayload<SkipBreakResponse>(
            envelope,
            IpcMessageType.SkipBreakResponse,
            ["accepted", "reason", "phaseIndex", "currentPhase", "sessionEndUtc"],
            response => response is not null && !string.IsNullOrWhiteSpace(response.Reason)
                ? IpcValidationResult.Success
                : new IpcValidationResult(["Skip-break response is invalid."]));

    public static IpcValidationResult ValidatePrepareRequest(PrepareSessionRequest? request)
    {
        if (request is null || request.ProfileId == Guid.Empty || !Enum.IsDefined(request.Mode))
        {
            return new IpcValidationResult(["Prepare-session request identity is invalid."]);
        }

        var errors = new List<string>();
        if (request.ProfileName is { Length: > IpcProtocol.MaximumProfileNameLength })
        {
            errors.Add("Profile name exceeds the maximum length.");
        }

        if (request.StrictMode
            ? request.RecoveryDelaySeconds is < 0 or > IpcProtocol.MaximumRecoveryDelaySeconds
            : request.RecoveryDelaySeconds != 0)
        {
            errors.Add("Recovery delay is invalid for the selected strictness.");
        }

        if (request.Mode == SessionMode.SimpleTimer)
        {
            if (request.DurationSeconds is < IpcProtocol.MinimumDurationSeconds or > IpcProtocol.MaximumDurationSeconds)
            {
                errors.Add("Simple-session duration is outside the supported range.");
            }

            if (request.CycleConfiguration is not null)
            {
                errors.Add("Simple sessions cannot include a cycle configuration.");
            }
        }
        else if (request.CycleConfiguration is null)
        {
            errors.Add("Focus-cycle sessions require a cycle configuration.");
        }
        else if (!TryBuildConfiguration(request.CycleConfiguration, out _, out var configurationErrors))
        {
            errors.AddRange(configurationErrors);
        }

        ValidateRules(request.ApplicationRules, request.WebsiteRules, errors);
        ValidateRules(request.BreakApplicationRules, request.BreakWebsiteRules, errors, breakRules: true);
        return errors.Count == 0 ? IpcValidationResult.Success : new IpcValidationResult(errors);
    }

    private static IpcValidationResult ValidateSkipBreakRequest(SkipBreakRequest? request) =>
        request is not null && request.PhaseIndex >= 0
            ? IpcValidationResult.Success
            : new IpcValidationResult(["Skip-break phase index is invalid."]);

    private static IpcValidationResult ValidatePrepareResponse(PrepareSessionResponse? response)
    {
        if (response is null
            || string.IsNullOrWhiteSpace(response.Reason)
            || !Enum.IsDefined(response.Mode)
            || response.Phases is null
            || response.Phases.Count > FocusCycleConfiguration.MaximumCycleCount * 2 + 1)
        {
            return new IpcValidationResult(["Prepared-session response is invalid."]);
        }

        if (!response.Accepted)
        {
            return IpcValidationResult.Success;
        }

        if (response.PreparationId is not { } preparationId
            || preparationId == Guid.Empty
            || response.StartUtc?.Offset != TimeSpan.Zero
            || response.EndUtc?.Offset != TimeSpan.Zero
            || response.ExpiresUtc?.Offset != TimeSpan.Zero
            || response.EndUtc <= response.StartUtc
            || response.ExpiresUtc <= response.StartUtc)
        {
            return new IpcValidationResult(["Accepted prepared-session response has invalid timestamps."]);
        }

        return IpcValidationResult.Success;
    }

    public static bool TryBuildConfiguration(
        FocusCycleConfigurationContract contract,
        out FocusCycleConfiguration? configuration,
        out IReadOnlyList<string> errors)
    {
        configuration = null;
        var validation = new List<string>();
        if (contract.FocusSeconds <= 0
            || contract.ShortBreakSeconds <= 0
            || contract.LongBreakSeconds <= 0
            || contract.TotalDurationSeconds is <= 0)
        {
            validation.Add("Cycle durations must be positive.");
        }

        if (validation.Count == 0)
        {
            var result = FocusCycleConfiguration.Create(
                TimeSpan.FromSeconds(contract.FocusSeconds),
                TimeSpan.FromSeconds(contract.ShortBreakSeconds),
                TimeSpan.FromSeconds(contract.LongBreakSeconds),
                contract.CyclesBeforeLongBreak,
                contract.TerminationMode,
                contract.CycleCount,
                contract.TotalDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
            if (result.IsValid)
            {
                configuration = result.Value;
            }
            else
            {
                validation.AddRange(result.Errors.Select(error => error.Message));
            }
        }

        errors = validation;
        return validation.Count == 0;
    }

    private static void ValidateRules(
        IReadOnlyList<ApplicationPolicyRule>? applicationRules,
        IReadOnlyList<WebsitePolicyRule>? websiteRules,
        ICollection<string> errors,
        bool breakRules = false)
    {
        if ((applicationRules?.Count ?? 0) + (websiteRules?.Count ?? 0) > IpcProtocol.MaximumPolicyRuleCount)
        {
            errors.Add("Policy rule count exceeds the maximum.");
        }

        var ids = new HashSet<Guid>();
        foreach (var rule in applicationRules ?? [])
        {
            if (rule is null
                || rule.Id == Guid.Empty
                || !ids.Add(rule.Id)
                || (breakRules ? rule.Action is not "Block" : rule.Action is not ("Allow" or "Block"))
                || rule.MatchKind is not ("ExactPath" or "Folder" or "Sha256" or "FileName")
                || string.IsNullOrWhiteSpace(rule.Value)
                || rule.Value.Length > IpcProtocol.MaximumRuleValueLength)
            {
                errors.Add("Application policy rule is invalid or duplicated.");
                break;
            }
        }

        foreach (var rule in websiteRules ?? [])
        {
            if (rule is null
                || rule.Id == Guid.Empty
                || !ids.Add(rule.Id)
                || (breakRules ? rule.Action is not "Block" : rule.Action is not ("Allow" or "Block"))
                || string.IsNullOrWhiteSpace(rule.Pattern)
                || rule.Pattern.Length > IpcProtocol.MaximumRuleValueLength
                || rule.Pattern.Contains('?', StringComparison.Ordinal))
            {
                errors.Add("Website policy rule is invalid or duplicated.");
                break;
            }
        }
    }

    private static IpcCodecResult<IpcEnvelope> CreateEnvelope<T>(
        IpcMessageType messageType,
        Guid correlationId,
        T payload)
    {
        if (correlationId == Guid.Empty)
        {
            return IpcCodecResult<IpcEnvelope>.Failure("Correlation ID is required.");
        }

        var envelope = new IpcEnvelope(
            IpcProtocol.CurrentVersion,
            messageType,
            correlationId,
            JsonSerializer.Serialize(payload, JsonOptions));
        var validation = IpcProtocol.ValidateEnvelope(envelope);
        return validation.IsValid
            ? IpcCodecResult<IpcEnvelope>.Success(envelope)
            : IpcCodecResult<IpcEnvelope>.Failure([.. validation.Errors]);
    }

    private static IpcCodecResult<T> ReadPayload<T>(
        IpcEnvelope envelope,
        IpcMessageType expectedType,
        string[] expectedProperties,
        Func<T?, IpcValidationResult> validate)
    {
        var envelopeValidation = IpcProtocol.ValidateEnvelope(envelope);
        if (!envelopeValidation.IsValid || envelope.MessageType != expectedType)
        {
            return IpcCodecResult<T>.Failure(
                [.. envelopeValidation.Errors, "IPC message type does not match the expected contract."]);
        }

        try
        {
            using var document = JsonDocument.Parse(envelope.PayloadJson, new JsonDocumentOptions { MaxDepth = IpcProtocol.MaxJsonDepth });
            var properties = new HashSet<string>(StringComparer.Ordinal);
            var propertyCount = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                propertyCount++;
                if (!properties.Add(property.Name))
                {
                    return IpcCodecResult<T>.Failure("IPC JSON shape is invalid.");
                }
            }

            if (propertyCount != expectedProperties.Length
                || properties.Count != expectedProperties.Length
                || expectedProperties.Any(property => !properties.Contains(property)))
            {
                return IpcCodecResult<T>.Failure("IPC JSON shape is invalid.");
            }

            var value = JsonSerializer.Deserialize<T>(envelope.PayloadJson, JsonOptions);
            var validation = validate(value);
            return validation.IsValid
                ? IpcCodecResult<T>.Success(value!)
                : IpcCodecResult<T>.Failure([.. validation.Errors]);
        }
        catch (JsonException)
        {
            return IpcCodecResult<T>.Failure("IPC payload is invalid or contains an unknown field.");
        }
    }
}
