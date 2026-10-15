using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.Webpki.JsonCanonicalizer;

namespace FocusGuard.Devices;

public sealed record DeviceMember(string DeviceId, string SigningPublicKey, string EncryptionPublicKey,
    string Status, string[] Roles);

public sealed record MembershipDocument(string Protocol, string AccountId, string GenerationId,
    long BaseRevision, long ProposedRevision, long KeyEpoch, string PreviousDigest,
    DeviceMember[] Members, string RecoverySigningPublicKey, string RecoveryEncryptionPublicKey,
    string SignerDeviceId, string Signature);

public sealed record BootstrapChallengeRequest(string AccountId, string GenerationId, string DeviceId,
    string SigningPublicKey, string EncryptionPublicKey, string? RecoverySigningPublicKey,
    string? RecoveryEncryptionPublicKey);

public sealed record BootstrapChallengeProof(string ChallengeId, string AccountId, string GenerationId,
    string DeviceId, string Nonce);

public sealed record DeviceCapabilities(string Platform, string ClientVersion, string Normal, string Strict,
    string[] Channels, bool StrictPersistenceVerified, bool RecoveryReady)
{
    public void Validate()
    {
        if (Platform is not ("Windows" or "Linux" or "Android" or "macOS" or "iOS") ||
            string.IsNullOrWhiteSpace(ClientVersion) || ClientVersion.Length > 32 || ClientVersion.Any(char.IsControl) ||
            Normal is not ("Real" or "Limited" or "Simulated" or "Unsupported") ||
            Strict is not ("Real" or "Limited" or "Simulated" or "Unsupported") ||
            Channels is null || Channels.Length > 2 || Channels.Distinct(StringComparer.Ordinal).Count() != Channels.Length ||
            Channels.Any(c => c is not ("Applications" or "Websites")))
            throw new ArgumentException("Invalid device capabilities.");
        if (StrictPersistenceVerified && (Platform != "Windows" || Strict != "Real"))
            throw new ArgumentException("Strict persistence is not verified for this platform.");
    }
}

public sealed record PendingPairingSummary(string PairingId, string DeviceId, string SigningPublicKey,
    string EncryptionPublicKey, DeviceCapabilities Capabilities, string Fingerprint, DateTimeOffset ExpiresAtUtc);
public sealed record BootstrapPairingProof(string PairingId, BootstrapChallengeProof Challenge,
    DeviceCapabilities Capabilities);
public sealed record DeviceRequestProof(string Method, string RequestTarget, string AccountId, string GenerationId,
    string DeviceId, string RequestId, long TimestampUnixMilliseconds, string AccessTokenDigest, string BodyDigest);
public sealed record PairingApprovalRequest(string PairingId, MembershipDocument Membership, SignedKeyEnvelope[] KeyEnvelopes);
public sealed record RevokeDeviceRequest(string DeviceId, MembershipDocument Membership, SignedKeyEnvelope[] KeyEnvelopes);
public sealed record PairingStateResponse(string PairingId, string DeviceId, string State, DateTimeOffset ExpiresAtUtc,
    string Fingerprint, string MembershipDigest, MembershipDocument? Membership, SignedKeyEnvelope[]? KeyEnvelopes)
{
    public MembershipDocument[] MembershipChain { get; init; } = [];
    public MembershipDocument? CurrentMembership { get; init; }
    public string CurrentMembershipDigest { get; init; } = "";
    public SignedKeyEnvelope[] CurrentKeyEnvelopes { get; init; } = [];
    public MembershipDocument[] CurrentMembershipChain { get; init; } = [];
}
public sealed record ConfirmPairingRequest(string PairingId, string MembershipDigest, string DeviceSession);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record RenewDeviceSessionRequest(string AccountId, string GenerationId, string DeviceId,
    string MembershipDigest, string DeviceSession);

public static class MembershipProtocol
{
    private const string MembershipDomain = "FG-MEMBERSHIP-1";
    public const string ChallengeProofDomain = "FG-BOOTSTRAP-CHALLENGE-1";
    private static readonly byte[] EmptyDigest = new byte[32];
    private static readonly string[] InitialRoles = ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"];

    public static byte[] CanonicalChallenge(BootstrapChallengeProof proof) =>
        new JsonCanonicalizer(JsonSerializer.Serialize(proof)).GetEncodedUTF8();

