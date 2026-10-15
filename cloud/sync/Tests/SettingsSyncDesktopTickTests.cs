using System.Security.Cryptography;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsSyncDesktopTickTests
{
    [Fact]
    public async Task DisabledConsentDoesNotTouchLocalPreferences()
    {
        using var fixture = new Fixture();
        await fixture.Vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: false));
        var preferences = new DesktopSettingsSyncStore(fixture.PreferencesPath);
        var tick = new SettingsSyncDesktopTick(fixture.Worker(enabled: false, persistedConsent: false),
            fixture.Vault, preferences, new());

        Assert.Equal(SettingsSyncStatus.Disabled, await tick.RunAsync());
        Assert.Equal(0, fixture.Transport.Reads);
        Assert.False(File.Exists(fixture.PreferencesPath));
    }

    [Fact]
    public async Task PreferenceProjectionFailureReturnsStorageAttentionAfterWorkerTick()
    {
        using var fixture = new Fixture();
        await fixture.Vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: true));
        var preferences = new DesktopSettingsSyncStore(fixture.PreferencesPath);
        var tick = new SettingsSyncDesktopTick(fixture.Worker(enabled: true, persistedConsent: true),
            fixture.Vault, preferences, new());

        var error = await Assert.ThrowsAsync<SettingsTransportException>(() => tick.RunAsync());
        Assert.Equal(SettingsTransportFailure.Storage, error.Failure);
        Assert.Equal(1, fixture.Transport.Reads);
        Assert.False(File.Exists(fixture.PreferencesPath));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "FocusGuard-DesktopTick-" + Guid.NewGuid().ToString("N"));
        private readonly DeviceIdentity identity = DeviceIdentity.Create();
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        private readonly DeviceAccountContext account;
        private readonly string entityId = Guid.NewGuid().ToString("D");
        private readonly SettingsSyncCryptography crypto;

        public string PreferencesPath => Path.Combine(root, "preferences.json");
        public EmptyTransport Transport { get; } = new();
        public SettingsOutboxVault Vault { get; }

        public Fixture()
        {
            Directory.CreateDirectory(root);
            account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
                DeviceEncoding.Encode(SHA256.HashData("desktop-tick-test"u8)), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            var scope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, identity.Public.DeviceId);
            Vault = new SettingsOutboxVault(Path.Combine(root, "outbox.dpapi"), scope);
            crypto = new SettingsSyncCryptography(account, entityId, 1, 1, identity, key,
                (_, device) => device == identity.Public.DeviceId ? identity.Public : throw new CryptographicException("Unknown author."));
        }

        public SettingsSyncWorker Worker(bool enabled, bool persistedConsent) => new(Vault, crypto, Transport,
            () => enabled, _ => Task.FromResult(persistedConsent));

        public void Dispose()
        {
            crypto.Dispose();
            identity.Dispose();
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class EmptyTransport : ISettingsDocumentTransport
    {
        public int Reads { get; private set; }
        public Task<byte[]?> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult<byte[]?>(null);
        }

        public Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("No settings write is pending in this test.");
        }
    }
}
