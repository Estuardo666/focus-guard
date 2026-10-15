using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Hosting;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostMembershipHistoryTests
{
    [Fact]
    public async Task HostRetainsAndValidatesApiHistoryAgainstDeviceVaultAnchor()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-MembershipHistory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var owner = DeviceIdentity.Create();
        using var newlyJoined = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("host-membership-test"u8)), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var ownerMember = new DeviceMember(owner.Public.DeviceId, owner.Public.SigningPublicKey, owner.Public.EncryptionPublicKey,
            "Active", ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var newMember = new DeviceMember(newlyJoined.Public.DeviceId, newlyJoined.Public.SigningPublicKey,
            newlyJoined.Public.EncryptionPublicKey, "Active", ["Read", "WriteSettings"]);
        var firstUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId, 0, 1, 1,
            DeviceEncoding.Encode(new byte[32]), [ownerMember], owner.Public.SigningPublicKey,
            owner.Public.EncryptionPublicKey, owner.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var first = Sign(firstUnsigned, owner);
        var firstDigest = MembershipProtocol.Digest(first);
        var secondUnsigned = first with
        {
            BaseRevision = 1,
            ProposedRevision = 2,
            PreviousDigest = firstDigest,
            Members = [ownerMember, newMember],
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var second = Sign(secondUnsigned, owner);
        var secondDigest = MembershipProtocol.Digest(second);
        var contentKey = RandomNumberGenerator.GetBytes(32);

        try
        {
            var sessionBytes = RandomNumberGenerator.GetBytes(32);
            string session;
            try { session = DeviceEncoding.Encode(sessionBytes); }
            finally { CryptographicOperations.ZeroMemory(sessionBytes); }
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(owner, null, account, contentKey, 2, secondDigest, CancellationToken.None, session);
            using var loaded = await vault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The synthetic device vault did not load.");

            var response = new HostAccountService.MembershipSnapshot(second, secondDigest, [])
            { MembershipChain = [first, second] };
            var json = JsonSerializer.SerializeToUtf8Bytes(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var decoded = JsonSerializer.Deserialize<HostAccountService.MembershipSnapshot>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(2, decoded.MembershipChain!.Length);
            HostAccountService.ValidateMembershipSnapshot(decoded, account, loaded.Material);

            var truncated = decoded with { MembershipChain = [second] };
            var rejected = Assert.ThrowsAny<Exception>(() =>
                HostAccountService.ValidateMembershipSnapshot(truncated, account, loaded.Material));
            Assert.Equal("DeviceMembershipUpdateRequired", rejected.GetType().GetProperty("Code")!.GetValue(rejected));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            Directory.Delete(root, recursive: true);
        }
    }

    private static MembershipDocument Sign(MembershipDocument document, DeviceIdentity signer)
    {
        var canonical = MembershipProtocol.CanonicalMembership(document);
        try { return document with { Signature = signer.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }
}