    public static byte[] CanonicalPairingProof(BootstrapPairingProof proof)
    {
        DeviceEncoding.RequireId(proof.PairingId);
        proof.Capabilities.Validate();
        return new JsonCanonicalizer(JsonSerializer.Serialize(new
        {
            protocol = "FG-PAIRING-1", pairingId = proof.PairingId,
            challengeId = proof.Challenge.ChallengeId, accountId = proof.Challenge.AccountId,
            generationId = proof.Challenge.GenerationId, deviceId = proof.Challenge.DeviceId,
            nonce = proof.Challenge.Nonce, capabilities = new
            {
                platform = proof.Capabilities.Platform, clientVersion = proof.Capabilities.ClientVersion,
                normal = proof.Capabilities.Normal, strict = proof.Capabilities.Strict,
                channels = proof.Capabilities.Channels.Order(StringComparer.Ordinal),
                strictPersistenceVerified = proof.Capabilities.StrictPersistenceVerified,
                recoveryReady = proof.Capabilities.RecoveryReady
            }
        })).GetEncodedUTF8();
    }

    public static byte[] CanonicalDeviceRequestProof(DeviceRequestProof proof)
    {
        DeviceEncoding.RequireId(proof.AccountId); DeviceEncoding.RequireId(proof.GenerationId);
        DeviceEncoding.RequireId(proof.DeviceId); DeviceEncoding.RequireId(proof.RequestId);
        if (proof.Method is not ("GET" or "POST") || string.IsNullOrEmpty(proof.RequestTarget) || proof.RequestTarget.Length > 2048 ||
            proof.RequestTarget.Any(char.IsControl) || proof.TimestampUnixMilliseconds < 1)
            throw new ArgumentException("Invalid signed request metadata.");
        _ = DeviceEncoding.Decode(proof.AccessTokenDigest, 32); _ = DeviceEncoding.Decode(proof.BodyDigest, 32);
        return new JsonCanonicalizer(JsonSerializer.Serialize(new
        {
            protocol = "FG-REQUEST-1", method = proof.Method, requestTarget = proof.RequestTarget,
            accountId = proof.AccountId, generationId = proof.GenerationId, deviceId = proof.DeviceId,
            requestId = proof.RequestId, timestampUnixMilliseconds = proof.TimestampUnixMilliseconds,
            accessTokenDigest = proof.AccessTokenDigest, bodyDigest = proof.BodyDigest
        })).GetEncodedUTF8();
    }

