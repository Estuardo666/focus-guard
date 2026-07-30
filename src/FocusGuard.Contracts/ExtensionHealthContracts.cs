using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Domain;

namespace FocusGuard.Contracts;

public sealed record ExtensionHealthRequest(
    int ProtocolVersion,
    string Type,
    Guid CorrelationId,
    DateTimeOffset SentAtUtc,
    Guid? AcknowledgedPolicyId = null,
    string? AcknowledgedPolicyHash = null,
    string? BlockedUrl = null,
    // Set only on a "capture" message: the line the user typed on the block page. May be
    // null when they simply pressed Enter and accepted the pre-filled domain.
    string? CaptureText = null);

public sealed record ExtensionHealthResponse(
    int ProtocolVersion,
    string Type,
    Guid CorrelationId,
    Guid ServiceInstanceId,
    bool ServiceReady,
    bool ExtensionReady,
    string SessionState,
    long RemainingSeconds,
    DateTimeOffset ObservedAtUtc,
    string? ErrorCode,
    PolicyDelivery? Policy = null,
    bool EnforcementConfirmed = false);

public static class ExtensionHealthProtocol
{
#if FOCUSGUARD_PRODUCTION
    public const string PipeName = "FocusGuard.ExtensionHealth.v1";
#else
    public const string PipeName = "FocusGuard.Development.ExtensionHealth.v1";
#endif
    public const int CurrentVersion = 1;
    public const int MaxMessageBytes = IpcProtocol.MaxPayloadBytes;
    public const int MaxJsonDepth = 8;
    public const string HeartbeatType = "heartbeat";
    public const string StatusType = "status";
    public const string AuditType = "audit";
    public const string CaptureType = "capture";
    public const int MaximumCaptureTextLength = IpcProtocol.MaximumCaptureTextLength;
    public const string StatusResponseType = "healthStatus";
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = MaxJsonDepth
    };

    static ExtensionHealthProtocol()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private static readonly HashSet<string> RequestProperties =
    [
        "protocolVersion",
        "type",
        "correlationId",
        "sentAtUtc",
        "acknowledgedPolicyId",
        "acknowledgedPolicyHash",
        "blockedUrl",
        "captureText"
    ];

    private static readonly HashSet<string> ResponseProperties =
    [
        "protocolVersion",
        "type",
        "correlationId",
        "serviceInstanceId",
        "serviceReady",
        "extensionReady",
        "sessionState",
        "remainingSeconds",
        "observedAtUtc",
        "errorCode",
        "policy",
        "enforcementConfirmed"
    ];

    public static IpcCodecResult<ExtensionHealthRequest> ReadRequest(
        ReadOnlySpan<byte> payload,
        DateTimeOffset utcNow)
    {
        var shape = ValidateJsonShape(payload, RequestProperties);
        if (!shape.IsValid)
        {
            return IpcCodecResult<ExtensionHealthRequest>.Failure([.. shape.Errors]);
        }

        try
        {
            var request = JsonSerializer.Deserialize<ExtensionHealthRequest>(payload, JsonOptions);
            if (request is null)
            {
                return IpcCodecResult<ExtensionHealthRequest>.Failure("Health request is required.");
            }

            var errors = new List<string>();
            if (request.ProtocolVersion != CurrentVersion)
            {
                errors.Add("Unsupported health protocol version.");
            }

            if (request.Type is not (HeartbeatType or StatusType or AuditType or CaptureType))
            {
                errors.Add("Unsupported health message type.");
            }

            if (request.CorrelationId == Guid.Empty)
            {
                errors.Add("Correlation ID is required.");
            }

            if (request.SentAtUtc.Offset != TimeSpan.Zero)
            {
                errors.Add("Health timestamp must use UTC.");
            }

            if ((request.AcknowledgedPolicyId is null) !=
                (request.AcknowledgedPolicyHash is null)
                || request.AcknowledgedPolicyId == Guid.Empty
                || (request.AcknowledgedPolicyHash is not null
                    && request.AcknowledgedPolicyHash.Length != 64))
            {
                errors.Add("Policy acknowledgement must contain a valid ID and hash together.");
            }
            else if ((request.SentAtUtc - utcNow).Duration() > MaximumClockSkew)
            {
                errors.Add("Health timestamp is outside the allowed clock skew.");
            }

            // Audit and capture both name the site that was blocked; nothing else may.
            if ((request.Type is AuditType or CaptureType) != (request.BlockedUrl is not null)
                || (request.BlockedUrl is not null
                    && (request.BlockedUrl.Length > 2_048
                        || request.BlockedUrl.Contains('?', StringComparison.Ordinal)
                        || request.BlockedUrl.Contains('#', StringComparison.Ordinal)
                        || !Uri.TryCreate(request.BlockedUrl, UriKind.Absolute, out var blockedUri)
                        || blockedUri.Scheme is not ("http" or "https")
                        || string.IsNullOrWhiteSpace(blockedUri.Host))))
            {
                errors.Add("Browser audit must contain one sanitized HTTP(S) URL.");
            }

            if (request.CaptureText is not null
                && (request.Type != CaptureType
                    || request.CaptureText.Length > MaximumCaptureTextLength))
            {
                errors.Add("Capture text is only valid on a bounded capture message.");
            }

            return errors.Count == 0
                ? IpcCodecResult<ExtensionHealthRequest>.Success(request)
                : IpcCodecResult<ExtensionHealthRequest>.Failure([.. errors]);
        }
        catch (JsonException)
        {
            return IpcCodecResult<ExtensionHealthRequest>.Failure("Health request payload is invalid.");
        }
    }

    public static IpcCodecResult<ExtensionHealthResponse> ReadResponse(ReadOnlySpan<byte> payload)
    {
        var shape = ValidateJsonShape(payload, ResponseProperties);
        if (!shape.IsValid)
        {
            return IpcCodecResult<ExtensionHealthResponse>.Failure([.. shape.Errors]);
        }

        try
        {
            var response = JsonSerializer.Deserialize<ExtensionHealthResponse>(payload, JsonOptions);
            if (response is null ||
                response.ProtocolVersion != CurrentVersion ||
                response.Type != StatusResponseType ||
                response.CorrelationId == Guid.Empty ||
                response.ObservedAtUtc.Offset != TimeSpan.Zero ||
                response.RemainingSeconds < 0 ||
                string.IsNullOrWhiteSpace(response.SessionState))
            {
                return IpcCodecResult<ExtensionHealthResponse>.Failure("Health response fields are invalid.");
            }

            if (response.Policy is not null && !response.Policy.HasValidIntegrity())
            {
                return IpcCodecResult<ExtensionHealthResponse>.Failure(
                    "Health response policy integrity is invalid.");
            }

            if (response.ServiceReady && response.ServiceInstanceId == Guid.Empty)
            {
                return IpcCodecResult<ExtensionHealthResponse>.Failure(
                    "Ready service response requires a service instance ID.");
            }

            return IpcCodecResult<ExtensionHealthResponse>.Success(response);
        }
        catch (JsonException)
        {
            return IpcCodecResult<ExtensionHealthResponse>.Failure("Health response payload is invalid.");
        }
    }

    public static byte[] WriteRequest(ExtensionHealthRequest request) =>
        WriteBounded(request);

    public static byte[] WriteResponse(ExtensionHealthResponse response) =>
        WriteBounded(response);

    private static byte[] WriteBounded<T>(T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length is 0 or > MaxMessageBytes)
        {
            throw new InvalidDataException("Health message length is outside the supported range.");
        }

        return payload;
    }

    private static IpcValidationResult ValidateJsonShape(
        ReadOnlySpan<byte> payload,
        IReadOnlySet<string> expectedProperties)
    {
        if (payload.Length is 0 or > MaxMessageBytes)
        {
            return new IpcValidationResult(["Health message length is outside the supported range."]);
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
                return new IpcValidationResult(["Health message root must be an object."]);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return new IpcValidationResult(["Health message contains duplicate properties."]);
                }

                if (!expectedProperties.Contains(property.Name))
                {
                    return new IpcValidationResult(["Health message contains an unsupported property."]);
                }
            }

            if (!expectedProperties.All(names.Contains))
            {
                return new IpcValidationResult(["Health message is missing required properties."]);
            }

            return IpcValidationResult.Success;
        }
        catch (JsonException)
        {
            return new IpcValidationResult(["Health message must be valid bounded JSON."]);
        }
    }
}

public static class BoundedPipeFraming
{
    private const int HeaderSize = sizeof(uint);

    public static async ValueTask<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderSize];
        var headerRead = await ReadExactOrEofAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead != HeaderSize)
        {
            throw new InvalidDataException("Pipe message header is truncated.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > ExtensionHealthProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException("Pipe message length is outside the supported range.");
        }

        var payload = new byte[(int)length];
        if (await ReadExactOrEofAsync(stream, payload, cancellationToken).ConfigureAwait(false) != payload.Length)
        {
            throw new InvalidDataException("Pipe message payload is truncated.");
        }

        return payload;
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.Length is 0 or > ExtensionHealthProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException("Pipe message length is outside the supported range.");
        }

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadExactOrEofAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
