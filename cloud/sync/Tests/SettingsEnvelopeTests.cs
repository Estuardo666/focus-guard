using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsEnvelopeTests
{
    private static ContentDocumentContext Context() => new(Guid.NewGuid().ToString("D"),
        Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), GlobalSettings.DocumentKind, 3, 4, 1, 2);

    [Fact]
    public async Task RealEncryptedWriteSurvivesVaultRestartAndKeepsExactRetryBytes()
    {
        using var author = DeviceIdentity.Create();
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context();
            var state = new SettingsOutbox(new(3, GlobalSettings.Default))
                .Edit(GlobalSettings.Default with { Theme = "light", UiLanguage = "en" })
                .Prepare(intent => SettingsEnvelopeCodec.Prepare(intent, context, key, author));
            var wire = state.InFlight!.GetEnvelope();
            var root = Path.Combine(Path.GetTempPath(), "FocusGuard-EncryptedSync-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test",
                    context.AccountId, context.GenerationId, author.Public.DeviceId);
                var path = Path.Combine(root, "outbox.dpapi");
                await new SettingsOutboxVault(path, scope).SaveAsync(state);
                var restored = await new SettingsOutboxVault(path, scope).LoadAsync();
                Assert.Equal(wire, restored!.InFlight!.GetEnvelope());
                Assert.Equal(state.Draft, SettingsEnvelopeCodec.Open(restored.InFlight.GetEnvelope(), context, key, author.Public).Values);
            }
            finally { Directory.Delete(root, recursive: true); }
            Assert.DoesNotContain("\"theme\"", Encoding.UTF8.GetString(wire));
            var snapshot = SettingsEnvelopeCodec.Open(wire, context, key, author.Public);
            Assert.Equal(state.Draft, snapshot.Values); Assert.Equal(4, snapshot.Revision);
            Assert.Same(state, state.Prepare(_ => throw new InvalidOperationException("Retry must reuse ciphertext")));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void LostAckRecoveryRequiresExactCiphertextAndPreservesLaterEdit()
    {
        using var author = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context();
            var pending = new SettingsOutbox(new(3, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" })
                .Prepare(intent => SettingsEnvelopeCodec.Prepare(intent, context, key, author));
            var later = pending.Edit(pending.Draft! with { UiLanguage = "en" });
            var differentCiphertext = SettingsEnvelopeCodec.Prepare(pending.InFlight!.Intent, context, key, author);
            Assert.Same(later, SettingsEnvelopeCodec.ConfirmOwnWrite(later, differentCiphertext, context, key, author.Public));
            var recovered = SettingsEnvelopeCodec.ConfirmOwnWrite(later, pending.InFlight.GetEnvelope(), context, key, author.Public);
            Assert.Null(recovered.InFlight); Assert.Equal(4, recovered.Confirmed.Revision);
            Assert.Equal("en", recovered.Draft!.UiLanguage); Assert.True(recovered.Pending);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void WrongAuthorKeyCiphertextAndRoutingAreRejected()
    {
        using var author = DeviceIdentity.Create(); using var other = DeviceIdentity.Create();
        var key = RandomNumberGenerator.GetBytes(32); var wrongKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context(); var intent = new SettingsWriteIntent(Guid.NewGuid(), 3, GlobalSettings.Default);
            var wire = SettingsEnvelopeCodec.Prepare(intent, context, key, author);
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire, context, wrongKey, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire, context, key, other.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { AccountId = Guid.NewGuid().ToString("D") }, key, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { KeyEpoch = 2 }, key, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { GenerationId = Guid.NewGuid().ToString("D") }, key, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { EntityId = Guid.NewGuid().ToString("D") }, key, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { MembershipRevision = 3 }, key, author.Public));
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(wire,
                context with { BaseRevision = 4, ProposedRevision = 5 }, key, author.Public));
            var changed = JsonNode.Parse(wire)!;
            var cipher = DeviceEncoding.Decode(changed["ciphertext"]!.GetValue<string>(), GlobalSettingsCodec.Encode(intent.Values).Length + 16);
            cipher[0] ^= 1; changed["ciphertext"] = DeviceEncoding.Encode(cipher);
            Assert.Throws<CryptographicException>(() => SettingsEnvelopeCodec.Open(
                Encoding.UTF8.GetBytes(changed.ToJsonString()), context, key, author.Public));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(wrongKey); }
    }

    [Fact]
    public void ProposalMustUseTheGlobalKindAndExactCasRevision()
    {
        using var author = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context(); var intent = new SettingsWriteIntent(Guid.NewGuid(), 3, GlobalSettings.Default);
            Assert.Throws<ArgumentException>(() => SettingsEnvelopeCodec.Prepare(intent,
                context with { Kind = "settings.selected.v1" }, key, author));
            Assert.Throws<ArgumentException>(() => SettingsEnvelopeCodec.Prepare(intent,
                context with { BaseRevision = 4, ProposedRevision = 5 }, key, author));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void ValidSignatureDoesNotPermitUnexpectedPlaintextSettings()
    {
        using var author = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context();
            var clear = ContentDocumentCodec.CanonicalizeJson("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\",\"permissions\":true}"u8);
            try
            {
                var encrypted = ContentDocumentCodec.Encrypt(clear, key, context, author);
                var wire = JsonSerializer.SerializeToUtf8Bytes(encrypted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.Throws<JsonException>(() => SettingsEnvelopeCodec.Open(wire, context, key, author.Public));
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void HistoricalKeysReadOldDocumentsButNewWritesUseCurrentEpoch()
    {
        using var author = DeviceIdentity.Create();
        var previousKey = RandomNumberGenerator.GetBytes(32); var currentKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context();
            var old = SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 3, GlobalSettings.Default), context, previousKey, author);
            var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
                DeviceEncoding.Encode(SHA256.HashData("subject"u8)), context.AccountId, context.GenerationId);
            using var crypto = new SettingsSyncCryptography(account, context.EntityId, 2, 3, author, currentKey,
                (_, _) => author.Public, new Dictionary<long, byte[]> { [1] = previousKey });
            CryptographicOperations.ZeroMemory(previousKey); // The adapter owns a defensive copy.
            Assert.Equal(GlobalSettings.Default, crypto.Open(old).Values);
            var fresh = crypto.Prepare(new(Guid.NewGuid(), 4, GlobalSettings.Default with { Theme = "light" }));
            Assert.Equal("light", SettingsEnvelopeCodec.Open(fresh, context with
            {
                BaseRevision = 4,
                ProposedRevision = 5,
                KeyEpoch = 2,
                MembershipRevision = 3
            }, currentKey, author.Public).Values.Theme);
            using var withoutHistory = new SettingsSyncCryptography(account, context.EntityId, 2, 3, author, currentKey, (_, _) => author.Public);
            Assert.Throws<CryptographicException>(() => withoutHistory.Open(old));
            crypto.Dispose(); Assert.Throws<ObjectDisposedException>(() => crypto.Open(old));
        }
        finally { CryptographicOperations.ZeroMemory(previousKey); CryptographicOperations.ZeroMemory(currentKey); }
    }

    [Fact]
    public void ExtraWireFieldsAndOversizedInputAreRejected()
    {
        using var author = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = Context();
            var wire = SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 3, GlobalSettings.Default), context, key, author);
            var changed = JsonNode.Parse(wire)!; changed["permissions"] = true;
            Assert.Throws<JsonException>(() => SettingsEnvelopeCodec.Open(Encoding.UTF8.GetBytes(changed.ToJsonString()), context, key, author.Public));
            var duplicate = "{\"protocol\":\"FG-E2EE-1\"," + Encoding.UTF8.GetString(wire)[1..];
            Assert.Throws<ArgumentException>(() => SettingsEnvelopeCodec.Open(Encoding.UTF8.GetBytes(duplicate), context, key, author.Public));
            Assert.Throws<JsonException>(() => SettingsEnvelopeCodec.Open(new byte[65_537], context, key, author.Public));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
