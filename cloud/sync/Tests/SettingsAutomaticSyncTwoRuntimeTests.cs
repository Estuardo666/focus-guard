using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsAutomaticSyncTwoRuntimeTests
{
    [Fact]
    public async Task TwoIsolatedRuntimesConvergeAcrossEditsAndReconnectSignals()
    {
        using var firstIdentity = DeviceIdentity.Create();
        using var secondIdentity = DeviceIdentity.Create();
        var identities = new Dictionary<string, DevicePublicIdentity>(StringComparer.Ordinal)
        {
            [firstIdentity.Public.DeviceId] = firstIdentity.Public,
            [secondIdentity.Public.DeviceId] = secondIdentity.Public
        };
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
            DeviceEncoding.Encode(SHA256.HashData("two-runtime-test-account"u8)), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var entityId = Guid.NewGuid().ToString("D");
        var contentKey = RandomNumberGenerator.GetBytes(32);
        using var firstCrypto = new SettingsSyncCryptography(account, entityId, 1, 1, firstIdentity, contentKey,
            (_, deviceId) => identities.TryGetValue(deviceId, out var identity) ? identity : throw new CryptographicException("Unknown device."));
        using var secondCrypto = new SettingsSyncCryptography(account, entityId, 1, 1, secondIdentity, contentKey,
            (_, deviceId) => identities.TryGetValue(deviceId, out var identity) ? identity : throw new CryptographicException("Unknown device."));
        var firstScope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, firstIdentity.Public.DeviceId);
        var secondScope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, secondIdentity.Public.DeviceId);
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-TwoRuntime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, firstScope);
        var secondDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, secondScope);
        Assert.NotEqual(firstDirectory, secondDirectory);

        var firstVault = NewVault(firstDirectory, firstScope);
        var secondVault = NewVault(secondDirectory, secondScope);
        await firstVault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default)));
        await secondVault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default)));
        var firstConsent = new SettingsConsentStore(firstVault);
        var secondConsent = new SettingsConsentStore(secondVault);
        var sharedServer = new EncryptedSettingsServer(account.AccountId, account.GenerationId, entityId, identities);
        var firstIdle = NewSignal();
        var secondIdle = NewSignal();
        var firstRuntime = NewRuntime(firstConsent, firstVault, firstCrypto,
            new ScopedTransport(sharedServer), new SettingsRetryCheckpointStore(Path.Combine(firstDirectory, "retry.dpapi"), firstScope), firstIdle);
        var secondRuntime = NewRuntime(secondConsent, secondVault, secondCrypto,
            new ScopedTransport(sharedServer), new SettingsRetryCheckpointStore(Path.Combine(secondDirectory, "retry.dpapi"), secondScope), secondIdle);

        using var stop = new CancellationTokenSource();
        Task? firstLoop = null;
        Task? secondLoop = null;
        try
        {
            await firstRuntime.EnableAsync(ownerConfirmed: true);
            await secondRuntime.EnableAsync(ownerConfirmed: true);
            firstLoop = firstRuntime.RunAsync(stop.Token);
            secondLoop = secondRuntime.RunAsync(stop.Token);
            await firstIdle.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondIdle.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var firstValues = GlobalSettings.Default with { Theme = "light" };
            await firstVault.UpdateAsync(state => state!.Edit(firstValues));
            Assert.True(firstRuntime.NotifyLocalSettingsChanged());
            await sharedServer.WaitForRevisionAsync(1, stop.Token);
            await WaitForConfirmedAsync(firstVault, state => state.Confirmed.Revision == 1 && state.Confirmed.Values == firstValues && !state.Pending);

            Assert.True(secondRuntime.NotifyConnectivityRestored());
            await WaitForConfirmedAsync(secondVault, state => state.Confirmed.Revision == 1 && state.Confirmed.Values == firstValues && !state.Pending);

            var combinedValues = firstValues with { UiLanguage = "en" };
            await secondVault.UpdateAsync(state => state!.Edit(combinedValues));
            Assert.True(secondRuntime.NotifyLocalSettingsChanged());
            await sharedServer.WaitForRevisionAsync(2, stop.Token);
            await WaitForConfirmedAsync(secondVault, state => state.Confirmed.Revision == 2 && state.Confirmed.Values == combinedValues && !state.Pending);

            Assert.True(firstRuntime.NotifyConnectivityRestored());
            var firstFinal = await WaitForConfirmedAsync(firstVault,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values == combinedValues && !state.Pending);
            var secondFinal = await secondVault.LoadAsync();
            Assert.Equal(firstFinal.Confirmed, secondFinal!.Confirmed);
            Assert.Equal(GlobalSettings.Default.DefaultDurationMinutes, firstFinal.Confirmed.Values.DefaultDurationMinutes);
            Assert.Equal(2, sharedServer.Revision);
            Assert.True(await firstConsent.IsEnabledAsync());
            Assert.True(await secondConsent.IsEnabledAsync());

            var ciphertext = sharedServer.ReadCommittedDocument();
            Assert.NotNull(ciphertext);
            var wireText = Encoding.UTF8.GetString(ciphertext);
            Assert.DoesNotContain("\"theme\"", wireText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"uiLanguage\"", wireText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"defaultDurationMinutes\"", wireText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            stop.Cancel();
            if (firstLoop is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstLoop);
            if (secondLoop is not null) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondLoop);
            await firstRuntime.DisposeAsync();
            await secondRuntime.DisposeAsync();
            CryptographicOperations.ZeroMemory(contentKey);
            Directory.Delete(root, recursive: true);
        }
    }

    private static SettingsOutboxVault NewVault(string directory, SettingsSyncScope scope) =>
        new(Path.Combine(directory, "outbox.dpapi"), scope);

    private static SettingsAutomaticSyncRuntime NewRuntime(SettingsConsentStore consent, SettingsOutboxVault vault,
        SettingsSyncCryptography crypto, ISettingsDocumentTransport transport, SettingsRetryCheckpointStore checkpoint,
        TaskCompletionSource idle) => new(consent,
        (runtimeEnabled, persistedConsent) => new SettingsSyncWorker(vault, crypto, transport, runtimeEnabled, persistedConsent).TickAsync,
        checkpointStore: checkpoint,
        report: (result, _) =>
        {
            if (result.Status == SettingsRetryStatus.Idle) idle.TrySetResult();
            return Task.CompletedTask;
        });

    private static async Task<SettingsOutbox> WaitForConfirmedAsync(SettingsOutboxVault vault, Func<SettingsOutbox, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await vault.LoadAsync();
            if (state is not null && predicate(state)) return state;
            await Task.Delay(20);
        }
        throw new TimeoutException("The isolated sync runtime did not converge in time.");
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ScopedTransport(EncryptedSettingsServer server) : ISettingsDocumentTransport
    {
        public Task<byte[]?> ReadAsync(CancellationToken token) => server.ReadAsync(token);
        public Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token) =>
            server.PublishAsync(envelope, token);
    }

    private sealed class EncryptedSettingsServer(string accountId, string generationId, string entityId,
        IReadOnlyDictionary<string, DevicePublicIdentity> authors)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        private readonly object sync = new();
        private byte[]? document;
        private long revision;
        private TaskCompletionSource changed = NewSignal();

        public long Revision { get { lock (sync) return revision; } }

        public Task<byte[]?> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (sync) return Task.FromResult(document?.ToArray());
        }

        public Task<SettingsPublishResult> PublishAsync(ReadOnlyMemory<byte> envelope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var candidate = JsonSerializer.Deserialize<EncryptedContentDocument>(envelope.Span, Json)
                ?? throw new InvalidDataException("The test server received an empty envelope.");
            if (candidate.KeyEpoch != 1 || candidate.MembershipRevision != 1)
                throw new CryptographicException("The test server rejected an unexpected key or membership epoch.");
            if (!authors.TryGetValue(candidate.AuthorDeviceId, out var author))
                throw new CryptographicException("Unknown settings author.");
            var context = new ContentDocumentContext(accountId, generationId, entityId, GlobalSettings.DocumentKind,
                candidate.BaseRevision, candidate.ProposedRevision, candidate.KeyEpoch, candidate.MembershipRevision);
            if (!ContentDocumentCodec.VerifySignedHeader(candidate, context, author))
                throw new CryptographicException("The test server rejected the signed envelope.");

            TaskCompletionSource notify;
            lock (sync)
            {
                token.ThrowIfCancellationRequested();
                if (candidate.BaseRevision != revision) return Task.FromResult(SettingsPublishResult.RevisionConflict);
                if (candidate.ProposedRevision != revision + 1) throw new InvalidDataException("Invalid test server revision.");
                document = envelope.ToArray();
                revision = candidate.ProposedRevision;
                notify = changed;
                changed = NewSignal();
            }
            notify.TrySetResult();
            return Task.FromResult(SettingsPublishResult.Stored);
        }

        public async Task WaitForRevisionAsync(long expected, CancellationToken token)
        {
            while (true)
            {
                Task wait;
                lock (sync)
                {
                    if (revision >= expected) return;
                    wait = changed.Task;
                }
                await wait.WaitAsync(TimeSpan.FromSeconds(5), token);
            }
        }

        public byte[]? ReadCommittedDocument()
        {
            lock (sync) return document?.ToArray();
        }
    }
}
