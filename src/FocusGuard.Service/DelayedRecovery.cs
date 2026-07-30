using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Contracts;

namespace FocusGuard.Service;

public sealed record DelayedRecoveryRequest(
    int SchemaVersion,
    Guid RequestId,
    Guid SessionId,
    DateTimeOffset RequestedUtc,
    DateTimeOffset EligibleUtc);

public sealed record SignedDelayedRecoveryRequest(
    DelayedRecoveryRequest Request,
    string Integrity);

public sealed class DelayedRecoveryRequestStore
{
    public const int CurrentSchemaVersion = 1;
    private const int MaximumPayloadBytes = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly byte[] _integrityKey;

    public DelayedRecoveryRequestStore(byte[] integrityKey, string directory)
        : this(integrityKey.AsSpan(), directory)
    {
    }

    public DelayedRecoveryRequestStore(ReadOnlySpan<byte> integrityKey, string directory)
    {
        if (integrityKey.Length < 32)
        {
            throw new ArgumentException("The integrity key must contain at least 32 bytes.", nameof(integrityKey));
        }

        _integrityKey = integrityKey.ToArray();
        _directory = directory;
    }

    public string RequestPath => Path.Combine(_directory, "recovery-request.json");

    public async Task<DelayedRecoveryRequest> RequestAsync(
        StrictPolicySnapshot snapshot,
        string suppliedCode,
        DateTimeOffset requestedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!RecoveryCodeService.Validate(suppliedCode, snapshot.RecoveryCredential))
        {
            throw new UnauthorizedAccessException("The recovery code is invalid.");
        }

        var trustedRequestedUtc = requestedUtc.ToUniversalTime() >= snapshot.LastObservedUtc
            ? requestedUtc.ToUniversalTime()
            : snapshot.LastObservedUtc;
        var request = new DelayedRecoveryRequest(
            CurrentSchemaVersion,
            Guid.NewGuid(),
            snapshot.Policy.SessionId,
            trustedRequestedUtc,
            trustedRequestedUtc.Add(snapshot.RecoveryDelay));
        Validate(request, snapshot);

        if (File.Exists(RequestPath))
        {
            return await LoadAsync(snapshot, cancellationToken)
                ?? throw new InvalidDataException("The existing recovery request is invalid.");
        }

        var signed = new SignedDelayedRecoveryRequest(request, ComputeIntegrity(request));
        Directory.CreateDirectory(_directory);
        var temporaryPath = RequestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                JsonSerializer.SerializeToUtf8Bytes(signed, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, RequestPath, false);
        }
        catch (IOException) when (File.Exists(RequestPath))
        {
            return await LoadAsync(snapshot, cancellationToken)
                ?? throw new InvalidDataException("The concurrent recovery request is invalid.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return request;
    }

    public async Task<DelayedRecoveryRequest?> LoadAsync(
        StrictPolicySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(RequestPath))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(RequestPath);
            if (file.Length is <= 0 or > MaximumPayloadBytes)
            {
                return null;
            }

            var signed = JsonSerializer.Deserialize<SignedDelayedRecoveryRequest>(
                await File.ReadAllBytesAsync(RequestPath, cancellationToken),
                JsonOptions);
            if (signed is null)
            {
                return null;
            }

            Validate(signed.Request, snapshot);
            var expected = Convert.FromHexString(ComputeIntegrity(signed.Request));
            var actual = Convert.FromHexString(signed.Integrity);
            return CryptographicOperations.FixedTimeEquals(expected, actual)
                ? signed.Request
                : null;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or FormatException)
        {
            return null;
        }
    }

    public Task ReleaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(RequestPath))
        {
            File.Delete(RequestPath);
        }

        return Task.CompletedTask;
    }

    private string ComputeIntegrity(DelayedRecoveryRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        return Convert.ToHexString(HMACSHA256.HashData(_integrityKey, bytes));
    }

    private static void Validate(DelayedRecoveryRequest request, StrictPolicySnapshot snapshot)
    {
        if (request.SchemaVersion != CurrentSchemaVersion
            || request.RequestId == Guid.Empty
            || request.SessionId != snapshot.Policy.SessionId
            || request.RequestedUtc.Offset != TimeSpan.Zero
            || request.EligibleUtc.Offset != TimeSpan.Zero
            || request.RequestedUtc < snapshot.Policy.StartUtc
            || request.RequestedUtc >= snapshot.Policy.EndUtc
            || request.EligibleUtc != request.RequestedUtc.Add(snapshot.RecoveryDelay))
        {
            throw new InvalidDataException("The delayed recovery request is invalid.");
        }
    }
}

public sealed record RecoveryAuditEvent(
    int SchemaVersion,
    Guid EventId,
    Guid SessionId,
    DateTimeOffset OccurredUtc,
    string EventType,
    string Reason);

public sealed class RecoveryAuditLog(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(
        Guid sessionId,
        DateTimeOffset occurredUtc,
        string eventType,
        string reason,
        CancellationToken cancellationToken)
    {
        var auditDirectory = Path.Combine(directory, "audit");
        Directory.CreateDirectory(auditDirectory);
        var auditEvent = new RecoveryAuditEvent(
            1,
            Guid.NewGuid(),
            sessionId,
            occurredUtc.ToUniversalTime(),
            eventType,
            reason);
        var path = Path.Combine(
            auditDirectory,
            $"{occurredUtc:yyyyMMddHHmmssfff}-{auditEvent.EventId:N}.json");
        await File.WriteAllBytesAsync(
            path,
            JsonSerializer.SerializeToUtf8Bytes(auditEvent, JsonOptions),
            cancellationToken);
    }
}