    public static byte[] CanonicalPairingApprovalBody(PairingApprovalRequest body) =>
        new JsonCanonicalizer(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))).GetEncodedUTF8();

    public static byte[] CanonicalRevokeDeviceBody(RevokeDeviceRequest body)
    {
        DeviceEncoding.RequireId(body.DeviceId);
        ArgumentNullException.ThrowIfNull(body.Membership);
        ArgumentNullException.ThrowIfNull(body.KeyEnvelopes);
        return new JsonCanonicalizer(JsonSerializer.Serialize(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))).GetEncodedUTF8();
    }

    public static byte[] CanonicalConfirmPairingBody(ConfirmPairingRequest body) =>
        new JsonCanonicalizer(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))).GetEncodedUTF8();

    public static byte[] CanonicalRenewDeviceSessionBody(RenewDeviceSessionRequest body)
    {
        DeviceEncoding.RequireId(body.AccountId); DeviceEncoding.RequireId(body.GenerationId); DeviceEncoding.RequireId(body.DeviceId);
        _ = DeviceEncoding.Decode(body.MembershipDigest, 32);
        var session = DeviceEncoding.Decode(body.DeviceSession, 32);
        CryptographicOperations.ZeroMemory(session);
        return new JsonCanonicalizer(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))).GetEncodedUTF8();
    }

    public static bool VerifyChallengeProof(BootstrapChallengeProof proof, string signature,
        DevicePublicIdentity proposedDevice)
    {
        if (proof is null) return false;
        try
        {
            DeviceEncoding.RequireId(proof.ChallengeId);
            DeviceEncoding.RequireId(proof.AccountId);
            DeviceEncoding.RequireId(proof.GenerationId);
            if (proof.DeviceId != proposedDevice.DeviceId || DeviceEncoding.Decode(proof.Nonce, 32).Length != 32) return false;
            return DeviceIdentity.Verify(proposedDevice, ChallengeProofDomain, CanonicalChallenge(proof), signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    public static byte[] CanonicalMembership(MembershipDocument membership)
    {
        ValidateMembership(membership, requireSignature: false);
        var unsigned = new
        {
            protocol = membership.Protocol, accountId = membership.AccountId, generationId = membership.GenerationId,
            baseRevision = membership.BaseRevision, proposedRevision = membership.ProposedRevision, keyEpoch = membership.KeyEpoch,
            previousDigest = membership.PreviousDigest, members = membership.Members.Select(m => new
            {
                deviceId = m.DeviceId, signingPublicKey = m.SigningPublicKey,
                encryptionPublicKey = m.EncryptionPublicKey, status = m.Status, roles = m.Roles
            }),
            recoverySigningPublicKey = membership.RecoverySigningPublicKey,
            recoveryEncryptionPublicKey = membership.RecoveryEncryptionPublicKey, signerDeviceId = membership.SignerDeviceId
        };
        return new JsonCanonicalizer(JsonSerializer.Serialize(unsigned)).GetEncodedUTF8();
    }

    public static void ValidateMembership(MembershipDocument m) => ValidateMembership(m, requireSignature: true);

    private static void ValidateMembership(MembershipDocument m, bool requireSignature)
    {
        if (m is null || m.Protocol != MembershipDomain || m.BaseRevision < 0 ||
            m.ProposedRevision != m.BaseRevision + 1 || m.ProposedRevision > 9_007_199_254_740_991 ||
            m.KeyEpoch is < 1 or > 9_007_199_254_740_991)
            throw new ArgumentException("Invalid membership revision.");
        DeviceEncoding.RequireId(m.AccountId); DeviceEncoding.RequireId(m.GenerationId); DeviceEncoding.RequireId(m.SignerDeviceId);
        var previousDigest = DeviceEncoding.Decode(m.PreviousDigest, 32);
        if (m.Members is null || m.Members.Length is < 1 or > 10 || m.Members.Any(member => member is null) ||
            m.Members.Select(member => member.DeviceId).Distinct(StringComparer.Ordinal).Count() != m.Members.Length)
            throw new ArgumentException("Invalid membership member set.");
        var roles = new HashSet<string>(["Read", "WriteSettings", "StartNormal", "StartStrict", "ManageDevices"], StringComparer.Ordinal);
        foreach (var member in m.Members)
        {
            DeviceEncoding.RequireId(member.DeviceId);
            if (member.Status is not ("Active" or "Pending" or "Paused" or "Revoked") || member.Roles is null ||
                member.Roles.Length > 5 || member.Roles.Any(role => !roles.Contains(role)) ||
                member.Roles.Distinct(StringComparer.Ordinal).Count() != member.Roles.Length)
                throw new ArgumentException("Invalid membership member or roles.");
            _ = DeviceEncoding.Decode(member.SigningPublicKey, 32);
            _ = DeviceEncoding.Decode(member.EncryptionPublicKey, 32);
        }
        if (m.BaseRevision == 0 && (m.ProposedRevision != 1 || m.KeyEpoch != 1 ||
            !CryptographicOperations.FixedTimeEquals(previousDigest, EmptyDigest) || m.Members.Length != 1 ||
            m.Members[0].DeviceId != m.SignerDeviceId || m.Members[0].Status != "Active" ||
            !m.Members[0].Roles.Order(StringComparer.Ordinal).SequenceEqual(InitialRoles, StringComparer.Ordinal)))
            throw new ArgumentException("Invalid initial membership anchor.");
        _ = DeviceEncoding.Decode(m.RecoverySigningPublicKey, 32);
        _ = DeviceEncoding.Decode(m.RecoveryEncryptionPublicKey, 32);
        if (requireSignature) _ = DeviceEncoding.Decode(m.Signature, 64);
    }

    public static bool VerifyMembership(MembershipDocument m)
    {
        try
        {
            ValidateMembership(m);
            var member = m.Members.Single(entry => entry.DeviceId == m.SignerDeviceId);
            var identity = new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey, member.EncryptionPublicKey);
            return DeviceIdentity.Verify(identity, MembershipDomain, CanonicalMembership(m), m.Signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidDataException) { return false; }
    }

    public static string Digest(MembershipDocument membership) =>
        DeviceEncoding.Encode(SHA256.HashData(DeviceEncoding.DomainBytes(MembershipDomain, CanonicalMembership(membership))));
}
