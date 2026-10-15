using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.Webpki.JsonCanonicalizer;

namespace FocusGuard.Devices;

public sealed record KeyEnvelopeContext(string AccountId, string GenerationId, long KeyEpoch,
    string RecipientId, string MembershipDigest, string SignerDeviceId)
{
    public void Validate()
    {
        DeviceEncoding.RequireId(AccountId); DeviceEncoding.RequireId(GenerationId);
        DeviceEncoding.RequireId(RecipientId); DeviceEncoding.RequireId(SignerDeviceId);
        if (KeyEpoch is < 1 or > 9_007_199_254_740_991) throw new ArgumentException("Invalid key epoch.");
        _ = DeviceEncoding.Decode(MembershipDigest, 32);
    }
}

public sealed record SignedKeyEnvelope(string Suite, string GenerationId, long KeyEpoch, string RecipientId,
    string RecipientPublicKey, string MembershipDigest, string Ciphertext, string SignerDeviceId, string Signature);

/// <summary>FG-SODIUM-BOX-1 key wrapping; caller supplies signer/recipient from verified membership.</summary>
public static class SignedKeyEnvelopeCodec
{
    private delegate byte[] SealedBoxOpener(ReadOnlySpan<byte> ciphertext);
    private delegate byte[] EnvelopeSigner(string domain, byte[] payload);
    public const string Suite = "FG-SODIUM-BOX-1";
    private const string PayloadProtocol = "FG-ACCOUNT-KEY-1";

    public static SignedKeyEnvelope SealForDevice(ReadOnlySpan<byte> accountContentKey, KeyEnvelopeContext context,
        string recipientEncryptionPublicKey, DeviceIdentity signer) =>
        Seal(accountContentKey, context, recipientEncryptionPublicKey, signer.Public,
            (domain, payload) => DeviceEncoding.Decode(signer.Sign(domain, payload), 64));

