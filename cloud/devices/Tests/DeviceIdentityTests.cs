using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using FocusGuard.Devices;
using Xunit;

public sealed class DeviceIdentityTests
{
    private static readonly DeviceAccountContext Context = new(
        "https://auth.focusguard.totemmassmedia.com/realms/focusguard",
        DeviceEncoding.Encode(SHA256.HashData("owner-sub"u8)),
        "11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222");

    [Fact]
    public void DevicesHaveIndependentSigningAndEncryptionKeys()
    {
        using var first = DeviceIdentity.Create(); using var second = DeviceIdentity.Create();
        Assert.NotEqual(first.Public, second.Public);
        Assert.NotEqual(first.Public.SigningPublicKey, first.Public.EncryptionPublicKey);
        Assert.Equal(32, DeviceEncoding.Decode(first.Public.SigningPublicKey, 32).Length);
        Assert.Equal(32, DeviceEncoding.Decode(first.Public.EncryptionPublicKey, 32).Length);
        Assert.NotEqual(first.Public.Fingerprint, second.Public.Fingerprint);
    }

    [Fact]
    public void SignatureRejectsWrongDeviceDomainMessageAndMalformedEncoding()
    {
        using var identity = DeviceIdentity.Create(); using var other = DeviceIdentity.Create();
        var payload = DeviceEncoding.Canonical(new { generationId = Context.GenerationId, nonce = "test" });
        var signature = identity.Sign("FG-REQUEST-1", payload);
        Assert.True(DeviceIdentity.Verify(identity.Public, "FG-REQUEST-1", payload, signature));
        Assert.False(DeviceIdentity.Verify(other.Public, "FG-REQUEST-1", payload, signature));
        Assert.False(DeviceIdentity.Verify(identity.Public, "FG-MEMBERSHIP-1", payload, signature));
        Assert.False(DeviceIdentity.Verify(identity.Public, "FG-REQUEST-1", Encoding.UTF8.GetBytes("other"), signature));
        Assert.False(DeviceIdentity.Verify(identity.Public, "FG-REQUEST-1", payload, signature + "="));
        var bytes = DeviceEncoding.Decode(signature, 64); bytes[0] ^= 1;
        Assert.False(DeviceIdentity.Verify(identity.Public, "FG-REQUEST-1", payload, DeviceEncoding.Encode(bytes)));
    }

    [Fact]
    public void CanonicalFormatUsesJcsAndExactUnicode()
    {
        Assert.Equal("{\"a\":\"café\",\"n\":1e+30,\"z\":1}", Encoding.UTF8.GetString(DeviceEncoding.Canonical(new { z = 1, a = "café", n = 1e30 })));
        var preferences = ContentDocumentCodec.CanonicalizeJson("{\"theme\":\"Light\",\"durationMinutes\":30}"u8);
        Assert.Equal("{\"durationMinutes\":30,\"theme\":\"Light\"}", Encoding.UTF8.GetString(preferences));
        CryptographicOperations.ZeroMemory(preferences);
        Assert.Throws<ArgumentException>(() => ContentDocumentCodec.CanonicalizeJson("{\"theme\":\"Dark\",\"theme\":\"Light\"}"u8));
        var composed = SHA256.HashData(Encoding.UTF8.GetBytes("café"));
        var decomposed = SHA256.HashData(Encoding.UTF8.GetBytes("cafe\u0301"));
        Assert.NotEqual(DeviceEncoding.Encode(composed), DeviceEncoding.Encode(decomposed));
    }

    [Theory]
    [InlineData("HTTP://identity.example")]
    [InlineData("https://identity.example/?injected=1")]
    public void ContextRejectsUnsafeIdentity(string issuer) =>
        Assert.Throws<ArgumentException>(() => (Context with { Issuer = issuer }).Validate());

    [Fact]
    public void ContextRejectsInvalidAccountBinding() =>
        Assert.Throws<FormatException>(() => (Context with { AccountBinding = DeviceEncoding.Encode(new byte[31]) }).Validate());

    [Fact]
    public void DisposedIdentityCannotSign()
    {
        var identity = DeviceIdentity.Create(); identity.Dispose(); identity.Dispose();
        Assert.Throws<ObjectDisposedException>(() => identity.Sign("FG-REQUEST-1", "{}"u8));
    }

