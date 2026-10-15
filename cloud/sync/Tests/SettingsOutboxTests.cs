using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsOutboxTests
{
    private static SettingsOutbox NewState() => new(new(3, GlobalSettings.Default));
    private static SettingsOutbox Pending() => NewState().Edit(GlobalSettings.Default with { Theme = "light" })
        .Prepare(_ => [1, 2, 3, 4]); // Opaque transport bytes; these tests do not claim E2EE verification.

    [Fact]
    public void RetryPreservesOperationAndExactEnvelopeWithoutEncryptingAgain()
    {
        var pending = Pending();
        Assert.Same(pending, pending.Prepare(_ => throw new InvalidOperationException("Must not re-encrypt a retry")));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, pending.InFlight!.GetEnvelope());
    }

    [Fact]
    public void LegacyOutboxWithoutLocalOverridePropertyDefaultsToEmptyOverrides()
    {
        const string legacy = """
            {"confirmed":{"revision":3,"values":{"defaultDurationMinutes":50,"theme":"dark","uiLanguage":"es"},"deleted":false},"draft":null,"inFlight":null,"conflicts":0,"draftDeleted":false,"syncConsentEnabled":false,"cursor":0}
            """;
        var state = JsonSerializer.Deserialize<SettingsOutbox>(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(state);
        Assert.Equal(new LocalSettingsOverrides(), state!.LocalOverrides);
        state.Validate();
    }

    [Fact]
    public void PreparedGlobalWriteUsesOnlyTheGlobalDraftWhenLocalOverridesExist()
    {
        var global = new GlobalSettings(25, "dark", "es");
        var state = NewState() with { LocalOverrides = new(Theme: "light", UiLanguage: "en", DefaultDurationMinutes: 90) };
        var prepared = state.Edit(global).Prepare(intent => JsonSerializer.SerializeToUtf8Bytes(intent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(global, prepared.InFlight!.Intent.Values);
        var envelopeText = Encoding.UTF8.GetString(prepared.InFlight.GetEnvelope());
        Assert.DoesNotContain("localOverrides", envelopeText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultDurationMinutes\":90", envelopeText, StringComparison.Ordinal);
    }

    [Fact]
    public void AckForInFlightWritePreservesEditsMadeDuringUpload()
    {
        var pending = Pending();
        var newer = pending.Edit(pending.Draft! with { UiLanguage = "en" });
        var intent = pending.InFlight!.Intent;
        var acknowledged = newer.Acknowledge(intent.OperationId, intent.ProposedRevision, intent.Values);
        Assert.Null(acknowledged.InFlight); Assert.True(acknowledged.Pending);
        Assert.Equal("en", acknowledged.Draft!.UiLanguage);
        Assert.Equal("es", acknowledged.Confirmed.Values.UiLanguage);
        var next = acknowledged.Prepare(_ => [9]);
        Assert.Equal(4, next.InFlight!.Intent.BaseRevision);
        Assert.NotEqual(intent.OperationId, next.InFlight.Intent.OperationId);
    }

    [Fact]
    public void WrongAckNeverClearsDraftAndMatchingAckCanBeRepeated()
    {
        var pending = Pending(); var intent = pending.InFlight!.Intent;
        Assert.Same(pending, pending.Acknowledge(Guid.NewGuid(), 4, intent.Values));
        var acknowledged = pending.Acknowledge(intent.OperationId, 4, intent.Values);
        Assert.False(acknowledged.Pending);
        Assert.Same(acknowledged, acknowledged.Acknowledge(intent.OperationId, 4, intent.Values));
    }

    [Fact]
    public void CasConflictRequiresDeliberateResolutionBeforeAnotherUpload()
    {
        var pending = NewState().Edit(GlobalSettings.Default with { DefaultDurationMinutes = 25 }).Prepare(_ => [1]);
        var remote = new SettingsSnapshot(4, GlobalSettings.Default with { DefaultDurationMinutes = 90 });
        Assert.Throws<InvalidOperationException>(() => pending.Receive(remote));
        var conflicted = pending.Receive(remote, casRejected: true);
        Assert.Null(conflicted.InFlight); Assert.Equal(SettingsFields.Duration, conflicted.Conflicts);
        Assert.Throws<InvalidOperationException>(() => conflicted.Prepare(_ => [1]));
        var repeated = conflicted.Receive(remote);
        Assert.Equal(SettingsFields.Duration, repeated.Conflicts);
        Assert.Throws<InvalidOperationException>(() => repeated.Prepare(_ => [1]));
        Assert.NotNull(conflicted.Resolve(conflicted.Draft!).Prepare(_ => [1]).InFlight);
    }

    [Fact]
    public async Task RealDpapiRestartRecoversPendingWriteAndExactRetryPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scope = Scope(); var path = Path.Combine(root, "outbox.dpapi"); var state = Pending();
            await new SettingsOutboxVault(path, scope).SaveAsync(state);
            Assert.DoesNotContain("\"theme\"", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
            var restored = await new SettingsOutboxVault(path, scope).LoadAsync();
            Assert.Equal(state, restored); Assert.True(restored!.Pending);
            Assert.Equal(state.InFlight!.GetEnvelope(), restored.InFlight!.GetEnvelope());
            var intent = restored.InFlight.Intent;
            await new SettingsOutboxVault(path, scope).SaveAsync(restored.Acknowledge(intent.OperationId, 4, intent.Values));
            Assert.False((await new SettingsOutboxVault(path, scope).LoadAsync())!.Pending);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task WrongScopeAndTamperAreRejectedWithoutDiscardingVault()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scope = Scope(); var path = Path.Combine(root, "outbox.dpapi");
            await new SettingsOutboxVault(path, scope).SaveAsync(Pending());
            var wrong = new SettingsOutboxVault(path, scope with { GenerationId = Guid.NewGuid().ToString("D") });
            await Assert.ThrowsAsync<CryptographicException>(() => wrong.LoadAsync());
            Assert.True(File.Exists(path));
            var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
            await Assert.ThrowsAsync<CryptographicException>(() => new SettingsOutboxVault(path, scope).LoadAsync());
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CancelledSavePreservesLastCommittedState()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), Scope());
            var first = Pending(); await vault.SaveAsync(first);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.SaveAsync(first.Edit(first.Draft! with { UiLanguage = "en" }), cancelled.Token));
            Assert.Equal(first, await vault.LoadAsync());
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentVaultInstancesDoNotLoseIndependentEdits()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "outbox.dpapi"); var scope = Scope();
            var first = new SettingsOutboxVault(path, scope); var second = new SettingsOutboxVault(path, scope);
            await first.SaveAsync(NewState());
            await Task.WhenAll(
                Task.Run(() => first.UpdateAsync(state => state!.Edit((state.Draft ?? state.Confirmed.Values) with { Theme = "light" }))),
                Task.Run(() => second.UpdateAsync(state => state!.Edit((state.Draft ?? state.Confirmed.Values) with { UiLanguage = "en" }))));
            var final = await first.LoadAsync();
            Assert.Equal("light", final!.Draft!.Theme); Assert.Equal("en", final.Draft.UiLanguage);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OrphanedTemporaryWriteDoesNotSilentlyBecomeAnEmptyOutbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "outbox.dpapi");
            await File.WriteAllBytesAsync(path + ".interrupted.tmp", [1, 2, 3]);
            await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsOutboxVault(path, Scope()).LoadAsync());
            Assert.True(File.Exists(path + ".interrupted.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SettingsSyncScope Scope() => new("https://identity.example.invalid/realms/test",
        Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
}
