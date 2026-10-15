using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class SettingsHttpsLoopbackTests
{
    [Fact]
    public async Task ActualPinnedHttpsCarriesSignedCasAndTwoRuntimeConvergenceWithoutPlaintextServer()
    {
        using var identity = DeviceIdentity.Create(); var publicIdentity = identity.Public;
        using var secondIdentity = DeviceIdentity.Create(); var secondPublicIdentity = secondIdentity.Public;
        var publicIdentities = new Dictionary<string, DevicePublicIdentity>(StringComparer.Ordinal)
        {
            [publicIdentity.DeviceId] = publicIdentity,
            [secondPublicIdentity.DeviceId] = secondPublicIdentity
        };
        var key = RandomNumberGenerator.GetBytes(32);
        const string token = "loopback-synthetic-token-first";
        const string secondToken = "loopback-synthetic-token-second";
        var digest = DeviceEncoding.Encode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        var secondDigest = DeviceEncoding.Encode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secondToken)));
        var issuer = "https://identity.example.invalid/realms/local";
        var accountId = Guid.NewGuid().ToString("D"); var generationId = Guid.NewGuid().ToString("D");
        var account = new DeviceAccountContext(issuer, digest, accountId, generationId);
        var secondAccount = new DeviceAccountContext(issuer, secondDigest, accountId, generationId);
        var entityId = Guid.NewGuid().ToString("D");
        var session = DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32));
        var secondSession = DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32));
        var sessionHash = SHA256.HashData(DeviceEncoding.Decode(session, 32));
        var secondSessionHash = SHA256.HashData(DeviceEncoding.Decode(secondSession, 32));
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-HttpsSync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scope = new SettingsSyncScope(issuer, accountId, generationId, publicIdentity.DeviceId);
        var secondScope = new SettingsSyncScope(issuer, accountId, generationId, secondPublicIdentity.DeviceId);
        var scopedDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, scope);
        var secondScopedDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, secondScope);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(san.Build());
        using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows SChannel rejects ephemeral server keys. Import this laboratory key into a transient
        // user key container (no PersistKeySet, no certificate-store installation), deleted on Dispose.
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pfx = generated.Export(X509ContentType.Pkcs12, password);
        X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        using var serverCertificate = certificate;
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 131_072;
            options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate));
        });
        await using var app = builder.Build();
        var requestIds = new ConcurrentDictionary<string, byte>(); var storageGate = new object();
        var clients = new Dictionary<string, LoopbackClient>(StringComparer.Ordinal)
        {
            [publicIdentity.DeviceId] = new(identity, token, digest, sessionHash),
            [secondPublicIdentity.DeviceId] = new(secondIdentity, secondToken, secondDigest, secondSessionHash)
        };
        EncryptedContentDocument? stored = null;
        // Synthetic token and membership authority; real TLS, HTTP, request signatures and document CAS.
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            context.Request.EnableBuffering(); using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body); context.Request.Body.Position = 0;
            var requestId = context.Request.Headers["X-FG-Request-Id"].ToString();
            var instant = DateTimeOffset.Parse(context.Request.Headers["X-FG-Timestamp"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
            var deviceId = context.Request.Headers["X-FG-Device-Id"].ToString();
            if (!clients.TryGetValue(deviceId, out var clientData)) { context.Response.StatusCode = 403; return; }
            var proof = new DeviceRequestProof(context.Request.Method, context.Request.Path.Value!, account.AccountId,
                account.GenerationId, deviceId, requestId, instant.ToUnixTimeMilliseconds(), clientData.Binding,
                DeviceEncoding.Encode(SHA256.HashData(body.ToArray())));
            var suppliedSession = SHA256.HashData(DeviceEncoding.Decode(context.Request.Headers["X-FG-Device-Session"].ToString(), 32));
            var accepted = context.Request.Headers.Authorization == "Bearer " + clientData.Token &&
                CryptographicOperations.FixedTimeEquals(suppliedSession, clientData.SessionHash) &&
                (instant - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromMinutes(1) &&
                requestIds.TryAdd(requestId, 0) && DeviceIdentity.Verify(clientData.Identity.Public, "FG-REQUEST-1",
                    MembershipProtocol.CanonicalDeviceRequestProof(proof), context.Request.Headers["X-FG-Proof"].ToString());
            CryptographicOperations.ZeroMemory(suppliedSession);
            if (!accepted) { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.MapGet("/v1/documents/{entityId:guid}", (HttpContext context, string entityId) =>
        {
            lock (storageGate)
            {
                if (stored is null || entityId != stored.EntityId) return Results.NotFound(new { error = "DocumentUnavailable" });
                context.Response.Headers.ETag = "\"" + stored.ProposedRevision + "\"";
                return Results.Json(new { revision = stored.ProposedRevision, document = stored });
            }
        });
        app.MapPost("/v1/documents/{entityId:guid}", (HttpContext context, string entityId, EncryptedContentDocument document) =>
        {
            var expected = new ContentDocumentContext(account.AccountId, account.GenerationId, entityId, GlobalSettings.DocumentKind,
                document.BaseRevision, document.ProposedRevision, 1, 1);
            if (!publicIdentities.TryGetValue(document.AuthorDeviceId, out var documentAuthor) ||
                !ContentDocumentCodec.VerifySignedHeader(document, expected, documentAuthor))
                return Results.BadRequest(new { error = "InvalidEncryptedDocument" });
            lock (storageGate)
            {
                if (document.BaseRevision != (stored?.ProposedRevision ?? 0)) return Results.Conflict(new { error = "DocumentRevisionConflict" });
                stored = document; context.Response.Headers.ETag = "\"" + document.ProposedRevision + "\"";
                return Results.Json(new { state = "Stored", revision = document.ProposedRevision, entityId }, statusCode: 201);
            }
        });
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var pin = certificate.GetCertHash(HashAlgorithmName.SHA256);
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, supplied, _, errors) => supplied is not null &&
                    (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
                    CryptographicOperations.FixedTimeEquals(pin, supplied.GetCertHash(HashAlgorithmName.SHA256))
            };
            using var client = new HttpClient(handler);
            var sender = new LoopbackSender(client, digest, token);
            var transport = new SettingsDocumentHttpTransport(new Uri(address + "/"), sender, account, identity, session, entityId);
            var secondSender = new LoopbackSender(client, secondDigest, secondToken);
            var secondTransport = new SettingsDocumentHttpTransport(new Uri(address + "/"), secondSender,
                secondAccount, secondIdentity, secondSession, entityId);
            var missing = await Assert.ThrowsAsync<SettingsTransportException>(() => transport.ReadAsync(CancellationToken.None));
            Assert.Equal(SettingsTransportFailure.AmbiguousMissing, missing.Failure);
            var initial = SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default),
                new(account.AccountId, account.GenerationId, entityId, GlobalSettings.DocumentKind, 0, 1, 1, 1), key, identity);
            var vault = new SettingsOutboxVault(Path.Combine(scopedDirectory, "outbox.dpapi"), scope);
            var firstPreferencesPath = Path.Combine(scopedDirectory, "desktop-settings.json");
            var profileThemeId = Guid.NewGuid();
            var desktopJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var firstLocalBaseline = new DesktopSettings(Theme: AppTheme.Dark, ReduceMotion: true,
                ProfileThemes: new Dictionary<Guid, AppTheme> { [profileThemeId] = AppTheme.Light },
                EnforcementEnabled: true, CyclePhaseNotifications: false, UiLanguage: "es");
            await File.WriteAllBytesAsync(firstPreferencesPath, JsonSerializer.SerializeToUtf8Bytes(firstLocalBaseline, desktopJson));
            var firstPreferences = new DesktopSettingsSyncStore(firstPreferencesPath);
            var overrides = new LocalSettingsOverrides();
            using var crypto = new SettingsSyncCryptography(account, entityId, 1, 1, identity, key,
                (_, device) => publicIdentities.TryGetValue(device, out var author) ? author : throw new CryptographicException());
            var lossy = new LostCreateResponse(transport);
            var initializer = new SettingsDocumentInitializer(vault, crypto, lossy);
            await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync(GlobalSettings.Default, false));
            Assert.Null(await vault.LoadAsync());
            await Assert.ThrowsAsync<IOException>(() => initializer.InitializeAsync(GlobalSettings.Default, true));
            Assert.NotNull((await vault.LoadAsync())!.InFlight);
            var created = await initializer.InitializeAsync(GlobalSettings.Default, true);
            Assert.Equal(1, lossy.Writes); // Authenticated readback recovered the committed create, no second POST.
            Assert.Equal(1, created.Confirmed.Revision); Assert.False(created.Pending); Assert.False(created.SyncConsentEnabled);
            Assert.Equal(SettingsPublishResult.RevisionConflict, await transport.PublishAsync(Guid.NewGuid(), initial, CancellationToken.None));
            var firstConsent = new SettingsConsentStore(vault);
            await firstConsent.SetAsync(true, true);
            var firstLocalEdits = new LocalSettingsCoordinator(vault, firstPreferences);
            await firstLocalEdits.EditSelectedAsync(GlobalSettings.Default with { Theme = "light" }, SettingsFields.Theme, overrides);
            var firstDesktopTick = new SettingsSyncDesktopTick(new SettingsSyncWorker(vault, crypto, transport, () => true),
                vault, firstPreferences, overrides);
            Assert.Equal(SettingsSyncStatus.Idle, await firstDesktopTick.RunAsync());
            var restored = await vault.LoadAsync(); Assert.False(restored!.Pending); Assert.Equal(2, restored.Confirmed.Revision);
            Assert.Equal("light", restored.Confirmed.Values.Theme);
            var firstProjected = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(firstPreferencesPath), desktopJson)!;
            Assert.Equal(AppTheme.Light, firstProjected.Theme); Assert.Equal("es", firstProjected.UiLanguage);
            Assert.Equal(50, firstProjected.LastDurationMinutes); Assert.True(firstProjected.EnforcementEnabled);
            Assert.True(firstProjected.ReduceMotion); Assert.False(firstProjected.CyclePhaseNotifications);
            Assert.Equal(AppTheme.Light, firstProjected.ProfileThemes![profileThemeId]);
            Assert.DoesNotContain("\"theme\"", JsonSerializer.Serialize(stored));

            var secondVault = new SettingsOutboxVault(Path.Combine(secondScopedDirectory, "outbox.dpapi"), secondScope);
            await secondVault.SaveAsync(new SettingsOutbox(new(0, GlobalSettings.Default)));
            var secondConsent = new SettingsConsentStore(secondVault);
            var secondPreferencesPath = Path.Combine(secondScopedDirectory, "desktop-settings.json");
            var secondLocalBaseline = new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", EnforcementEnabled: true,
                ReduceMotion: false, CyclePhaseNotifications: true);
            await File.WriteAllBytesAsync(secondPreferencesPath, JsonSerializer.SerializeToUtf8Bytes(secondLocalBaseline, desktopJson));
            var secondPreferences = new DesktopSettingsSyncStore(secondPreferencesPath);
            using var secondCrypto = new SettingsSyncCryptography(secondAccount, entityId, 1, 1, secondIdentity, key,
                (_, device) => publicIdentities.TryGetValue(device, out var author) ? author : throw new CryptographicException());
            var firstIdle = NewSignal(); var secondIdle = NewSignal();
            var firstConflict = NewSignal(); var secondConflict = NewSignal();
            var firstRuntime = NewRuntime(firstConsent, vault, crypto, transport, firstPreferences, overrides,
                new SettingsRetryCheckpointStore(Path.Combine(scopedDirectory, "retry.dpapi"), scope), firstIdle, firstConflict);
            var secondRuntime = NewRuntime(secondConsent, secondVault, secondCrypto, secondTransport, secondPreferences, overrides,
                new SettingsRetryCheckpointStore(Path.Combine(secondScopedDirectory, "retry.dpapi"), secondScope), secondIdle, secondConflict);
            using (var stop = new CancellationTokenSource())
            {
                await secondRuntime.EnableAsync(ownerConfirmed: true);
                var firstLoop = firstRuntime.RunAsync(stop.Token);
                var secondLoop = secondRuntime.RunAsync(stop.Token);
                try
                {
                    await firstIdle.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await secondIdle.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var imported = await WaitForConfirmedAsync(secondVault,
                        state => state.Confirmed.Revision == 2 && state.Confirmed.Values.Theme == "light" && !state.Pending);
                    var combined = imported.Confirmed.Values with { UiLanguage = "en" };
                    await new LocalSettingsCoordinator(secondVault, secondPreferences)
                        .EditSelectedAsync(combined, SettingsFields.Language, overrides);
                    Assert.True(secondRuntime.NotifyLocalSettingsChanged());
                    var secondFinal = await WaitForConfirmedAsync(secondVault,
                        state => state.Confirmed.Revision == 3 && state.Confirmed.Values == combined && !state.Pending);

                    Assert.True(firstRuntime.NotifyConnectivityRestored());
                    var firstFinal = await WaitForConfirmedAsync(vault,
                        state => state.Confirmed.Revision == 3 && state.Confirmed.Values == combined && !state.Pending);
                    Assert.Equal(firstFinal.Confirmed, secondFinal.Confirmed);
                    Assert.Equal(3, stored!.ProposedRevision);
                    Assert.DoesNotContain("\"uiLanguage\"", JsonSerializer.Serialize(stored));

                    await new LocalSettingsCoordinator(vault, firstPreferences).EditSelectedAsync(
                        restored.Confirmed.Values with { DefaultDurationMinutes = 40 }, SettingsFields.Duration, overrides);
                    await new LocalSettingsCoordinator(secondVault, secondPreferences).EditSelectedAsync(
                        combined with { DefaultDurationMinutes = 60 }, SettingsFields.Duration, overrides);
                    Assert.True(firstRuntime.NotifyLocalSettingsChanged());
                    Assert.True(secondRuntime.NotifyLocalSettingsChanged());
                    var reportedConflict = await Task.WhenAny(firstConflict.Task, secondConflict.Task).WaitAsync(TimeSpan.FromSeconds(5));
                    var losingRuntime = reportedConflict == firstConflict.Task ? firstRuntime : secondRuntime;
                    var losingVault = reportedConflict == firstConflict.Task ? vault : secondVault;
                    var winningRuntime = reportedConflict == firstConflict.Task ? secondRuntime : firstRuntime;
                    var winningVault = reportedConflict == firstConflict.Task ? secondVault : vault;
                    var losingState = await WaitForConfirmedAsync(losingVault,
                        state => state.Confirmed.Revision == 4 && state.Conflicts == SettingsFields.Duration && state.Draft is not null);
                    var winningState = await WaitForConfirmedAsync(winningVault,
                        state => state.Confirmed.Revision == 4 && state.Conflicts == SettingsFields.None && !state.Pending);
                    Assert.NotEqual(losingState.Confirmed.Values.DefaultDurationMinutes, losingState.Draft!.DefaultDurationMinutes);

                    var losingPreferences = ReferenceEquals(losingVault, vault) ? firstPreferences : secondPreferences;
                    var resolution = new SettingsConflictResolutionService(losingVault, losingPreferences, overrides, losingRuntime);
                    await resolution.ResolveAsync(losingState.Draft!, losingState.Draft!, losingState.Confirmed.Revision,
                        losingState.Conflicts, ownerConfirmed: true);
                    var resolvedState = await WaitForConfirmedAsync(losingVault,
                        state => state.Confirmed.Revision == 5 && state.Conflicts == SettingsFields.None && !state.Pending);
                    Assert.True(winningRuntime.NotifyConnectivityRestored());
                    var finalState = await WaitForConfirmedAsync(winningVault,
                        state => state.Confirmed.Revision == 5 && state.Conflicts == SettingsFields.None && !state.Pending);
                    Assert.Equal(resolvedState.Confirmed, finalState.Confirmed);
                    Assert.Contains(finalState.Confirmed.Values.DefaultDurationMinutes, new[] { 40, 60 });
                    Assert.Equal(5, stored!.ProposedRevision);

                    var firstProjectedFinal = JsonSerializer.Deserialize<DesktopSettings>(
                        await File.ReadAllBytesAsync(firstPreferencesPath), desktopJson)!;
                    var secondProjectedFinal = JsonSerializer.Deserialize<DesktopSettings>(
                        await File.ReadAllBytesAsync(secondPreferencesPath), desktopJson)!;
                    Assert.Equal(finalState.Confirmed.Values, DesktopSettingsProjection.Extract(firstProjectedFinal));
                    Assert.Equal(finalState.Confirmed.Values, DesktopSettingsProjection.Extract(secondProjectedFinal));
                    Assert.True(firstProjectedFinal.EnforcementEnabled); Assert.True(secondProjectedFinal.EnforcementEnabled);
                    Assert.True(firstProjectedFinal.ReduceMotion); Assert.False(secondProjectedFinal.ReduceMotion);
                    Assert.Equal(AppTheme.Light, firstProjectedFinal.ProfileThemes![profileThemeId]);
                }
                finally
                {
                    stop.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstLoop);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondLoop);
                    await firstRuntime.DisposeAsync();
                    await secondRuntime.DisposeAsync();
                }
            }

            await vault.UpdateAsync(state => state!.RequestReset(ownerConfirmed: true));
            Assert.Equal(SettingsSyncStatus.Idle, await firstDesktopTick.RunAsync());
            var reset = await vault.LoadAsync();
            Assert.True(reset!.Confirmed.Deleted); Assert.False(reset.Pending); Assert.Equal(6, reset.Confirmed.Revision);
            var resetDesktop = JsonSerializer.Deserialize<DesktopSettings>(await File.ReadAllBytesAsync(firstPreferencesPath), desktopJson)!;
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(resetDesktop));
            Assert.True(resetDesktop.EnforcementEnabled);
            Assert.DoesNotContain("deleted", JsonSerializer.Serialize(stored)); // The server only sees ciphertext.
        }
        finally
        {
            await app.StopAsync(); CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(sessionHash);
            CryptographicOperations.ZeroMemory(secondSessionHash);
            Directory.Delete(root, true);
        }
    }

    private sealed record LoopbackClient(DeviceIdentity Identity, string Token, string Binding, byte[] SessionHash);

    private sealed class LoopbackSender(HttpClient client, string binding, string bearerToken) : ISettingsRequestSender
    {
        public string? AccountBinding => binding;
        public async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> factory, CancellationToken token)
        {
            using var request = factory(binding);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
    }

    private static SettingsAutomaticSyncRuntime NewRuntime(SettingsConsentStore consent, SettingsOutboxVault vault,
        SettingsSyncCryptography crypto, ISettingsDocumentTransport transport, DesktopSettingsSyncStore preferences,
        LocalSettingsOverrides overrides, SettingsRetryCheckpointStore checkpoint,
        TaskCompletionSource idle, TaskCompletionSource conflict) => new(consent,
        (runtimeEnabled, persistedConsent) => new SettingsSyncDesktopTick(
            new SettingsSyncWorker(vault, crypto, transport, runtimeEnabled, persistedConsent), vault, preferences, overrides).RunAsync,
        checkpointStore: checkpoint,
        report: (result, _) =>
        {
            if (result.Status == SettingsRetryStatus.Idle) idle.TrySetResult();
            if (result.Status == SettingsRetryStatus.Conflict) conflict.TrySetResult();
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
        throw new TimeoutException("HTTPS settings runtimes did not converge in time.");
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class LostCreateResponse(ISettingsDocumentTransport inner) : ISettingsDocumentTransport
    {
        public int Writes { get; private set; }
        public Task<byte[]?> ReadAsync(CancellationToken token) => inner.ReadAsync(token);
        public async Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token)
        {
            Writes++; var result = await inner.PublishAsync(operationId, envelope, token);
            if (Writes == 1) throw new IOException("Synthetic lost response after real local HTTPS commit");
            return result;
        }
    }
}
