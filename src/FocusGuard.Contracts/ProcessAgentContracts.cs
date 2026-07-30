using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Domain;

namespace FocusGuard.Contracts;

public sealed record ProcessAgentRequest(
    int ProtocolVersion,
    string Type,
    Guid CorrelationId,
    Guid AgentInstanceId,
    DateTimeOffset SentAtUtc,
    Guid? AcknowledgedPolicyId = null,
    string? AcknowledgedPolicyHash = null,
    string? BlockedExecutable = null,
    string? Outcome = null);

public sealed record ProcessAgentResponse(
    int ProtocolVersion,
    string Type,
    Guid CorrelationId,
    Guid ServiceInstanceId,
    bool ServiceReady,
    bool EnforcementConfirmed,
    string SessionState,
    long RemainingSeconds,
    DateTimeOffset ObservedAtUtc,
    PolicyDelivery? Policy = null,
    string? ErrorCode = null);

public static class ProcessAgentProtocol
{
#if FOCUSGUARD_PRODUCTION
    public const string PipeName = "FocusGuard.ProcessAgent.v1";
#else
    public const string PipeName = "FocusGuard.Development.ProcessAgent.v1";
#endif
    public const int CurrentVersion = 1;
    public const int MaximumMessageBytes = IpcProtocol.MaxPayloadBytes;
    public const string HeartbeatType = "heartbeat";
    public const string AuditType = "audit";
    public const string StatusResponseType = "agentStatus";
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8
    };

    private static readonly HashSet<string> RequestProperties =
    [
        "protocolVersion", "type", "correlationId", "agentInstanceId", "sentAtUtc",
        "acknowledgedPolicyId", "acknowledgedPolicyHash", "blockedExecutable", "outcome"
    ];

    private static readonly HashSet<string> ResponseProperties =
    [
        "protocolVersion", "type", "correlationId", "serviceInstanceId", "serviceReady",
        "enforcementConfirmed", "sessionState", "remainingSeconds", "observedAtUtc",
        "policy", "errorCode"
    ];

    static ProcessAgentProtocol() =>
        JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static IpcCodecResult<ProcessAgentRequest> ReadRequest(
        ReadOnlySpan<byte> payload,
        DateTimeOffset utcNow)
    {
        var shape = ValidateShape(payload, RequestProperties);
        if (!shape.IsValid)
        {
            return IpcCodecResult<ProcessAgentRequest>.Failure([.. shape.Errors]);
        }

        try
        {
            var request = JsonSerializer.Deserialize<ProcessAgentRequest>(payload, JsonOptions);
            if (request is null)
            {
                return IpcCodecResult<ProcessAgentRequest>.Failure("Process agent request is required.");
            }

            var errors = new List<string>();
            if (request.ProtocolVersion != CurrentVersion) errors.Add("Unsupported process agent protocol version.");
            if (request.Type is not (HeartbeatType or AuditType)) errors.Add("Unsupported process agent message type.");
            if (request.CorrelationId == Guid.Empty) errors.Add("Correlation ID is required.");
            if (request.AgentInstanceId == Guid.Empty) errors.Add("Agent instance ID is required.");
            if (request.SentAtUtc.Offset != TimeSpan.Zero
                || (request.SentAtUtc - utcNow).Duration() > MaximumClockSkew)
            {
                errors.Add("Process agent timestamp must be current UTC.");
            }
            if ((request.AcknowledgedPolicyId is null) != (request.AcknowledgedPolicyHash is null)
                || request.AcknowledgedPolicyId == Guid.Empty
                || (request.AcknowledgedPolicyHash is not null
                    && (request.AcknowledgedPolicyHash.Length != 64
                        || !request.AcknowledgedPolicyHash.All(Uri.IsHexDigit))))
            {
                errors.Add("Policy acknowledgement must contain a valid ID and SHA-256 hash together.");
            }

            var hasAudit = request.BlockedExecutable is not null || request.Outcome is not null;
            if ((request.Type == AuditType) != hasAudit
                || (request.Type == AuditType
                    && (!IsSanitizedExecutable(request.BlockedExecutable)
                        || request.Outcome is not ("Closed" or "Terminated" or "Failed"))))
            {
                errors.Add("Process audit fields are invalid or unsanitized.");
            }

            return errors.Count == 0
                ? IpcCodecResult<ProcessAgentRequest>.Success(request)
                : IpcCodecResult<ProcessAgentRequest>.Failure([.. errors]);
        }
        catch (JsonException)
        {
            return IpcCodecResult<ProcessAgentRequest>.Failure("Process agent request is invalid.");
        }
    }

    public static IpcCodecResult<ProcessAgentResponse> ReadResponse(ReadOnlySpan<byte> payload)
    {
        var shape = ValidateShape(payload, ResponseProperties);
        if (!shape.IsValid)
        {
            return IpcCodecResult<ProcessAgentResponse>.Failure([.. shape.Errors]);
        }

        try
        {
            var response = JsonSerializer.Deserialize<ProcessAgentResponse>(payload, JsonOptions);
            if (response is null
                || response.ProtocolVersion != CurrentVersion
                || response.Type != StatusResponseType
                || response.CorrelationId == Guid.Empty
                || response.ObservedAtUtc.Offset != TimeSpan.Zero
                || response.RemainingSeconds < 0
                || string.IsNullOrWhiteSpace(response.SessionState)
                || (response.ServiceReady && response.ServiceInstanceId == Guid.Empty)
                || (response.Policy is not null && !response.Policy.HasValidIntegrity())
                || (response.EnforcementConfirmed && response.Policy is null))
            {
                return IpcCodecResult<ProcessAgentResponse>.Failure("Process agent response fields are invalid.");
            }

            return IpcCodecResult<ProcessAgentResponse>.Success(response);
        }
        catch (JsonException)
        {
            return IpcCodecResult<ProcessAgentResponse>.Failure("Process agent response is invalid.");
        }
    }

    public static byte[] WriteRequest(ProcessAgentRequest request) => WriteBounded(request);
    public static byte[] WriteResponse(ProcessAgentResponse response) => WriteBounded(response);

    private static byte[] WriteBounded<T>(T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length is 0 or > MaximumMessageBytes)
        {
            throw new InvalidDataException("Process agent message is outside the payload limit.");
        }
        return payload;
    }

    private static bool IsSanitizedExecutable(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 260
        && string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal)
        && value.IndexOfAny(['/', '\\', ':', '?', '#']) < 0;

    private static IpcValidationResult ValidateShape(
        ReadOnlySpan<byte> payload,
        IReadOnlySet<string> expectedProperties)
    {
        if (payload.Length is 0 or > MaximumMessageBytes)
        {
            return new IpcValidationResult(["Process agent message is outside the payload limit."]);
        }
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new IpcValidationResult(["Process agent message must be a JSON object."]);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!expectedProperties.Contains(property.Name) || !seen.Add(property.Name))
                {
                    return new IpcValidationResult(["Process agent message shape is invalid."]);
                }
            }
            return IpcValidationResult.Success;
        }
        catch (JsonException)
        {
            return new IpcValidationResult(["Process agent message is invalid JSON."]);
        }
    }
}
