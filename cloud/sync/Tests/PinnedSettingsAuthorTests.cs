using System.Security.Cryptography;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class PinnedSettingsAuthorTests
{
    [Fact]
    public async Task ContextCanBeConstructedFromRealDpapiDeviceVaultAndPinnedMembership()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-SyncContext-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var owner = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var account = new DeviceAccountContext("https://identity.example.invalid/realms/test", DeviceEncoding.Encode(SHA256.HashData("subject"u8)),
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            var member = new DeviceMember(owner.Public.DeviceId, owner.Public.SigningPublicKey, owner.Public.EncryptionPublicKey, "Active",
                ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
            var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId, 0, 1, 1,
                DeviceEncoding.Encode(new byte[32]), [member], owner.Public.SigningPublicKey, owner.Public.EncryptionPublicKey,
                owner.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
            var signed = unsigned with { Signature = owner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(unsigned)) };
            var vault = new WindowsDeviceVault(Path.Combine(root, "device.dpapi"));
            await vault.SaveAsync(owner, null, account, key, 1, MembershipProtocol.Digest(signed), CancellationToken.None,
                DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32)));
            using var loaded = await vault.LoadAsync(account, owner.Public.DeviceId, CancellationToken.None);
            using var crypto = SettingsSyncContextFactory.Create(account, loaded, Guid.NewGuid().ToString("D"), [signed]);
            var wire = crypto.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default));
            Assert.Equal(GlobalSettings.Default, crypto.Open(wire).Values);
            Assert.Throws<CryptographicException>(() => SettingsSyncContextFactory.Create(account with { AccountId = Guid.NewGuid().ToString("D") },
                loaded, Guid.NewGuid().ToString("D"), [signed]));
            await vault.SaveAsync(owner, null, account, key, 1, MembershipProtocol.Digest(signed), CancellationToken.None, allowReplace: true);
            using var noSession = await vault.LoadAsync(account, owner.Public.DeviceId, CancellationToken.None);
            Assert.Throws<CryptographicException>(() => SettingsSyncContextFactory.Create(account, noSession, Guid.NewGuid().ToString("D"), [signed]));
            await vault.SaveAsync(owner, null, account, key, 1, MembershipProtocol.Digest(signed), CancellationToken.None,
                DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32)), allowReplace: true, keyEpoch: 2);
            using var wrongEpoch = await vault.LoadAsync(account, owner.Public.DeviceId, CancellationToken.None);
            Assert.Throws<CryptographicException>(() => SettingsSyncContextFactory.Create(account, wrongEpoch, Guid.NewGuid().ToString("D"), [signed]));
        }
        finally { CryptographicOperations.ZeroMemory(key); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NewDeviceOpensSettingsAuthoredBeforeItJoinedUsingVerifiedMembershipHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-SyncHistory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var firstDevice = DeviceIdentity.Create();
        using var secondDevice = DeviceIdentity.Create();
        var accountId = Guid.NewGuid().ToString("D");
        var generationId = Guid.NewGuid().ToString("D");
        var firstAccount = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("first-subject"u8)), accountId, generationId);
        var secondAccount = firstAccount with { AccountBinding = DeviceEncoding.Encode(SHA256.HashData("second-subject"u8)) };
        var firstMember = new DeviceMember(firstDevice.Public.DeviceId, firstDevice.Public.SigningPublicKey,
            firstDevice.Public.EncryptionPublicKey, "Active", ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var secondMember = new DeviceMember(secondDevice.Public.DeviceId, secondDevice.Public.SigningPublicKey,
            secondDevice.Public.EncryptionPublicKey, "Active", ["Read", "WriteSettings"]);
        var initialUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", accountId, generationId, 0, 1, 1,
            DeviceEncoding.Encode(new byte[32]), [firstMember], firstDevice.Public.SigningPublicKey,
            firstDevice.Public.EncryptionPublicKey, firstDevice.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var initial = Sign(initialUnsigned, firstDevice);
        var initialDigest = MembershipProtocol.Digest(initial);
        var joinedUnsigned = initial with
        {
            BaseRevision = 1,
            ProposedRevision = 2,
            PreviousDigest = initialDigest,
            Members = [firstMember, secondMember],
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var joined = Sign(joinedUnsigned, firstDevice);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var sessionBytes = RandomNumberGenerator.GetBytes(32);
            string deviceSession;
            try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
            finally { CryptographicOperations.ZeroMemory(sessionBytes); }
            var deviceVault = new WindowsDeviceVault(Path.Combine(root, "second-device.dpapi"));
            await deviceVault.SaveAsync(secondDevice, null, secondAccount, key, 2, MembershipProtocol.Digest(joined),
                CancellationToken.None, deviceSession);
            using var loaded = await deviceVault.LoadForAccountAsync(secondAccount.Issuer, secondAccount.AccountBinding,
                CancellationToken.None) ?? throw new InvalidOperationException("Test device vault failed to load.");

            var entityId = Guid.NewGuid().ToString("D");
            using var correctCrypto = SettingsSyncContextFactory.Create(secondAccount, loaded.Material, entityId, [initial, joined]);
            var oldSettings = new GlobalSettings(35, "light", "en");
            var oldDocument = SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 0, oldSettings),
                new(accountId, generationId, entityId, GlobalSettings.DocumentKind, 0, 1, 1, 1), key, firstDevice);
            Assert.Equal(oldSettings, correctCrypto.Open(oldDocument).Values);

            var currentDocument = correctCrypto.Prepare(new(Guid.NewGuid(), 1, oldSettings with { Theme = "dark" }));
            Assert.Equal(oldSettings with { Theme = "dark" }, correctCrypto.Open(currentDocument).Values);
            Assert.Throws<CryptographicException>(() => SettingsSyncContextFactory.Create(secondAccount, loaded.Material,
                entityId, [joined]));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(root, recursive: true);
        }
    }

    private static MembershipDocument Sign(MembershipDocument document, DeviceIdentity signer)
    {
        var canonical = MembershipProtocol.CanonicalMembership(document);
        try { return document with { Signature = signer.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    [Fact]
    public void AuthorRequiresVerifiedSignatureAndTrustedDigestAndCannotBeMutatedAfterPinning()
    {
        using var owner = DeviceIdentity.Create();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test", DeviceEncoding.Encode(SHA256.HashData("subject"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var member = new DeviceMember(owner.Public.DeviceId, owner.Public.SigningPublicKey, owner.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId, 0, 1, 1,
            DeviceEncoding.Encode(new byte[32]), [member], owner.Public.SigningPublicKey, owner.Public.EncryptionPublicKey,
            owner.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var signed = unsigned with { Signature = owner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(unsigned)) };
        var digest = MembershipProtocol.Digest(signed);
        var pinned = new PinnedSettingsAuthor(signed, account, 1, digest);
        Assert.Equal(owner.Public, pinned.Resolve(1, owner.Public.DeviceId));
        Assert.Throws<CryptographicException>(() => pinned.Resolve(2, owner.Public.DeviceId));
        Assert.Throws<CryptographicException>(() => pinned.Resolve(1, Guid.NewGuid().ToString("D")));
        Assert.Throws<CryptographicException>(() => new PinnedSettingsAuthor(signed, account, 1, DeviceEncoding.Encode(new byte[32])));
        Assert.Throws<CryptographicException>(() => new PinnedSettingsAuthor(unsigned, account, 1, digest));
        var nextUnsigned = signed with
        {
            BaseRevision = 1,
            ProposedRevision = 2,
            PreviousDigest = digest,
            Members = [member with { Roles = ["ManageDevices", "Read"] }],
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var next = nextUnsigned with { Signature = owner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(nextUnsigned)) };
        var history = new PinnedSettingsAuthor([signed, next], account, 2, MembershipProtocol.Digest(next));
        Assert.Equal(owner.Public, history.Resolve(1, owner.Public.DeviceId));
        Assert.Throws<CryptographicException>(() => history.Resolve(2, owner.Public.DeviceId));
        Assert.Throws<CryptographicException>(() => new PinnedSettingsAuthor([next], account, 2, MembershipProtocol.Digest(next)));
        var brokenUnsigned = nextUnsigned with { PreviousDigest = DeviceEncoding.Encode(new byte[32]) };
        var broken = brokenUnsigned with { Signature = owner.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(brokenUnsigned)) };
        Assert.Throws<CryptographicException>(() => new PinnedSettingsAuthor([signed, broken], account, 2, MembershipProtocol.Digest(broken)));
        signed.Members[0].Roles[0] = "WriteSettings";
        Assert.Equal(owner.Public, pinned.Resolve(1, owner.Public.DeviceId));
    }
}
