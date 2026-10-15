using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Devices;
using FocusGuard.Identity;

namespace FocusGuard.Sync;

public interface ISettingsRequestSender
{
    string? AccountBinding { get; }
    Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> requestFactory, CancellationToken token);
}

/// <summary>Native identity attaches bearer privately and enforces its configured HTTPS API origin/no redirects.</summary>
public sealed class NativeSettingsRequestSender(IdentitySession identity) : ISettingsRequestSender
{
    public string? AccountBinding => identity.AccountBinding;
    public Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> factory, CancellationToken token) =>
        identity.SendApiRequestWithTokenDigestAsync(factory, token);
}

public enum SettingsTransportFailure { Authentication, Permission, MembershipChanged, RekeyRequired, Throttled, Unavailable, AmbiguousMissing, Protocol, Storage }
public sealed class SettingsTransportException(SettingsTransportFailure failure, TimeSpan? retryAfter = null)
    : IOException("Settings transport failed: " + failure)
{
    public SettingsTransportFailure Failure { get; } = failure;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Wire adapter only. E2EE/history verification remains mandatory in the worker.</summary>
public sealed class SettingsDocumentHttpTransport : ISettingsDocumentTransport
{
    private sealed record ReadResult(long Revision, EncryptedContentDocument Document, long? Cursor = null);
    private sealed record WriteResult(string State, long Revision, string EntityId, long? Cursor = null);
    private sealed record CursorResult(long Cursor);
    private sealed record ErrorResult(string Error);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly Uri origin;
    private readonly ISettingsRequestSender sender;
    private readonly DeviceAccountContext account;
    private readonly DeviceIdentity device;
    private readonly string session;
    private readonly string entityId;
    private readonly string target;
    private readonly TimeProvider time;
    private long lastSeenCursor;

    public long? LastSeenCursor
    {
        get
        {
            var cursor = Interlocked.Read(ref lastSeenCursor);
            return cursor > 0 ? cursor : null;
        }
    }

    public SettingsDocumentHttpTransport(Uri origin, ISettingsRequestSender sender, DeviceAccountContext account,
        DeviceIdentity device, string deviceSession, string entityId, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(origin); ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(account); account.Validate(); ArgumentNullException.ThrowIfNull(device);
        if (origin.Scheme != "https" || origin.AbsolutePath != "/" || origin.Query.Length != 0 ||
            origin.Fragment.Length != 0 || origin.UserInfo.Length != 0) throw new ArgumentException("Exact HTTPS API origin required.");
        DeviceEncoding.RequireId(entityId); _ = DeviceEncoding.Decode(deviceSession, 32);
        this.origin = origin; this.sender = sender; this.account = account; this.device = device;
        session = deviceSession; this.entityId = entityId; this.time = time ?? TimeProvider.System;
        target = "/v1/documents/" + entityId;
    }

    public async Task<byte[]?> ReadAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, null, token);
        if (response.StatusCode != HttpStatusCode.OK) throw await FailureAsync(response, token);
        var body = await ReadBodyAsync(response, token);
        var parsed = Parse<ReadResult>(body);
        if (parsed.Document is null || parsed.Revision != parsed.Document.ProposedRevision) throw Protocol();
        ValidateRouting(parsed.Document);
        RequireETag(response, parsed.Revision);
        RecordCursor(response, parsed.Cursor);
        return JsonSerializer.SerializeToUtf8Bytes(parsed.Document, Json);
    }

    public async Task<long?> ReadCursorAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, null, token, "/v1/sync/cursor");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Interlocked.Exchange(ref lastSeenCursor, 0); // Older API: fall back to the document snapshot.
            return null;
        }
        if (response.StatusCode != HttpStatusCode.OK) throw await FailureAsync(response, token);
        var parsed = Parse<CursorResult>(await ReadBodyAsync(response, token));
        if (parsed.Cursor is < 0 or > 9_007_199_254_740_991) throw Protocol();
        RecordCursor(response, parsed.Cursor);
        return parsed.Cursor;
    }

    public async Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token)
    {
        if (operationId == Guid.Empty || envelope.Length is < 2 or > 65_536) throw new ArgumentException("Invalid settings publication.");
        var document = Parse<EncryptedContentDocument>(envelope);
        ValidateRouting(document);
        var context = new ContentDocumentContext(account.AccountId, account.GenerationId, entityId, GlobalSettings.DocumentKind,
            document.BaseRevision, document.ProposedRevision, document.KeyEpoch, document.MembershipRevision);
        if (!ContentDocumentCodec.VerifySignedHeader(document, context, device.Public))
            throw new CryptographicException("Outgoing document signature rejected.");
        var body = ContentDocumentCodec.CanonicalDocumentBody(document);
        using var response = await SendAsync(HttpMethod.Post, body, token);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = Parse<ErrorResult>(await ReadBodyAsync(response, token));
            if (error.Error == "DocumentRevisionConflict") return SettingsPublishResult.RevisionConflict;
            throw Error(error.Error);
        }
        if (response.StatusCode != HttpStatusCode.Created) throw await FailureAsync(response, token);
        var result = Parse<WriteResult>(await ReadBodyAsync(response, token));
        if (result.State != "Stored" || result.Revision != document.ProposedRevision || result.EntityId != entityId) throw Protocol();
        RequireETag(response, result.Revision);
        RecordCursor(response, result.Cursor);
        return SettingsPublishResult.Stored; // The worker still requires a committed read before ACK.
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, byte[]? body, CancellationToken token,
        string? requestTargetOverride = null)
    {
        RequireAccount();
        var requestTarget = requestTargetOverride ?? target;
        return sender.SendAsync(digest =>
        {
            RequireAccount(); // Check again after identity refresh, against the token actually attached.
            _ = DeviceEncoding.Decode(digest, 32);
            var now = time.GetUtcNow();
            now = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            var requestId = Guid.NewGuid().ToString("D"); // Fresh possession proof per HTTP attempt.
            var proof = new DeviceRequestProof(method.Method, requestTarget, account.AccountId, account.GenerationId,
                device.Public.DeviceId, requestId, now.ToUnixTimeMilliseconds(), digest,
                DeviceEncoding.Encode(SHA256.HashData(body ?? Array.Empty<byte>())));
            var canonical = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var request = new HttpRequestMessage(method, new Uri(origin, requestTarget));
                request.Headers.Add("X-FG-Device-Id", device.Public.DeviceId);
                request.Headers.Add("X-FG-Device-Session", session);
                request.Headers.Add("X-FG-Request-Id", requestId);
                request.Headers.Add("X-FG-Timestamp", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-FG-Proof", device.Sign("FG-REQUEST-1", canonical));
                if (body is not null) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
                return request;
            }
            finally { CryptographicOperations.ZeroMemory(canonical); }
        }, token);
    }

    private void RequireAccount()
    {
        if (sender.AccountBinding != account.AccountBinding) throw new SettingsTransportException(SettingsTransportFailure.Authentication);
    }
    private void ValidateRouting(EncryptedContentDocument document)
    {
        if (document.AccountId != account.AccountId || document.GenerationId != account.GenerationId ||
            document.EntityId != entityId || document.Kind != GlobalSettings.DocumentKind || document.Protocol != ContentDocumentCodec.Protocol ||
            document.Suite != ContentDocumentCodec.Suite) throw Protocol();
        new ContentDocumentContext(document.AccountId, document.GenerationId, document.EntityId, document.Kind,
            document.BaseRevision, document.ProposedRevision, document.KeyEpoch, document.MembershipRevision).Validate();
    }
    private static void RequireETag(HttpResponseMessage response, long revision)
    {
        if (response.Headers.ETag is not { IsWeak: false } tag || tag.Tag != "\"" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"") throw Protocol();
    }
    private void RecordCursor(HttpResponseMessage response, long? bodyCursor)
    {
        long? headerCursor = null;
        if (response.Headers.TryGetValues("X-FG-Cursor", out var values))
        {
            var all = values.ToArray();
            if (all.Length != 1 || !long.TryParse(all[0], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 0 or > 9_007_199_254_740_991)
                throw Protocol();
            headerCursor = parsed;
        }
        if (headerCursor is not null && bodyCursor is not null && headerCursor != bodyCursor) throw Protocol();
        var cursor = headerCursor ?? bodyCursor;
        if (cursor is null) return; // Older compatible servers can omit the optional cursor.
        if (cursor is < 0 or > 9_007_199_254_740_991 || cursor.Value < Interlocked.Read(ref lastSeenCursor)) throw Protocol();
        Interlocked.Exchange(ref lastSeenCursor, cursor.Value);
    }
    private static T Parse<T>(ReadOnlyMemory<byte> body) where T : class
    {
        if (body.Length is < 2 or > 131_072) throw Protocol();
        try
        {
            var canonical = ContentDocumentCodec.CanonicalizeJson(body.Span);
            CryptographicOperations.ZeroMemory(canonical);
            return JsonSerializer.Deserialize<T>(body.Span, Json) ?? throw Protocol();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException) { throw Protocol(); }
    }
    private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw Protocol();
        if (response.Content.Headers.ContentLength > 131_072) throw Protocol();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > 131_072) throw Protocol();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private async Task<SettingsTransportException> FailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized) return new(SettingsTransportFailure.Authentication);
        if (response.StatusCode == HttpStatusCode.Forbidden) return new(SettingsTransportFailure.Permission);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            RecordCursor(response, null);
            return new(SettingsTransportFailure.AmbiguousMissing);
        }
        if ((int)response.StatusCode == 429)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - time.GetUtcNow());
            return new(SettingsTransportFailure.Throttled, retry is { } delay && delay > TimeSpan.Zero ? delay : null);
        }
        if (response.StatusCode == HttpStatusCode.Conflict) return Error(Parse<ErrorResult>(await ReadBodyAsync(response, token)).Error);
        return (int)response.StatusCode >= 500 ? new(SettingsTransportFailure.Unavailable) : Protocol();
    }
    private static SettingsTransportException Error(string code) => code switch
    {
        "DeviceMembershipUpdateRequired" => new(SettingsTransportFailure.MembershipChanged),
        "DocumentRekeyRequired" => new(SettingsTransportFailure.RekeyRequired),
        _ => Protocol()
    };
    private static SettingsTransportException Protocol() => new(SettingsTransportFailure.Protocol);
}
