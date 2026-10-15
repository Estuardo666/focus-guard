using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Devices;

namespace FocusGuard.Sync;

/// <summary>Adapter to existing FG-E2EE-1. The caller resolves authors from verified membership history.</summary>
public static class SettingsEnvelopeCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Recover a lost write ACK only from the exact committed encrypted document, verified cryptographically.</summary>
    public static SettingsOutbox ConfirmOwnWrite(SettingsOutbox pending, ReadOnlyMemory<byte> committed,
        ContentDocumentContext expected, ReadOnlySpan<byte> accountContentKey, DevicePublicIdentity verifiedLocalAuthor)
    {
        ArgumentNullException.ThrowIfNull(pending); pending.Validate();
        if (pending.InFlight is null) return pending;
        var intent = pending.InFlight.Intent;
        if (expected.BaseRevision != intent.BaseRevision || expected.ProposedRevision != intent.ProposedRevision)
            throw new ArgumentException("Commit context does not match the pending write.");
        var snapshot = Open(committed, expected, accountContentKey, verifiedLocalAuthor);
        var original = ContentDocumentCodec.CanonicalizeJson(pending.InFlight.GetEnvelope());
        var received = ContentDocumentCodec.CanonicalizeJson(committed.Span);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), SHA256.HashData(received))) return pending;
            return pending.Acknowledge(intent.OperationId, snapshot.Revision, snapshot.Values, snapshot.Deleted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original); CryptographicOperations.ZeroMemory(received);
        }
    }

    public static byte[] Prepare(SettingsWriteIntent intent, ContentDocumentContext context,
        ReadOnlySpan<byte> accountContentKey, DeviceIdentity author)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(context);
        intent.Validate(); context.Validate();
        if (context.Kind != GlobalSettings.DocumentKind || intent.BaseRevision != context.BaseRevision ||
            intent.ProposedRevision != context.ProposedRevision)
            throw new ArgumentException("Settings proposal does not match the encrypted document context.");
        var clear = intent.Deleted ? "{\"deleted\":true}"u8.ToArray() : GlobalSettingsCodec.Encode(intent.Values);
        try
        {
            var document = ContentDocumentCodec.Encrypt(clear, accountContentKey, context, author);
            return JsonSerializer.SerializeToUtf8Bytes(document, Json);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static SettingsSnapshot Open(ReadOnlyMemory<byte> envelope, ContentDocumentContext expected,
        ReadOnlySpan<byte> accountContentKey, DevicePublicIdentity verifiedAuthor)
    {
        ArgumentNullException.ThrowIfNull(expected); expected.Validate();
        if (expected.Kind != GlobalSettings.DocumentKind) throw new ArgumentException("Unsupported settings document kind.");
        if (envelope.Length is < 2 or > 65_536) throw new JsonException("Invalid settings envelope size.");
        // Canonicalization is used only to reject duplicate fields; trust still requires signature and AEAD.
        var normalized = ContentDocumentCodec.CanonicalizeJson(envelope.Span);
        CryptographicOperations.ZeroMemory(normalized);
        var document = JsonSerializer.Deserialize<EncryptedContentDocument>(envelope.Span, Json)
            ?? throw new JsonException("Missing encrypted settings document.");
        var clear = ContentDocumentCodec.Decrypt(document, expected, accountContentKey, verifiedAuthor);
        try
        {
            using var payload = JsonDocument.Parse(clear);
            if (payload.RootElement.ValueKind == JsonValueKind.Object && payload.RootElement.EnumerateObject().Count() == 1 &&
                payload.RootElement.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
                return new(expected.ProposedRevision, GlobalSettings.Default, Deleted: true);
            return new(expected.ProposedRevision, GlobalSettingsCodec.Decode(clear));
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
