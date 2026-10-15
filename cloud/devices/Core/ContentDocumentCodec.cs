using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Org.Webpki.JsonCanonicalizer;

namespace FocusGuard.Devices;

public sealed record ContentDocumentContext(string AccountId, string GenerationId, string EntityId,
    string Kind, long BaseRevision, long ProposedRevision, long KeyEpoch, long MembershipRevision)
{
    public void Validate()
    {
        DeviceEncoding.RequireId(AccountId);
        DeviceEncoding.RequireId(GenerationId);
        DeviceEncoding.RequireId(EntityId);
        if (string.IsNullOrEmpty(Kind) || Kind.Length > 64 || Kind.Any(character =>
            !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')) ||
            Kind[0] is < 'a' or > 'z')
            throw new ArgumentException("Invalid document kind.");
        if (BaseRevision is < 0 or > 9_007_199_254_740_990 ||
            ProposedRevision != BaseRevision + 1 || ProposedRevision > 9_007_199_254_740_991 ||
            KeyEpoch is < 1 or > 9_007_199_254_740_991 ||
            MembershipRevision is < 1 or > 9_007_199_254_740_991)
            throw new ArgumentException("Invalid document revision or epoch.");
    }
}

public sealed record EncryptedContentDocument(string Protocol, string Suite, string AccountId,
    string GenerationId, string EntityId, string Kind, long BaseRevision, long ProposedRevision,
    long KeyEpoch, long MembershipRevision, string AuthorDeviceId, string Nonce, string Ciphertext, string Signature);

/// <summary>FG-E2EE-1 JCS envelope with XChaCha20-Poly1305 content encryption and Ed25519 author signature.</summary>
public static class ContentDocumentCodec
{
    public const string Protocol = "FG-E2EE-1";
    public const string Suite = "XCHACHA20-POLY1305-IETF";
    private const string SignatureDomain = "FG-DOCUMENT-1";

    public static EncryptedContentDocument Encrypt(ReadOnlySpan<byte> canonicalJson,
        ReadOnlySpan<byte> contentKey, ContentDocumentContext context, DeviceIdentity author)
    {
        ArgumentNullException.ThrowIfNull(author);
        context.Validate();
        author.Public.Validate();
        if (contentKey.Length != SodiumAead.KeyBytes) throw new ArgumentException("Content key must contain 32 bytes.");
        ValidateCanonicalJson(canonicalJson);

        var nonce = RandomNumberGenerator.GetBytes(SodiumAead.NonceBytes);
        var aad = AssociatedData(context);
        byte[]? ciphertext = null;
        byte[]? header = null;
        try
        {
            ciphertext = SodiumAead.Encrypt(canonicalJson, aad, nonce, contentKey);
            var unsigned = new EncryptedContentDocument(Protocol, Suite, context.AccountId,
                context.GenerationId, context.EntityId, context.Kind, context.BaseRevision,
                context.ProposedRevision, context.KeyEpoch, context.MembershipRevision, author.Public.DeviceId,
                DeviceEncoding.Encode(nonce), DeviceEncoding.Encode(ciphertext), DeviceEncoding.Encode(new byte[64]));
            header = CanonicalHeader(unsigned);
            var signature = author.Sign(SignatureDomain, header);
            try { return unsigned with { Signature = signature }; }
            finally { CryptographicOperations.ZeroMemory(DeviceEncoding.Decode(signature, 64)); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(aad);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (header is not null) CryptographicOperations.ZeroMemory(header);
        }
    }

    public static byte[] Decrypt(EncryptedContentDocument document, ContentDocumentContext expectedContext,
        ReadOnlySpan<byte> contentKey, DevicePublicIdentity verifiedAuthor)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(verifiedAuthor);
        expectedContext.Validate();
        verifiedAuthor.Validate();
        if (contentKey.Length != SodiumAead.KeyBytes || document.Protocol != Protocol || document.Suite != Suite ||
            document.AccountId != expectedContext.AccountId || document.GenerationId != expectedContext.GenerationId ||
            document.EntityId != expectedContext.EntityId || document.Kind != expectedContext.Kind ||
            document.BaseRevision != expectedContext.BaseRevision || document.ProposedRevision != expectedContext.ProposedRevision ||
            document.KeyEpoch != expectedContext.KeyEpoch || document.MembershipRevision != expectedContext.MembershipRevision ||
            document.AuthorDeviceId != verifiedAuthor.DeviceId)
            throw new CryptographicException("Document context rejected.");

        byte[] nonce;
        byte[] ciphertext;
        byte[] signature;
        try
        {
            nonce = DeviceEncoding.Decode(document.Nonce, SodiumAead.NonceBytes);
            ciphertext = DeviceEncoding.DecodeVariable(document.Ciphertext, SodiumAead.TagBytes + 1, 262_144 + SodiumAead.TagBytes);
            signature = DeviceEncoding.Decode(document.Signature, 64);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Document encoding rejected.", exception);
        }

        byte[]? header = null;
        byte[]? aad = null;
        byte[]? plaintext = null;
        try
        {
            var unsigned = document with { Signature = DeviceEncoding.Encode(new byte[64]) };
            header = CanonicalHeader(unsigned);
            if (!DeviceIdentity.Verify(verifiedAuthor, SignatureDomain, header, document.Signature))
                throw new CryptographicException("Document author signature rejected.");
            aad = AssociatedData(expectedContext);
            plaintext = SodiumAead.Decrypt(ciphertext, aad, nonce, contentKey);
            ValidateCanonicalJson(plaintext);
            var clear = plaintext;
            plaintext = null;
            return clear;
        }
        catch (ArgumentException exception)
        {
            throw new CryptographicException("Document data rejected.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(signature);
            if (header is not null) CryptographicOperations.ZeroMemory(header);
            if (aad is not null) CryptographicOperations.ZeroMemory(aad);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static bool VerifySignedHeader(EncryptedContentDocument document, ContentDocumentContext expectedContext,
        DevicePublicIdentity verifiedAuthor)
    {
        try
        {
            expectedContext.Validate();
            verifiedAuthor.Validate();
            if (document.Protocol != Protocol || document.Suite != Suite || document.AccountId != expectedContext.AccountId ||
                document.GenerationId != expectedContext.GenerationId || document.EntityId != expectedContext.EntityId ||
                document.Kind != expectedContext.Kind || document.BaseRevision != expectedContext.BaseRevision ||
                document.ProposedRevision != expectedContext.ProposedRevision || document.KeyEpoch != expectedContext.KeyEpoch ||
                document.MembershipRevision != expectedContext.MembershipRevision || document.AuthorDeviceId != verifiedAuthor.DeviceId)
                return false;
            var nonce = DeviceEncoding.Decode(document.Nonce, SodiumAead.NonceBytes);
            var ciphertext = DeviceEncoding.DecodeVariable(document.Ciphertext, SodiumAead.TagBytes + 1,
                262_144 + SodiumAead.TagBytes);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            var header = CanonicalHeader(document with { Signature = DeviceEncoding.Encode(new byte[64]) });
            try { return DeviceIdentity.Verify(verifiedAuthor, SignatureDomain, header, document.Signature); }
            finally { CryptographicOperations.ZeroMemory(header); }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static byte[] CanonicalHeader(EncryptedContentDocument document)
    {
        var context = new ContentDocumentContext(document.AccountId, document.GenerationId, document.EntityId,
            document.Kind, document.BaseRevision, document.ProposedRevision, document.KeyEpoch, document.MembershipRevision);
        context.Validate();
        DeviceEncoding.RequireId(document.AuthorDeviceId);
        return Canonical(new
        {
            protocol = document.Protocol, suite = document.Suite, accountId = document.AccountId,
            generationId = document.GenerationId, entityId = document.EntityId, kind = document.Kind,
            baseRevision = document.BaseRevision, proposedRevision = document.ProposedRevision,
            keyEpoch = document.KeyEpoch, authorDeviceId = document.AuthorDeviceId,
            membershipRevision = document.MembershipRevision, nonce = document.Nonce, ciphertext = document.Ciphertext
        });
    }

    public static byte[] AssociatedData(ContentDocumentContext context)
    {
        context.Validate();
        return Canonical(new
        {
            protocol = Protocol, accountId = context.AccountId,
            generationId = context.GenerationId, entityId = context.EntityId, kind = context.Kind,
            baseRevision = context.BaseRevision, proposedRevision = context.ProposedRevision,
            keyEpoch = context.KeyEpoch, membershipRevision = context.MembershipRevision
        });
    }

    public static byte[] CanonicalDocumentBody(EncryptedContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new JsonCanonicalizer(json).GetEncodedUTF8();
    }

    public static byte[] CanonicalizeJson(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > 262_144) throw new ArgumentException("Document JSON size rejected.");
        var bytes = json.ToArray();
        try
        {
            using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Document must be a JSON object.");
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return new JsonCanonicalizer(text).GetEncodedUTF8();
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Document JSON must be valid UTF-8.", exception);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Document JSON is malformed.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException("Document JSON cannot be canonicalized.", exception);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void ValidateCanonicalJson(ReadOnlySpan<byte> json)
    {
        var canonical = CanonicalizeJson(json);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(canonical, json))
                throw new ArgumentException("Document JSON must use JCS canonical encoding.");
        }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    private static byte[] Canonical<T>(T value) =>
        new JsonCanonicalizer(JsonSerializer.Serialize(value)).GetEncodedUTF8();
}
