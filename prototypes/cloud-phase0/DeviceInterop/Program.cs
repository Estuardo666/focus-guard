using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Devices;

// Synthetic test-vector harness only. It reads secrets from stdin and emits public data/boolean status only.
var input = JsonSerializer.Deserialize<Input>(Console.In.ReadToEnd(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Interop vector required.");
if (input.Mode == "device-identity")
{
    var signingSeed = DeviceEncoding.Decode(input.SignerSigningSeed, 32);
    var boxSeed = DeviceEncoding.Decode(input.SignerBoxSeed, 32);
    var (boxPublic, boxSecret) = SodiumBox.FromSeed(boxSeed);
    try
    {
        using var identity = DeviceIdentity.ImportForInterop(input.SignerDeviceId, signingSeed, boxSecret);
        if (DeviceEncoding.Encode(boxPublic) != identity.Public.EncryptionPublicKey)
            throw new CryptographicException("X25519 public-key derivation mismatch.");
        var payload = DeviceEncoding.Canonical(new { deviceId = input.SignerDeviceId,
            purpose = "android-cross-platform", text = "café" });
        try
        {
            var signature = identity.Sign("FG-REQUEST-1", payload);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "PASS",
                signingPublicKey = identity.Public.SigningPublicKey,
                encryptionPublicKey = identity.Public.EncryptionPublicKey,
                fingerprint = identity.Public.Fingerprint,
                payload = DeviceEncoding.Encode(payload),
                signature
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
    finally
    {
        CryptographicOperations.ZeroMemory(signingSeed);
        CryptographicOperations.ZeroMemory(boxSeed);
        CryptographicOperations.ZeroMemory(boxPublic);
        CryptographicOperations.ZeroMemory(boxSecret);
    }
    return;
}
if (input.Mode == "membership-chain-vector")
{
    var memberSignerSeed = DeviceEncoding.Decode(input.SignerSigningSeed, 32);
    var memberSignerBoxSeed = DeviceEncoding.Decode(input.SignerBoxSeed, 32);
    var memberRecipientSeed = DeviceEncoding.Decode(input.RecipientSigningSeed, 32);
    var memberRecipientBoxSeed = DeviceEncoding.Decode(input.RecipientBoxSeed, 32);
    var memberContentKey = DeviceEncoding.Decode(input.ContentKey, 32);
    var memberRecoverySigning = Enumerable.Range(201, 32).Select(value => (byte)value).ToArray();
    var memberRecoveryEncryption = Enumerable.Range(233, 32).Select(value => (byte)value).ToArray();
    var memberSignerBox = SodiumBox.FromSeed(memberSignerBoxSeed);
    var memberRecipientBox = SodiumBox.FromSeed(memberRecipientBoxSeed);
    try
    {
        using var membershipSigner = DeviceIdentity.ImportForInterop(input.SignerDeviceId, memberSignerSeed, memberSignerBox.SecretKey);
        using var membershipRecipient = DeviceIdentity.ImportForInterop(input.RecipientDeviceId, memberRecipientSeed, memberRecipientBox.SecretKey);
        var allRoles = new[] { "ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings" };
        var initialUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", input.AccountId, input.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]),
            [new DeviceMember(membershipSigner.Public.DeviceId, membershipSigner.Public.SigningPublicKey,
                membershipSigner.Public.EncryptionPublicKey, "Active", allRoles)],
            DeviceEncoding.Encode(memberRecoverySigning), DeviceEncoding.Encode(memberRecoveryEncryption), membershipSigner.Public.DeviceId, "");
        var initialSignature = membershipSigner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(initialUnsigned));
        var initial = initialUnsigned with { Signature = initialSignature };
        var initialDigest = MembershipProtocol.Digest(initial);

        var currentUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", input.AccountId, input.GenerationId,
            1, 2, 2, initialDigest,
            [new DeviceMember(membershipSigner.Public.DeviceId, membershipSigner.Public.SigningPublicKey,
                 membershipSigner.Public.EncryptionPublicKey, "Active", allRoles),
             new DeviceMember(membershipRecipient.Public.DeviceId, membershipRecipient.Public.SigningPublicKey,
                 membershipRecipient.Public.EncryptionPublicKey, "Pending", [])],
            DeviceEncoding.Encode(memberRecoverySigning), DeviceEncoding.Encode(memberRecoveryEncryption), membershipSigner.Public.DeviceId, "");
        var currentSignature = membershipSigner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(currentUnsigned));
        var current = currentUnsigned with { Signature = currentSignature };
        var digest = MembershipProtocol.Digest(current);

        var unauthorizedCurrentUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", input.AccountId, input.GenerationId,
            1, 2, 2, initialDigest,
            [new DeviceMember(membershipSigner.Public.DeviceId, membershipSigner.Public.SigningPublicKey,
                 membershipSigner.Public.EncryptionPublicKey, "Revoked", []),
             new DeviceMember(membershipRecipient.Public.DeviceId, membershipRecipient.Public.SigningPublicKey,
                 membershipRecipient.Public.EncryptionPublicKey, "Active", allRoles)],
            DeviceEncoding.Encode(memberRecoverySigning), DeviceEncoding.Encode(memberRecoveryEncryption), membershipRecipient.Public.DeviceId, "");
        var unauthorizedSignature = membershipRecipient.Sign("FG-MEMBERSHIP-1",
            MembershipProtocol.CanonicalMembership(unauthorizedCurrentUnsigned));
        var unauthorizedCurrent = unauthorizedCurrentUnsigned with { Signature = unauthorizedSignature };

        var envelopeContext = new KeyEnvelopeContext(input.AccountId, input.GenerationId, 2,
            input.RecipientDeviceId, digest, input.SignerDeviceId);
        var envelope = SignedKeyEnvelopeCodec.SealForDevice(memberContentKey, envelopeContext,
            membershipRecipient.Public.EncryptionPublicKey, membershipSigner);
        var documentContext = new ContentDocumentContext(input.AccountId, input.GenerationId,
            "44444444-4444-4444-8444-444444444444", "profile.settings", 0, 1, 2, 2);
        var documentClear = Encoding.UTF8.GetBytes("{\"durationMinutes\":25,\"theme\":\"mist\"}");
        EncryptedContentDocument contentDocument;
        try { contentDocument = ContentDocumentCodec.Encrypt(documentClear, memberContentKey, documentContext, membershipSigner); }
        finally { CryptographicOperations.ZeroMemory(documentClear); }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", initial, current, digest, envelope,
            unauthorizedCurrent, contentDocument },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    finally
    {
        CryptographicOperations.ZeroMemory(memberSignerSeed); CryptographicOperations.ZeroMemory(memberSignerBoxSeed);
        CryptographicOperations.ZeroMemory(memberRecipientSeed); CryptographicOperations.ZeroMemory(memberRecipientBoxSeed);
        CryptographicOperations.ZeroMemory(memberContentKey); CryptographicOperations.ZeroMemory(memberRecoverySigning);
        CryptographicOperations.ZeroMemory(memberRecoveryEncryption); CryptographicOperations.ZeroMemory(memberSignerBox.PublicKey);
        CryptographicOperations.ZeroMemory(memberSignerBox.SecretKey); CryptographicOperations.ZeroMemory(memberRecipientBox.PublicKey);
        CryptographicOperations.ZeroMemory(memberRecipientBox.SecretKey);
    }
    return;
}
var signerSeed = DeviceEncoding.Decode(input.SignerSigningSeed, 32);
var signerBoxSeed = DeviceEncoding.Decode(input.SignerBoxSeed, 32);
var recipientSeed = DeviceEncoding.Decode(input.RecipientSigningSeed, 32);
var recipientBoxSeed = DeviceEncoding.Decode(input.RecipientBoxSeed, 32);
var contentKey = DeviceEncoding.Decode(input.ContentKey, 32);
var signerBox = SodiumBox.FromSeed(signerBoxSeed);
var recipientBox = SodiumBox.FromSeed(recipientBoxSeed);
try
{
    using var signer = DeviceIdentity.ImportForInterop(input.SignerDeviceId, signerSeed, signerBox.SecretKey);
    using var recipient = DeviceIdentity.ImportForInterop(input.RecipientDeviceId, recipientSeed, recipientBox.SecretKey);
    if (DeviceEncoding.Encode(signerBox.PublicKey) != signer.Public.EncryptionPublicKey ||
        DeviceEncoding.Encode(recipientBox.PublicKey) != recipient.Public.EncryptionPublicKey)
        throw new CryptographicException("X25519 public-key interoperability mismatch.");
    var context = new KeyEnvelopeContext(input.AccountId, input.GenerationId, input.KeyEpoch,
        input.RecipientId, input.MembershipDigest, input.SignerDeviceId);

    if (input.Mode is "node-to-csharp" or "android-key-envelope-to-csharp")
    {
        var envelope = input.Envelope ?? throw new InvalidOperationException("Envelope required.");
        var opened = SignedKeyEnvelopeCodec.OpenForDevice(envelope, context, signer.Public, recipient);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(opened, contentKey))
                throw new CryptographicException("Interop content-key mismatch.");
        }
        finally { CryptographicOperations.ZeroMemory(opened); }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS",
            direction = input.Mode == "android-key-envelope-to-csharp" ? "android-to-csharp" : "node-to-csharp" }));
        return;
    }

    if (input.Mode == "csharp-to-node")
    {
        var envelope = SignedKeyEnvelopeCodec.SealForDevice(contentKey, context,
            recipient.Public.EncryptionPublicKey, signer);
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", signer = signer.Public,
            recipient = recipient.Public, envelope }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return;
    }

    if (input.Mode is "node-document-to-csharp" or "android-document-to-csharp")
    {
        var envelope = input.ContentEnvelope ?? throw new InvalidOperationException("Content document required.");
        var documentContext = input.DocumentContext ?? throw new InvalidOperationException("Content context required.");
        var expected = Encoding.UTF8.GetBytes(input.DocumentJson ?? throw new InvalidOperationException("Canonical document required."));
        var opened = ContentDocumentCodec.Decrypt(envelope, documentContext, contentKey, signer.Public);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(opened, expected))
                throw new CryptographicException("Content document interop mismatch.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(opened);
            CryptographicOperations.ZeroMemory(expected);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS",
            direction = input.Mode == "android-document-to-csharp" ? "android-to-csharp" : "node-to-csharp" }));
        return;
    }

    if (input.Mode == "csharp-document-to-node")
    {
        var documentContext = input.DocumentContext ?? throw new InvalidOperationException("Content context required.");
        var clear = Encoding.UTF8.GetBytes(input.DocumentJson ?? throw new InvalidOperationException("Canonical document required."));
        try
        {
            var envelope = ContentDocumentCodec.Encrypt(clear, contentKey, documentContext, signer);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", envelope },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
        return;
    }
    throw new InvalidOperationException("Unknown interop direction.");
}
finally
{
    CryptographicOperations.ZeroMemory(signerSeed); CryptographicOperations.ZeroMemory(signerBoxSeed);
    CryptographicOperations.ZeroMemory(recipientSeed); CryptographicOperations.ZeroMemory(recipientBoxSeed);
    CryptographicOperations.ZeroMemory(contentKey); CryptographicOperations.ZeroMemory(signerBox.PublicKey);
    CryptographicOperations.ZeroMemory(signerBox.SecretKey); CryptographicOperations.ZeroMemory(recipientBox.PublicKey);
    CryptographicOperations.ZeroMemory(recipientBox.SecretKey);
}

internal sealed record Input(string Mode, string AccountId, string GenerationId, long KeyEpoch,
    string RecipientId, string MembershipDigest, string SignerDeviceId, string RecipientDeviceId,
    string SignerSigningSeed, string SignerBoxSeed, string RecipientSigningSeed, string RecipientBoxSeed,
    string ContentKey, SignedKeyEnvelope? Envelope, ContentDocumentContext? DocumentContext = null,
    string? DocumentJson = null, EncryptedContentDocument? ContentEnvelope = null);