    [Fact]
    public void RecoveryKitHasIndependentSigningAndWrappingSecretsAndDetectsTranscriptionErrors()
    {
        using var kit = RecoveryKit.Generate();
        var text = kit.Export();
        Assert.StartsWith("FG-RK1.", text, StringComparison.Ordinal);
        using var restored = RecoveryKit.Import(text);
        Assert.Equal(kit.Public, restored.Public);
        Assert.Throws<FormatException>(() => RecoveryKit.Import(text[..^1] + (text[^1] == 'A' ? 'B' : 'A')));
        var payload = DeviceEncoding.Canonical(new { purpose = "recovery-key-test" });
        var signature = kit.Sign("FG-RECOVERY-TEST-1", payload);
        var publicIdentity = new DevicePublicIdentity("22222222-2222-4222-8222-222222222222",
            kit.Public.SigningPublicKey, kit.Public.EncryptionPublicKey);
        Assert.True(DeviceIdentity.Verify(publicIdentity, "FG-RECOVERY-TEST-1", payload, DeviceEncoding.Encode(signature)));
        CryptographicOperations.ZeroMemory(signature);
    }

    [Fact]
    public void SignedSealedBoxBindsAccountGenerationEpochRecipientAndMembership()
    {
        using var signer = DeviceIdentity.Create(); using var recipient = DeviceIdentity.Create();
        using var wrongRecipient = DeviceIdentity.Create(); using var recovery = RecoveryKit.Generate();
        var account = Guid.NewGuid().ToString("D"); var generation = Guid.NewGuid().ToString("D");
        var digest = DeviceEncoding.Encode(SHA256.HashData("membership-v1"u8)); var root = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = new KeyEnvelopeContext(account, generation, 1, recipient.Public.DeviceId, digest, signer.Public.DeviceId);
            var envelope = SignedKeyEnvelopeCodec.SealForDevice(root, context, recipient.Public.EncryptionPublicKey, signer);
            Assert.Equal(64, DeviceEncoding.Decode(envelope.Signature, 64).Length);
            var opened = SignedKeyEnvelopeCodec.OpenForDevice(envelope, context, signer.Public, recipient);
            try { Assert.True(CryptographicOperations.FixedTimeEquals(root, opened)); }
            finally { CryptographicOperations.ZeroMemory(opened); }
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(envelope, context with { KeyEpoch = 2 }, signer.Public, recipient));
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(envelope, context with { MembershipDigest = DeviceEncoding.Encode(SHA256.HashData("membership-v2"u8)) }, signer.Public, recipient));
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(envelope, context with { SignerDeviceId = wrongRecipient.Public.DeviceId }, signer.Public, recipient));
            var corrupt = envelope with { Ciphertext = envelope.Ciphertext[..^1] + (envelope.Ciphertext[^1] == 'A' ? 'B' : 'A') };
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(corrupt, context, signer.Public, recipient));

            var recoveryContext = context with { RecipientId = account };
            var recoveryEnvelope = SignedKeyEnvelopeCodec.SealForDevice(root, recoveryContext, recovery.Public.EncryptionPublicKey, signer);
            var recovered = SignedKeyEnvelopeCodec.OpenForRecovery(recoveryEnvelope, recoveryContext, signer.Public, recovery);
            try { Assert.True(CryptographicOperations.FixedTimeEquals(root, recovered)); }
            finally { CryptographicOperations.ZeroMemory(recovered); }
        }
        finally { CryptographicOperations.ZeroMemory(root); }
    }

    [Fact]
    public void EncryptedContentDocumentBindsCanonicalJsonContextAuthorAndEpoch()
    {
        using var author = DeviceIdentity.Create();
        using var stranger = DeviceIdentity.Create();
        var context = new ContentDocumentContext(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), "profile.settings", 4, 5, 3, 2);
        var clear = DeviceEncoding.Canonical(new { theme = "dark", durationMinutes = 50, enabled = true });
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var envelope = ContentDocumentCodec.Encrypt(clear, contentKey, context, author);
            Assert.True(ContentDocumentCodec.VerifySignedHeader(envelope, context, author.Public));
            var opened = ContentDocumentCodec.Decrypt(envelope, context, contentKey, author.Public);
            try { Assert.Equal(clear, opened); }
            finally { CryptographicOperations.ZeroMemory(opened); }

            Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(
                envelope, context with { BaseRevision = 5, ProposedRevision = 6 }, contentKey, author.Public));
            Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(envelope, context, wrongKey, author.Public));
            Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(envelope, context, contentKey, stranger.Public));
            Assert.False(ContentDocumentCodec.VerifySignedHeader(envelope with { Signature = DeviceEncoding.Encode(new byte[64]) }, context, author.Public));
            var tampered = envelope with { Ciphertext = envelope.Ciphertext[..^1] + (envelope.Ciphertext[^1] == 'A' ? 'B' : 'A') };
            Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(tampered, context, contentKey, author.Public));
            Assert.Throws<ArgumentException>(() => ContentDocumentCodec.Encrypt("{\"theme\": \"dark\"}"u8,
                contentKey, context, author));
            Assert.Throws<ArgumentException>(() => ContentDocumentCodec.Encrypt(clear, contentKey,
                context with { ProposedRevision = 7 }, author));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(wrongKey);
        }
    }

    [Fact]
    public void SecondInstallationOpensOnlyItsSignedEnvelopeAndPasswordResetKeyCannotReadOldContent()
    {
        using var owner = DeviceIdentity.Create();
        using var secondDevice = DeviceIdentity.Create();
        using var otherAccount = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var accountId = Guid.NewGuid().ToString("D");
        var generationId = Guid.NewGuid().ToString("D");
        var membershipDigest = DeviceEncoding.Encode(SHA256.HashData("approved membership"u8));
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var afterPasswordReset = RandomNumberGenerator.GetBytes(32);
        var clear = DeviceEncoding.Canonical(new { profile = "Writing", theme = "mist", blocked = new[] { "example.invalid" } });
        try
        {
            var recipientContext = new KeyEnvelopeContext(accountId, generationId, 1,
                secondDevice.Public.DeviceId, membershipDigest, owner.Public.DeviceId);
            var envelope = SignedKeyEnvelopeCodec.SealForDevice(contentKey, recipientContext,
                secondDevice.Public.EncryptionPublicKey, owner);
            var sharedKey = SignedKeyEnvelopeCodec.OpenForDevice(envelope, recipientContext, owner.Public, secondDevice);
            try
            {
                var documentContext = new ContentDocumentContext(accountId, generationId,
                    Guid.NewGuid().ToString("D"), "profile.settings", 0, 1, 1, 1);
                var document = ContentDocumentCodec.Encrypt(clear, contentKey, documentContext, owner);
                var opened = ContentDocumentCodec.Decrypt(document, documentContext, sharedKey, owner.Public);
                try { Assert.Equal(clear, opened); }
                finally { CryptographicOperations.ZeroMemory(opened); }
                Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(document,
                    documentContext with { AccountId = Guid.NewGuid().ToString("D") }, sharedKey, owner.Public));
                Assert.Throws<CryptographicException>(() => ContentDocumentCodec.Decrypt(document,
                    documentContext, afterPasswordReset, owner.Public));
            }
            finally { CryptographicOperations.ZeroMemory(sharedKey); }

            var wrongContext = recipientContext with { AccountId = Guid.NewGuid().ToString("D") };
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(
                envelope, wrongContext, owner.Public, secondDevice));
            var otherGeneration = recipientContext with { GenerationId = Guid.NewGuid().ToString("D") };
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(
                envelope, otherGeneration, owner.Public, secondDevice));
            Assert.Throws<CryptographicException>(() => SignedKeyEnvelopeCodec.OpenForDevice(
                envelope, recipientContext, owner.Public, otherAccount));

            var recoveryContext = recipientContext with { RecipientId = accountId };
            var recoveryEnvelope = SignedKeyEnvelopeCodec.SealForDevice(contentKey, recoveryContext,
                recovery.Public.EncryptionPublicKey, owner);
            var recoveredKey = SignedKeyEnvelopeCodec.OpenForRecovery(recoveryEnvelope, recoveryContext,
                owner.Public, recovery);
            try { Assert.True(CryptographicOperations.FixedTimeEquals(contentKey, recoveredKey)); }
            finally { CryptographicOperations.ZeroMemory(recoveredKey); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(afterPasswordReset);
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyVaultRetainsIdentityContentAndRecoveryWithoutInventingADeviceSession(bool hasSessionFields)
    {
        using var identity = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var digest = DeviceEncoding.Encode(SHA256.HashData("legacy-membership"u8));
        var original = identity.SerializeForVault(Context, recovery, contentKey, 1, null, 1, digest, null, null);
        byte[]? legacy = null;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            node.Remove("keyEpoch"); node.Remove("historicalContentKeys");
            if (!hasSessionFields) { node.Remove("deviceSession"); node.Remove("pendingPairingId"); }
            legacy = Encoding.UTF8.GetBytes(node.ToJsonString());
            using var restored = DeviceIdentity.DeserializeVault(legacy, Context, identity.Public.DeviceId);
            Assert.Equal(identity.Public, restored.Identity.Public);
            Assert.True(CryptographicOperations.FixedTimeEquals(contentKey, restored.AccountContentKey.Span));
            Assert.Equal(recovery.Public, restored.RecoveryKit!.Public);
            Assert.Equal(1, restored.KeyEpoch);
            Assert.Equal(1, restored.MembershipRevision);
            Assert.Equal(digest, restored.MembershipDigest);
            Assert.Null(restored.DeviceSession);
            Assert.Null(restored.PendingPairingId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(original);
            if (legacy is not null) CryptographicOperations.ZeroMemory(legacy);
        }
    }

    [Theory]
    [InlineData("keyEpoch")]
    [InlineData("deviceSession")]
    public void LegacyCompatibilityRejectsPartiallyMissingFieldGroups(string missingField)
    {
        using var identity = DeviceIdentity.Create();
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var digest = DeviceEncoding.Encode(SHA256.HashData("membership"u8));
        var original = identity.SerializeForVault(Context, null, contentKey, 1, null, 1, digest, null, null);
        byte[]? malformed = null;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            node.Remove(missingField);
            malformed = Encoding.UTF8.GetBytes(node.ToJsonString());
            Assert.Throws<InvalidDataException>(() => DeviceIdentity.DeserializeVault(malformed, Context, identity.Public.DeviceId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey); CryptographicOperations.ZeroMemory(original);
            if (malformed is not null) CryptographicOperations.ZeroMemory(malformed);
        }
    }

    [Fact]
    public async Task LegacyDpapiFileMigratesSessionWithoutReplacingIdentityContentOrRecovery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg-legacy-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "identity.dpapi");
        using var identity = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var digest = DeviceEncoding.Encode(SHA256.HashData("legacy-membership"u8));
        var currentJson = identity.SerializeForVault(Context, recovery, contentKey, 1, null, 1, digest, null, null);
        byte[]? legacyJson = null; byte[]? entropy = null; byte[]? ciphertext = null; byte[]? fileBytes = null;
        try
        {
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(currentJson)!.AsObject();
            foreach (var field in new[] { "keyEpoch", "historicalContentKeys", "deviceSession", "pendingPairingId" }) legacy.Remove(field);
            legacyJson = Encoding.UTF8.GetBytes(legacy.ToJsonString());
            entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FocusGuard.DeviceVault.v1\0" + Context.Issuer + "\0" +
                Context.AccountBinding + "\0" + Context.AccountId + "\0" + Context.GenerationId + "\0" + identity.Public.DeviceId));
            ciphertext = ProtectedData.Protect(legacyJson, entropy, DataProtectionScope.CurrentUser);
            var header = JsonSerializer.SerializeToUtf8Bytes(new { protocol = "FG-DEVICE-VAULT-HEADER-1",
                deviceId = identity.Public.DeviceId, issuer = Context.Issuer, accountBinding = Context.AccountBinding,
                accountId = Context.AccountId, generationId = Context.GenerationId });
            fileBytes = new byte[4 + header.Length + ciphertext.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(fileBytes, header.Length);
            header.CopyTo(fileBytes.AsSpan(4)); ciphertext.CopyTo(fileBytes.AsSpan(4 + header.Length));
            await File.WriteAllBytesAsync(path, fileBytes);
            var beforeRead = SHA256.HashData(fileBytes);
            var vault = new WindowsDeviceVault(path);
            using var loaded = await vault.LoadForAccountAsync(Context.Issuer, Context.AccountBinding, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Null(loaded.Material.DeviceSession);
            Assert.Equal(beforeRead, SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var sessionBytes = RandomNumberGenerator.GetBytes(32);
            string session;
            try { session = DeviceEncoding.Encode(sessionBytes); }
            finally { CryptographicOperations.ZeroMemory(sessionBytes); }
            await vault.UpdateDeviceSessionAsync(Context, identity.Public, 1, digest, session, CancellationToken.None);
            using var migrated = await vault.LoadAsync(Context, identity.Public.DeviceId, CancellationToken.None);
            Assert.Equal(identity.Public, migrated.Identity.Public);
            Assert.Equal(recovery.Public, migrated.RecoveryKit!.Public);
            Assert.True(CryptographicOperations.FixedTimeEquals(contentKey, migrated.AccountContentKey.Span));
            Assert.Equal(session, migrated.DeviceSession);
            Assert.Equal(1, migrated.KeyEpoch); Assert.Equal(1, migrated.MembershipRevision); Assert.Equal(digest, migrated.MembershipDigest);
            var afterMigration = SHA256.HashData(await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAsync<InvalidOperationException>(() => vault.UpdateDeviceSessionAsync(Context,
                identity.Public, 2, digest, session, CancellationToken.None));
            Assert.Equal(afterMigration, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally
        {
            foreach (var bytes in new[] { contentKey, currentJson, legacyJson, entropy, ciphertext, fileBytes })
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task DpapiRestoresSameDeviceAndRejectsAccountGenerationOrDeviceSubstitution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg-device-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dpapi");
        try
        {
            using var identity = DeviceIdentity.Create();
            using var recovery = RecoveryKit.Generate();
            var vault = new WindowsDeviceVault(path);
            var accountKey = RandomNumberGenerator.GetBytes(32);
            var deviceSessionBytes = RandomNumberGenerator.GetBytes(32);
            string deviceSession;
            try { deviceSession = DeviceEncoding.Encode(deviceSessionBytes); }
            finally { CryptographicOperations.ZeroMemory(deviceSessionBytes); }
            var priorContentKey = RandomNumberGenerator.GetBytes(32);
            var contentKeyHistory = new Dictionary<long, byte[]> { [1] = priorContentKey };
            var membershipDigest = DeviceEncoding.Encode(SHA256.HashData("membership"u8));
            await vault.SaveAsync(identity, recovery, Context, accountKey, 1, membershipDigest,
                CancellationToken.None, deviceSession, keyEpoch: 2, historicalContentKeys: contentKeyHistory);
            using var restored = await vault.LoadAsync(Context, identity.Public.DeviceId, CancellationToken.None);
            Assert.Equal(identity.Public, restored.Identity.Public);
            Assert.Equal(1, restored.MembershipRevision);
            Assert.Equal(membershipDigest, restored.MembershipDigest);
            Assert.NotNull(restored.RecoveryKit);
            Assert.Equal(recovery.Public, restored.RecoveryKit!.Public);
            Assert.Equal(deviceSession, restored.DeviceSession);
            Assert.Equal(2, restored.KeyEpoch);
            Assert.True(CryptographicOperations.FixedTimeEquals(priorContentKey, restored.GetContentKey(1).Span));
            Assert.True(CryptographicOperations.FixedTimeEquals(accountKey, restored.AccountContentKey.Span));
            var renewedSessionBytes = RandomNumberGenerator.GetBytes(32);
            string renewedSession;
            try { renewedSession = DeviceEncoding.Encode(renewedSessionBytes); }
            finally { CryptographicOperations.ZeroMemory(renewedSessionBytes); }
            await vault.UpdateDeviceSessionAsync(Context, identity.Public, 1, membershipDigest, renewedSession, CancellationToken.None);
            using var renewedVault = await vault.LoadAsync(Context, identity.Public.DeviceId, CancellationToken.None);
            Assert.Equal(renewedSession, renewedVault.DeviceSession);
            Assert.Equal(2, renewedVault.KeyEpoch);
            Assert.Equal(recovery.Public, renewedVault.RecoveryKit!.Public);
            Assert.True(CryptographicOperations.FixedTimeEquals(priorContentKey, renewedVault.GetContentKey(1).Span));
            Assert.True(CryptographicOperations.FixedTimeEquals(accountKey, renewedVault.AccountContentKey.Span));
            var payload = DeviceEncoding.Canonical(new { nonce = "restore" });
            Assert.True(DeviceIdentity.Verify(identity.Public, "FG-REQUEST-1", payload, restored.Identity.Sign("FG-REQUEST-1", payload)));
            using var indexed = await vault.LoadForAccountAsync(Context.Issuer, Context.AccountBinding, CancellationToken.None);
            Assert.NotNull(indexed);
            Assert.Equal(Context, indexed.Context);
            Assert.Equal(identity.Public.DeviceId, indexed.DeviceId);
            Assert.Equal(identity.Public, indexed.Material.Identity.Public);
            Assert.Null(await vault.LoadForAccountAsync(Context.Issuer,
                DeviceEncoding.Encode(SHA256.HashData("another-account"u8)), CancellationToken.None));
            CryptographicOperations.ZeroMemory(accountKey);
            CryptographicOperations.ZeroMemory(priorContentKey);
            await Assert.ThrowsAsync<InvalidDataException>(() => vault.LoadAsync(Context with { AccountBinding = DeviceEncoding.Encode(SHA256.HashData("another-account"u8)) }, identity.Public.DeviceId, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => vault.LoadAsync(Context with { Issuer = "https://other.example/realms/focusguard" }, identity.Public.DeviceId, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => vault.LoadAsync(Context with { GenerationId = Guid.NewGuid().ToString("D") }, identity.Public.DeviceId, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => vault.LoadAsync(Context, Guid.NewGuid().ToString("D"), CancellationToken.None));
            var encrypted = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain("signingSeed", Encoding.UTF8.GetString(encrypted));
            Assert.DoesNotContain("recoveryKit", Encoding.UTF8.GetString(encrypted));
            Assert.DoesNotContain(deviceSession, Encoding.UTF8.GetString(encrypted));
            encrypted[encrypted.Length / 2] ^= 1;
            await File.WriteAllBytesAsync(path, encrypted);
            await Assert.ThrowsAsync<CryptographicException>(() => vault.LoadAsync(Context, identity.Public.DeviceId, CancellationToken.None));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task ConcurrentWritersKeepExactlyOneCompleteIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg-device-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dpapi");
        try
        {
            using var first = DeviceIdentity.Create(); using var second = DeviceIdentity.Create(); using var recovery = RecoveryKit.Generate();
            var accountKey = RandomNumberGenerator.GetBytes(32); var membershipDigest = DeviceEncoding.Encode(SHA256.HashData("membership"u8));
            async Task<bool> Save(DeviceIdentity identity)
            {
                try { await new WindowsDeviceVault(path).SaveAsync(identity, recovery, Context, accountKey, 1, membershipDigest, CancellationToken.None); return true; }
                catch (IOException) { return false; }
            }
            var results = await Task.WhenAll(Task.Run(() => Save(first)), Task.Run(() => Save(second)));
            Assert.Single(results, won => won);
            var winner = results[0] ? first : second;
            using var restored = await new WindowsDeviceVault(path).LoadAsync(Context, winner.Public.DeviceId, CancellationToken.None);
            Assert.Equal(winner.Public, restored.Identity.Public);
            CryptographicOperations.ZeroMemory(accountKey);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task ExistingVaultCannotBeOverwrittenAndCancelledWriteLeavesNoSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg-device-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "identity.dpapi");
        try
        {
            using var first = DeviceIdentity.Create(); using var second = DeviceIdentity.Create(); using var recovery = RecoveryKit.Generate();
            var vault = new WindowsDeviceVault(path);
            var accountKey = RandomNumberGenerator.GetBytes(32); var digest = DeviceEncoding.Encode(SHA256.HashData("membership"u8));
            await vault.SaveAsync(first, recovery, Context, accountKey, 1, digest, CancellationToken.None);
            await Assert.ThrowsAsync<IOException>(() => vault.SaveAsync(second, recovery, Context, accountKey, 1, digest, CancellationToken.None));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.SaveAsync(second, recovery, Context, accountKey, 1, digest, new CancellationToken(true)));
            using var restored = await vault.LoadAsync(Context, first.Public.DeviceId, CancellationToken.None);
            Assert.Equal(first.Public, restored.Identity.Public);
            CryptographicOperations.ZeroMemory(accountKey);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