    private static SignedKeyEnvelope Seal(ReadOnlySpan<byte> key, KeyEnvelopeContext context, string recipientPublic,
        DevicePublicIdentity signerIdentity, EnvelopeSigner sign)
    {
        context.Validate(); signerIdentity.Validate();
        if (key.Length != 32 || signerIdentity.DeviceId != context.SignerDeviceId)
            throw new ArgumentException("Key or signer does not match the envelope context.");
        var recipient = DeviceEncoding.Decode(recipientPublic, 32);
        var payload = new
        {
            protocol = PayloadProtocol, accountId = context.AccountId, generationId = context.GenerationId,
            keyEpoch = context.KeyEpoch, recipientId = context.RecipientId,
            membershipDigest = context.MembershipDigest, contentKey = DeviceEncoding.Encode(key)
        };
        var clear = Canonical(payload);
        try
        {
            var ciphertext = SodiumBox.Seal(clear, recipientPublic);
            var unsigned = new SignedKeyEnvelope(Suite, context.GenerationId, context.KeyEpoch, context.RecipientId,
                recipientPublic, context.MembershipDigest, DeviceEncoding.Encode(ciphertext), context.SignerDeviceId, "");
            var signedBytes = CanonicalHeader(unsigned);
            var signature = sign("FG-SODIUM-BOX-1", signedBytes);
            try { return unsigned with { Signature = DeviceEncoding.Encode(signature) }; }
            finally { CryptographicOperations.ZeroMemory(signature); CryptographicOperations.ZeroMemory(ciphertext); CryptographicOperations.ZeroMemory(signedBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(recipient); }
    }

    public static byte[] OpenForDevice(SignedKeyEnvelope envelope, KeyEnvelopeContext context,
        DevicePublicIdentity signer, DeviceIdentity recipient) => Open(envelope, context, signer,
            recipient.Public.DeviceId, recipient.Public.EncryptionPublicKey, recipient.OpenSealed);

    public static byte[] OpenForRecovery(SignedKeyEnvelope envelope, KeyEnvelopeContext context,
        DevicePublicIdentity signer, RecoveryKit recipient) => Open(envelope, context, signer,
            context.RecipientId, recipient.Public.EncryptionPublicKey, recipient.Open);

    public static bool VerifySignedHeader(SignedKeyEnvelope envelope, KeyEnvelopeContext context,
        DevicePublicIdentity signer, string expectedRecipientId, string expectedRecipientPublic)
    {
        try
        {
            context.Validate(); signer.Validate();
            if (envelope is null || envelope.Suite != Suite || envelope.GenerationId != context.GenerationId ||
                envelope.KeyEpoch != context.KeyEpoch || envelope.RecipientId != context.RecipientId ||
                envelope.MembershipDigest != context.MembershipDigest || envelope.SignerDeviceId != context.SignerDeviceId ||
                envelope.SignerDeviceId != signer.DeviceId || envelope.RecipientId != expectedRecipientId ||
                envelope.RecipientPublicKey != expectedRecipientPublic)
                return false;
            var cipher = DeviceEncoding.DecodeVariable(envelope.Ciphertext, 48, 6144);
            CryptographicOperations.ZeroMemory(cipher);
            var signature = DeviceEncoding.Decode(envelope.Signature, 64);
            var publicKey = DeviceEncoding.Decode(signer.SigningPublicKey, 32);
            var bytes = DomainBytes(Suite, CanonicalHeader(envelope));
            try
            {
                return NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(
                    NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519,
                        publicKey, NSec.Cryptography.KeyBlobFormat.RawPublicKey), bytes, signature);
            }
            finally { CryptographicOperations.ZeroMemory(signature); CryptographicOperations.ZeroMemory(publicKey); CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return false; }
    }


    private static byte[] Open(SignedKeyEnvelope envelope, KeyEnvelopeContext context, DevicePublicIdentity signer,
        string localRecipientId, string localRecipientPublic, SealedBoxOpener open)
    {
        context.Validate(); signer.Validate();
        if (envelope is null || envelope.Suite != Suite || envelope.GenerationId != context.GenerationId ||
            envelope.KeyEpoch != context.KeyEpoch || envelope.RecipientId != context.RecipientId ||
            envelope.MembershipDigest != context.MembershipDigest || envelope.SignerDeviceId != context.SignerDeviceId ||
            envelope.RecipientId != localRecipientId || envelope.RecipientPublicKey != localRecipientPublic ||
            signer.DeviceId != context.SignerDeviceId)
            throw new CryptographicException("Key envelope binding rejected.");
        byte[] signature = [];
        byte[] ciphertext = [];
        try
        {
            signature = DeviceEncoding.Decode(envelope.Signature, 64);
            ciphertext = DeviceEncoding.DecodeVariable(envelope.Ciphertext, 48, 6144);
        }
        catch (FormatException exception)
        {
            CryptographicOperations.ZeroMemory(signature);
            CryptographicOperations.ZeroMemory(ciphertext);
            throw new CryptographicException("Key envelope encoding rejected.", exception);
        }
        if (ciphertext.Length is < 48 or > 6144 || DeviceEncoding.Encode(ciphertext) != envelope.Ciphertext)
            throw new CryptographicException("Key envelope size rejected.");
        var unsigned = envelope with { Signature = "" };
        var signed = DomainBytes("FG-SODIUM-BOX-1", CanonicalHeader(unsigned));
        var publicKey = DeviceEncoding.Decode(signer.SigningPublicKey, 32);
        try
        {
            if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(
                NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, publicKey,
                    NSec.Cryptography.KeyBlobFormat.RawPublicKey), signed, signature))
                throw new CryptographicException("Key envelope signer rejected.");
            var clear = open(ciphertext);
            try { return ParsePayload(clear, context); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature); CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(signed); CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    public static byte[] CanonicalHeader(SignedKeyEnvelope e) => Canonical(new
    {
        suite = e.Suite, generationId = e.GenerationId, keyEpoch = e.KeyEpoch, recipientId = e.RecipientId,
        recipientPublicKey = e.RecipientPublicKey, membershipDigest = e.MembershipDigest,
        ciphertext = e.Ciphertext, signerDeviceId = e.SignerDeviceId
    });

    public static byte[] ParsePayload(ReadOnlySpan<byte> clear, KeyEnvelopeContext context)
    {
        context.Validate();
        var clearCopy = clear.ToArray();
        try { return ParsePayloadCore(clearCopy, context); }
        finally { CryptographicOperations.ZeroMemory(clearCopy); }
    }

    private static byte[] ParsePayloadCore(byte[] clear, KeyEnvelopeContext context)
    {
        using var document = JsonDocument.Parse(clear, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        var expected = new HashSet<string>(["protocol", "accountId", "generationId", "keyEpoch", "recipientId", "membershipDigest", "contentKey"], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object) throw new CryptographicException("Invalid key payload.");
        foreach (var field in root.EnumerateObject()) if (!seen.Add(field.Name)) throw new CryptographicException("Duplicate key payload field.");
        if (!seen.SetEquals(expected) || !root.GetProperty("protocol").ValueEquals(PayloadProtocol) ||
            !root.GetProperty("accountId").ValueEquals(context.AccountId) ||
            !root.GetProperty("generationId").ValueEquals(context.GenerationId) ||
            root.GetProperty("keyEpoch").GetInt64() != context.KeyEpoch ||
            !root.GetProperty("recipientId").ValueEquals(context.RecipientId) ||
            !root.GetProperty("membershipDigest").ValueEquals(context.MembershipDigest))
            throw new CryptographicException("Key payload context rejected.");
        var key = DeviceEncoding.Decode(root.GetProperty("contentKey").GetString()!, 32);
        var canonical = Canonical(new { protocol = PayloadProtocol, accountId = context.AccountId,
            generationId = context.GenerationId, keyEpoch = context.KeyEpoch, recipientId = context.RecipientId,
            membershipDigest = context.MembershipDigest, contentKey = DeviceEncoding.Encode(key) });
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(canonical, clear))
                throw new CryptographicException("Key payload must be canonical UTF-8 JSON.");
            return key;
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    private static byte[] Canonical<T>(T value) => new JsonCanonicalizer(JsonSerializer.Serialize(value)).GetEncodedUTF8();
    private static byte[] DomainBytes(string domain, ReadOnlySpan<byte> payload)
    {
        var prefix = Encoding.ASCII.GetBytes(domain); var result = new byte[prefix.Length + 1 + payload.Length];
        prefix.CopyTo(result, 0); payload.CopyTo(result.AsSpan(prefix.Length + 1)); return result;
    }
}
