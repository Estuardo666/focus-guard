using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsSyncWorkerTests
{
    [Fact]
    public async Task DisabledConsentDoesNotReadOrPublish()
    {
        using var fixture = new Fixture(); await fixture.Initialize(); fixture.Enabled = false;
        Assert.Equal(SettingsSyncStatus.Disabled, await fixture.Worker().TickAsync());
        Assert.Equal(0, fixture.Transport.Reads); Assert.Equal(0, fixture.Transport.Writes);
    }

    [Fact]
    public async Task PublishObservesDurableEncryptedPreparationAndConfirmsCommittedRead()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        fixture.Transport.BeforePublish = async (id, bytes) =>
        {
            var durable = await new SettingsOutboxVault(fixture.Path, fixture.Scope).LoadAsync();
            Assert.Equal(id, durable!.InFlight!.Intent.OperationId);
            Assert.Equal(bytes, durable.InFlight.GetEnvelope());
        };
        Assert.Equal(SettingsSyncStatus.Idle, await fixture.Worker().TickAsync());
        var state = await fixture.Vault.LoadAsync();
        Assert.False(state!.Pending); Assert.Equal("light", state.Confirmed.Values.Theme);
        Assert.Equal(1, fixture.Transport.Writes);
    }

    [Fact]
    public async Task LostResponseRecoveredAfterRestartWithoutSecondPublication()
    {
        using var fixture = new Fixture(); await fixture.Initialize(); fixture.Transport.LoseResponse = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Worker().TickAsync());
        var before = await fixture.Vault.LoadAsync(); Assert.NotNull(before!.InFlight);
        fixture.Transport.LoseResponse = false;
        Assert.Equal(SettingsSyncStatus.Idle, await fixture.Worker().TickAsync());
        Assert.Equal(1, fixture.Transport.Writes); Assert.False((await fixture.Vault.LoadAsync())!.Pending);
    }

    [Fact]
    public async Task WithdrawingConsentDuringReadPreventsPublication()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        fixture.Transport.AfterRead = () => fixture.Enabled = false;
        Assert.Equal(SettingsSyncStatus.Disabled, await fixture.Worker().TickAsync());
        Assert.Equal(0, fixture.Transport.Writes); Assert.True((await fixture.Vault.LoadAsync())!.Pending);
    }

    [Fact]
    public async Task ReceiptWithoutCommittedEchoKeepsPending()
    {
        using var fixture = new Fixture(); await fixture.Initialize(); fixture.Transport.HideEcho = true;
        Assert.Equal(SettingsSyncStatus.Pending, await fixture.Worker().TickAsync());
        Assert.NotNull((await fixture.Vault.LoadAsync())!.InFlight);
    }

    [Fact]
    public async Task AccountCursorPersistsWithTheOutboxAndRejectsRollback()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        fixture.Transport.Cursor = 7;
        Assert.Equal(SettingsSyncStatus.Idle, await fixture.Worker().TickAsync());
        var state = await fixture.Vault.LoadAsync();
        Assert.NotNull(state);
        Assert.Equal(7, state.Cursor);

        var reads = fixture.Transport.Reads;
        fixture.Transport.Cursor = 6;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Worker().TickAsync());
        Assert.Equal(reads, fixture.Transport.Reads);
        Assert.Equal(state, await fixture.Vault.LoadAsync());
    }

    [Fact]
    public async Task UnchangedCursorSkipsDocumentAndOlderServersFallBackToSnapshotRead()
    {
        using var fixture = new Fixture();
        await fixture.Vault.SaveAsync(new SettingsOutbox(new(1, GlobalSettings.Default),
            SyncConsentEnabled: true)
        { Cursor = 5 });
        fixture.Transport.Cursor = 5;
        Assert.Equal(SettingsSyncStatus.Idle, await fixture.Worker().TickAsync());
        Assert.Equal(1, fixture.Transport.CursorReads);
        Assert.Equal(0, fixture.Transport.Reads);

        fixture.Transport.Cursor = null;
        fixture.Transport.Stored = fixture.Crypto.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default));
        Assert.Equal(SettingsSyncStatus.Idle, await fixture.Worker().TickAsync());
        Assert.Equal(1, fixture.Transport.Reads);
        Assert.Equal(5, (await fixture.Vault.LoadAsync())!.Cursor);
    }

    [Fact]
    public async Task EditCommittedDuringCursorPollIsReconciledInsteadOfSkipped()
    {
        using var fixture = new Fixture();
        await fixture.Vault.SaveAsync(new SettingsOutbox(new(1, GlobalSettings.Default), SyncConsentEnabled: true)
        { Cursor = 5 });
        fixture.Transport.Cursor = 5;
        fixture.Transport.Stored = fixture.PeerDocument(GlobalSettings.Default);
        var cursorPollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCursorPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.BeforeCursorRead = async token =>
        {
            cursorPollStarted.TrySetResult();
            await finishCursorPoll.Task.WaitAsync(token);
        };
        var tick = fixture.Worker().TickAsync();

        try
        {
            await cursorPollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Vault.UpdateAsync(state => state!.Edit(GlobalSettings.Default with { Theme = "light" }));
        }
        finally { finishCursorPoll.TrySetResult(); }

        Assert.Equal(SettingsSyncStatus.Idle, await tick.WaitAsync(TimeSpan.FromSeconds(5)));
        var state = await fixture.Vault.LoadAsync();
        Assert.NotNull(state);
        Assert.Equal("light", state.Confirmed.Values.Theme);
        Assert.Equal(2, state.Confirmed.Revision);
        Assert.False(state.Pending);
        Assert.Equal(1, fixture.Transport.CursorReads);
        Assert.Equal(2, fixture.Transport.Reads);
        Assert.Equal(1, fixture.Transport.Writes);
    }

    [Fact]
    public async Task OlderApiCursor404FallsBackToDocumentReadInTheSameWorkerTick()
    {
        using var fixture = new Fixture();
        await fixture.Vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: true)
        { Cursor = 5 });
        var envelope = fixture.Crypto.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default));
        var document = JsonSerializer.Deserialize<EncryptedContentDocument>(envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var targets = new List<string>();
        var sender = new HandlerSender(fixture.Crypto.Account.AccountBinding, request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            targets.Add(path);
            if (path == "/v1/sync/cursor")
                return Task.FromResult(Reply(HttpStatusCode.NotFound, new { error = "NotFound" }));

            Assert.Equal("/v1/documents/" + fixture.EntityId, path);
            return Task.FromResult(Reply(HttpStatusCode.OK, new { revision = 1, document }, "1"));
        });
        var session = DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32));
        var transport = new SettingsDocumentHttpTransport(new Uri("https://api.example.invalid/"), sender,
            fixture.Crypto.Account, fixture.Author, session, fixture.EntityId);
        var worker = new SettingsSyncWorker(fixture.Vault, fixture.Crypto, transport, () => true);

        Assert.Equal(SettingsSyncStatus.Idle, await worker.TickAsync());
        Assert.Equal(["/v1/sync/cursor", "/v1/documents/" + fixture.EntityId], targets);
        var state = await fixture.Vault.LoadAsync();
        Assert.Equal(1, state!.Confirmed.Revision);
        Assert.Equal(5, state.Cursor);
    }

    [Fact]
    public async Task InvalidRemoteSignatureNeverChangesLocalDraftOrPublishes()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var valid = fixture.Crypto.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default));
        var modified = JsonNode.Parse(valid)!;
        var cipher = modified["ciphertext"]!.GetValue<string>();
        modified["ciphertext"] = (cipher[0] == 'A' ? 'B' : 'A') + cipher[1..];
        fixture.Transport.Stored = Encoding.UTF8.GetBytes(modified.ToJsonString());
        var before = await fixture.Vault.LoadAsync();
        await Assert.ThrowsAsync<CryptographicException>(() => fixture.Worker().TickAsync());
        Assert.Equal(before, await fixture.Vault.LoadAsync()); Assert.Equal(0, fixture.Transport.Writes);
    }

    [Fact]
    public async Task PersistentConsentIsReadAgainAfterRemoteReadAndStopsPublish()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var consent = new SettingsConsentStore(fixture.Vault);
        await consent.SetAsync(false, false);
        var worker = new SettingsSyncWorker(fixture.Vault, fixture.Crypto, fixture.Transport, () => true, consent.IsEnabledAsync);
        Assert.Equal(SettingsSyncStatus.Disabled, await worker.TickAsync()); Assert.Equal(0, fixture.Transport.Reads);
        await consent.SetAsync(true, true);
        fixture.Transport.AfterRead = () => consent.SetAsync(false, false).GetAwaiter().GetResult();
        Assert.Equal(SettingsSyncStatus.Disabled, await worker.TickAsync()); Assert.Equal(0, fixture.Transport.Writes);
        Assert.True((await fixture.Vault.LoadAsync())!.Pending);
    }

    [Fact]
    public async Task MissingOptionalCallbackCannotBypassStoredDisabledConsent()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await new SettingsConsentStore(fixture.Vault).SetAsync(false, false);
        Assert.Equal(SettingsSyncStatus.Disabled, await fixture.Worker().TickAsync());
        Assert.Equal(0, fixture.Transport.Reads); Assert.Equal(0, fixture.Transport.Writes);
    }

    [Fact]
    public async Task WrongAccountScopeIsRejectedBeforeTransport()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        var wrongVault = new SettingsOutboxVault(fixture.Path, fixture.Scope with { AccountId = Guid.NewGuid().ToString("D") });
        var worker = new SettingsSyncWorker(wrongVault, fixture.Crypto, fixture.Transport, () => true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.TickAsync());
        Assert.Equal(0, fixture.Transport.Reads); Assert.Equal(0, fixture.Transport.Writes);
    }

    [Fact]
    public async Task PeerConflictStaysBlockedAcrossPollsUntilExplicitResolution()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        await fixture.Vault.UpdateAsync(state => state!.Edit(GlobalSettings.Default with { DefaultDurationMinutes = 25 }));
        fixture.Transport.Stored = fixture.PeerDocument(GlobalSettings.Default with { DefaultDurationMinutes = 90 });
        var worker = fixture.Worker();
        Assert.Equal(SettingsSyncStatus.Conflict, await worker.TickAsync());
        Assert.Equal(SettingsSyncStatus.Conflict, await worker.TickAsync());
        Assert.Equal(0, fixture.Transport.Writes);
        await fixture.Vault.UpdateAsync(state => state!.Resolve(state.Draft!));
        Assert.Equal(SettingsSyncStatus.Idle, await worker.TickAsync());
        Assert.Equal(1, fixture.Transport.Writes);
        Assert.Equal(25, (await fixture.Vault.LoadAsync())!.Confirmed.Values.DefaultDurationMinutes);
    }

    [Fact]
    public async Task EditsDuringPublicationRemainQueuedAfterAck()
    {
        using var fixture = new Fixture(); await fixture.Initialize();
        fixture.Transport.BeforePublish = async (_, _) =>
            _ = await fixture.Vault.UpdateAsync(state => state!.Edit(state.Draft! with { UiLanguage = "en" }));
        Assert.Equal(SettingsSyncStatus.Pending, await fixture.Worker().TickAsync());
        var state = await fixture.Vault.LoadAsync();
        Assert.Equal("en", state!.Draft!.UiLanguage); Assert.Null(state.InFlight);
        Assert.Equal("es", state.Confirmed.Values.UiLanguage);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FocusGuard-Worker-" + Guid.NewGuid().ToString("N"));
        private readonly DeviceIdentity author = DeviceIdentity.Create();
        private readonly DeviceIdentity peer = DeviceIdentity.Create();
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        private readonly string entityId = Guid.NewGuid().ToString("D");
        public SettingsOutboxVault Vault { get; }
        public SettingsSyncScope Scope { get; }
        public string Path { get; }
        public string EntityId => entityId;
        public DeviceIdentity Author => author;
        public SettingsSyncCryptography Crypto { get; }
        public ControlledTransport Transport { get; } = new();
        public bool Enabled { get; set; } = true;
        public Fixture()
        {
            Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "outbox.dpapi");
            var account = new DeviceAccountContext("https://identity.example.invalid/realms/test",
                DeviceEncoding.Encode(SHA256.HashData("fixture-subject"u8)), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            Scope = new(account.Issuer, account.AccountId, account.GenerationId, author.Public.DeviceId);
            Vault = new(Path, Scope);
            // Synthetic trusted history lookup, not a test of the real membership-chain verifier.
            Crypto = new(account, entityId, 1, 1, author, key,
                (_, device) => device == author.Public.DeviceId ? author.Public :
                    device == peer.Public.DeviceId ? peer.Public : throw new CryptographicException("Unknown fixture author"));
        }
        public byte[] PeerDocument(GlobalSettings values) => SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 0, values),
            new(Scope.AccountId, Scope.GenerationId, entityId, GlobalSettings.DocumentKind, 0, 1, 1, 1), key, peer);
        public Task Initialize() => Vault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: true)
            .Edit(GlobalSettings.Default with { Theme = "light" }));
        public SettingsSyncWorker Worker() => new(Vault, Crypto, Transport, () => Enabled);
        public void Dispose() { Crypto.Dispose(); author.Dispose(); peer.Dispose(); CryptographicOperations.ZeroMemory(key); Directory.Delete(root, true); }
    }

    private sealed class HandlerSender(string accountBinding, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : ISettingsRequestSender
    {
        public string? AccountBinding { get; } = accountBinding;

        public Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> factory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var request = factory(DeviceEncoding.Encode(SHA256.HashData("synthetic-access-token"u8)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-access-token");
            return handler(request);
        }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, object body, string? etag = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue("\"" + etag + "\"");
        return response;
    }

    private sealed class ControlledTransport : ISettingsDocumentTransport
    {
        public byte[]? Stored { get; set; }
        public long? LastSeenCursor { get; set; }
        public long? Cursor { get; set; }
        public int CursorReads { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public bool LoseResponse { get; set; }
        public bool HideEcho { get; set; }
        public Func<CancellationToken, Task>? BeforeCursorRead { get; set; }
        public Action? AfterRead { get; set; }
        public Func<Guid, byte[], Task>? BeforePublish { get; set; }
        public async Task<long?> ReadCursorAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            CursorReads++;
            if (BeforeCursorRead is { } callback) await callback(token);
            LastSeenCursor = Cursor;
            return Cursor;
        }
        public Task<byte[]?> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Reads++; AfterRead?.Invoke();
            return Task.FromResult(HideEcho ? null : Stored?.ToArray());
        }
        public async Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Writes++;
            if (BeforePublish is { } callback) await callback(operationId, bytes.ToArray());
            Stored = bytes.ToArray();
            if (LoseResponse) throw new IOException("Synthetic lost response after commit");
            return SettingsPublishResult.Stored;
        }
    }
}
