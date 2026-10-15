using System.Security.Cryptography;
using System.Text;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsResetTests
{
    [Fact]
    public void ResetRequiresConfirmationAndItsPayloadHasNoOldSettings()
    {
        var previous = new GlobalSettings(90, "light", "en");
        var state = new SettingsOutbox(new(3, previous));
        Assert.Throws<InvalidOperationException>(() => state.RequestReset(false));
        var reset = state.RequestReset(true).Prepare(_ => [1]);
        Assert.True(reset.DraftDeleted); Assert.True(reset.InFlight!.Intent.Deleted);
        Assert.Equal(GlobalSettings.Default, reset.InFlight.Intent.Values);
        Assert.Throws<ArgumentException>(() => new SettingsWriteIntent(Guid.NewGuid(), 3, previous, true).Validate());
    }

    [Fact]
    public void NormalDefaultsAckCannotConfirmDeletion()
    {
        var reset = new SettingsOutbox(new(3, GlobalSettings.Default)).RequestReset(true).Prepare(_ => [1]);
        var intent = reset.InFlight!.Intent;
        Assert.Same(reset, reset.Acknowledge(intent.OperationId, 4, GlobalSettings.Default));
        var done = reset.Acknowledge(intent.OperationId, 4, GlobalSettings.Default, deleted: true);
        Assert.True(done.Confirmed.Deleted); Assert.False(done.Pending);
    }

    [Fact]
    public void ResetRequestedDuringNormalUploadSurvivesItsAck()
    {
        var normal = new SettingsOutbox(new(3, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" }).Prepare(_ => [1]);
        var reset = normal.RequestReset(true); var intent = normal.InFlight!.Intent;
        var next = reset.Acknowledge(intent.OperationId, 4, intent.Values);
        Assert.True(next.Pending); Assert.True(next.DraftDeleted);
        Assert.True(next.Prepare(_ => [2]).InFlight!.Intent.Deleted);
    }

    [Fact]
    public void ResetCannotSilentlyMergeOverNewRemoteEdit()
    {
        var reset = new SettingsOutbox(new(3, GlobalSettings.Default)).RequestReset(true).Prepare(_ => [1]);
        var changed = reset.Receive(new(4, GlobalSettings.Default with { UiLanguage = "en" }), casRejected: true);
        Assert.True(changed.DraftDeleted); Assert.NotEqual(SettingsFields.None, changed.Conflicts);
        Assert.Throws<InvalidOperationException>(() => changed.Prepare(_ => [2]));
        Assert.NotNull(changed.RequestReset(true).Prepare(_ => [2]).InFlight);
    }

    [Fact]
    public void MalformedOrMixedDeletePayloadDoesNotResetSettings()
    {
        using var identity = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var context = new ContentDocumentContext(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), GlobalSettings.DocumentKind, 0, 1, 1, 1);
            foreach (var json in new[] { "{\"deleted\":false}", "{\"deleted\":1}", "{\"deleted\":true,\"theme\":\"light\"}" })
            {
                var clear = ContentDocumentCodec.CanonicalizeJson(Encoding.UTF8.GetBytes(json));
                try
                {
                    var document = ContentDocumentCodec.Encrypt(clear, key, context, identity);
                    var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    Assert.Throws<System.Text.Json.JsonException>(() => SettingsEnvelopeCodec.Open(bytes, context, key, identity.Public));
                }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public async Task AuthenticatedTombstoneSurvivesRealVaultAndAcknowledgesExactEcho()
    {
        using var identity = DeviceIdentity.Create(); var key = RandomNumberGenerator.GetBytes(32);
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var accountId = Guid.NewGuid().ToString("D"); var generation = Guid.NewGuid().ToString("D");
            var context = new ContentDocumentContext(accountId, generation, Guid.NewGuid().ToString("D"), GlobalSettings.DocumentKind, 3, 4, 1, 1);
            var state = new SettingsOutbox(new(3, GlobalSettings.Default with { Theme = "light" })).RequestReset(true)
                .Prepare(intent => SettingsEnvelopeCodec.Prepare(intent, context, key, identity));
            var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test", accountId, generation, identity.Public.DeviceId);
            var path = Path.Combine(root, "outbox.dpapi"); await new SettingsOutboxVault(path, scope).SaveAsync(state);
            var loaded = (await new SettingsOutboxVault(path, scope).LoadAsync())!;
            Assert.True(loaded.DraftDeleted); Assert.True(loaded.InFlight!.Intent.Deleted);
            var snapshot = SettingsEnvelopeCodec.Open(loaded.InFlight.GetEnvelope(), context, key, identity.Public);
            Assert.True(snapshot.Deleted); Assert.Equal(GlobalSettings.Default, snapshot.Values);
            var confirmed = SettingsEnvelopeCodec.ConfirmOwnWrite(loaded, loaded.InFlight.GetEnvelope(), context, key, identity.Public);
            Assert.True(confirmed.Confirmed.Deleted); Assert.False(confirmed.Pending);
            var offline = new SettingsOutbox(new(3, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" });
            Assert.NotEqual(SettingsFields.None, offline.Receive(snapshot).Conflicts);
        }
        finally { CryptographicOperations.ZeroMemory(key); Directory.Delete(root, true); }
    }
}
