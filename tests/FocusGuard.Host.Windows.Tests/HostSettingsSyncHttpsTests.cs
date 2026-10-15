using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
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

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostSettingsSyncHttpsTests
{
    [Fact]
    public async Task TwoHostSessionsConvergeOverPinnedHttpsAndPreserveLocalOverridesAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-TwoSessionHttps-" + Guid.NewGuid().ToString("N"));
        var syncRoot = Path.Combine(root, "sync");
        Directory.CreateDirectory(root);
        using var owner = DeviceIdentity.Create();
        using var joinedDevice = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var account = new DeviceAccountContext("https://identity.example.invalid/realms/local",
            DeviceEncoding.Encode(SHA256.HashData("host-two-session-https-subject"u8)),
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        var ownerMember = new DeviceMember(owner.Public.DeviceId, owner.Public.SigningPublicKey,
            owner.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var joinedMember = new DeviceMember(joinedDevice.Public.DeviceId, joinedDevice.Public.SigningPublicKey,
            joinedDevice.Public.EncryptionPublicKey, "Active", ["Read", "WriteSettings"]);
        var initialUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [ownerMember], recovery.Public.SigningPublicKey,
            recovery.Public.EncryptionPublicKey, owner.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var initialMembership = Sign(initialUnsigned, owner);
        var joinedUnsigned = initialMembership with
        {
            BaseRevision = 1,
            ProposedRevision = 2,
            PreviousDigest = MembershipProtocol.Digest(initialMembership),
            Members = [ownerMember, joinedMember],
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var joinedMembership = Sign(joinedUnsigned, owner);
        MembershipDocument[] history = [initialMembership, joinedMembership];
        var membershipDigest = MembershipProtocol.Digest(joinedMembership);
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var ownerSessionId = NewDeviceSession();
        var joinedSessionId = NewDeviceSession();
        var ownerToken = "synthetic-owner-token";
        var joinedToken = "synthetic-joined-token";
        var ownerAccessDigestBytes = TokenDigest(ownerToken);
        var joinedAccessDigestBytes = TokenDigest(joinedToken);
        var ownerAccessDigest = DeviceEncoding.Encode(ownerAccessDigestBytes);
        var joinedAccessDigest = DeviceEncoding.Encode(joinedAccessDigestBytes);
        var ownerSessionHash = SessionHash(ownerSessionId);
        var joinedSessionHash = SessionHash(joinedSessionId);
        var entityId = HostSettingsSyncSession.GlobalSettingsEntityId;
        var ownerReports = new ConcurrentQueue<string>();
        var joinedReports = new ConcurrentQueue<string>();
        var ownerPreferences = new DesktopSettingsStore(Path.Combine(root, "owner", "desktop-settings.json"));
        var joinedPreferences = new DesktopSettingsStore(Path.Combine(root, "joined", "desktop-settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(ownerPreferences.FilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(joinedPreferences.FilePath)!);
        ownerPreferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false));
        joinedPreferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 50,
            ReduceMotion: false, EnforcementEnabled: true, CyclePhaseNotifications: true));

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(subjectAlternativeName.Build());
        using var generatedCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var certificatePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var certificateBytes = generatedCertificate.Export(X509ContentType.Pkcs12, certificatePassword);
        X509Certificate2 serverCertificate;
        try
        {
            // Windows SChannel needs a temporary user key container for the loopback server certificate.
            serverCertificate = X509CertificateLoader.LoadPkcs12(certificateBytes, certificatePassword,
                X509KeyStorageFlags.UserKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(certificateBytes); }

        var clients = new Dictionary<string, LoopbackClient>(StringComparer.Ordinal)
        {
            [owner.Public.DeviceId] = new(owner, ownerToken, ownerAccessDigest, ownerSessionHash),
            [joinedDevice.Public.DeviceId] = new(joinedDevice, joinedToken, joinedAccessDigest, joinedSessionHash)
        };
        var publicIdentities = clients.ToDictionary(pair => pair.Key, pair => pair.Value.Identity.Public,
            StringComparer.Ordinal);
        var requestIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var ledgerLock = new object();
        EncryptedContentDocument? committedDocument = null;
        EncryptedContentDocument? conflictReadSnapshot = null;
        var conflictReadBarrierEnabled = 0;
        var conflictReadCount = 0;
        var conflictReadsReady = NewSignal();
        var httpReadCount = 0;
        var httpPostCount = 0;
        HostSettingsSyncSession? ownerSession = null;
        HostSettingsSyncSession? joinedSyncSession = null;
        HostAccountService? resetConflictService = null;
        Task? ownerLoop = null;
        Task? joinedLoop = null;
        using var initialStop = new CancellationTokenSource();
        using var conflictStop = new CancellationTokenSource();
        using var resolvedStop = new CancellationTokenSource();
        using var reopenedStop = new CancellationTokenSource();
        using var resetPublisherStop = new CancellationTokenSource();
        using var resetConflictStop = new CancellationTokenSource();
        using var resetDeliveryStop = new CancellationTokenSource();
        using var overrideDeliveryStop = new CancellationTokenSource();
        WebApplication? app = null;
        HttpClient? httpClient = null;
        HttpClientHandler? handler = null;

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = 131_072;
                options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(serverCertificate));
            });
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Request.EnableBuffering();
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body, context.RequestAborted);
                context.Request.Body.Position = 0;
                var requestId = context.Request.Headers["X-FG-Request-Id"].ToString();
                var timestamp = DateTimeOffset.Parse(context.Request.Headers["X-FG-Timestamp"].ToString(),
                    System.Globalization.CultureInfo.InvariantCulture);
                var deviceId = context.Request.Headers["X-FG-Device-Id"].ToString();
                if (!clients.TryGetValue(deviceId, out var clientData))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var proof = new DeviceRequestProof(context.Request.Method, context.Request.Path.Value!, account.AccountId,
                    account.GenerationId, deviceId, requestId, timestamp.ToUnixTimeMilliseconds(), clientData.AccessDigest,
                    DeviceEncoding.Encode(SHA256.HashData(body.ToArray())));
                var sessionBytes = DeviceEncoding.Decode(context.Request.Headers["X-FG-Device-Session"].ToString(), 32);
                byte[] suppliedSessionHash;
                try { suppliedSessionHash = SHA256.HashData(sessionBytes); }
                finally { CryptographicOperations.ZeroMemory(sessionBytes); }
                var canonical = MembershipProtocol.CanonicalDeviceRequestProof(proof);
                bool accepted;
                try
                {
                    accepted = context.Request.Headers.Authorization == "Bearer " + clientData.Token &&
                        CryptographicOperations.FixedTimeEquals(suppliedSessionHash, clientData.SessionHash) &&
                        (timestamp - DateTimeOffset.UtcNow).Duration() < TimeSpan.FromMinutes(1) &&
                        requestIds.TryAdd(requestId, 0) &&
                        DeviceIdentity.Verify(clientData.Identity.Public, "FG-REQUEST-1", canonical,
                            context.Request.Headers["X-FG-Proof"].ToString());
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(suppliedSessionHash);
                    CryptographicOperations.ZeroMemory(canonical);
                }
                if (!accepted)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next(context);
            });
            app.MapGet("/v1/documents/{documentId:guid}", async (HttpContext context, string documentId) =>
            {
                Interlocked.Increment(ref httpReadCount);
                EncryptedContentDocument? responseDocument;
                if (Volatile.Read(ref conflictReadBarrierEnabled) != 0)
                {
                    var readIndex = Interlocked.Increment(ref conflictReadCount);
                    if (readIndex <= 2)
                    {
                        if (readIndex == 2)
                        {
                            lock (ledgerLock) conflictReadSnapshot = committedDocument;
                            conflictReadsReady.TrySetResult();
                        }
                        await conflictReadsReady.Task.WaitAsync(context.RequestAborted);
                        responseDocument = conflictReadSnapshot;
                    }
                    else lock (ledgerLock) responseDocument = committedDocument;
                }
                else lock (ledgerLock) responseDocument = committedDocument;

                lock (ledgerLock)
                {
                    if (responseDocument is null || documentId != responseDocument.EntityId)
                        return Results.NotFound(new { error = "DocumentUnavailable" });
                    context.Response.Headers.ETag = "\"" + responseDocument.ProposedRevision + "\"";
                    return Results.Json(new { revision = responseDocument.ProposedRevision, document = responseDocument });
                }
            });
            app.MapPost("/v1/documents/{documentId:guid}", (HttpContext context, string documentId,
                EncryptedContentDocument document) =>
            {
                if (documentId != entityId || !publicIdentities.TryGetValue(document.AuthorDeviceId, out var author))
                    return Results.BadRequest(new { error = "InvalidEncryptedDocument" });
                var contentContext = new ContentDocumentContext(account.AccountId, account.GenerationId, entityId,
                    GlobalSettings.DocumentKind, document.BaseRevision, document.ProposedRevision,
                    document.KeyEpoch, document.MembershipRevision);
                if (!ContentDocumentCodec.VerifySignedHeader(document, contentContext, author))
                    return Results.BadRequest(new { error = "InvalidEncryptedDocument" });
                Interlocked.Increment(ref httpPostCount);
                lock (ledgerLock)
                {
                    var currentRevision = committedDocument?.ProposedRevision ?? 0;
                    if (document.BaseRevision != currentRevision)
                        return Results.Conflict(new { error = "DocumentRevisionConflict" });
                    committedDocument = document;
                    context.Response.Headers.ETag = "\"" + document.ProposedRevision + "\"";
                    return Results.Json(new { state = "Stored", revision = document.ProposedRevision, entityId },
                        statusCode: StatusCodes.Status201Created);
                }
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            var pin = serverCertificate.GetCertHash(HashAlgorithmName.SHA256);
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, supplied, _, errors) => supplied is not null &&
                    (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
                    CryptographicOperations.FixedTimeEquals(pin, supplied.GetCertHash(HashAlgorithmName.SHA256))
            };
            httpClient = new HttpClient(handler);
            var ownerVault = new WindowsDeviceVault(Path.Combine(root, "owner-device.dpapi"));
            var joinedVault = new WindowsDeviceVault(Path.Combine(root, "joined-device.dpapi"));
            await ownerVault.SaveAsync(owner, recovery, account, contentKey, 2, membershipDigest,
                CancellationToken.None, ownerSessionId);
            await joinedVault.SaveAsync(joinedDevice, null, account, contentKey, 2, membershipDigest,
                CancellationToken.None, joinedSessionId);

            var ownerLoaded = await ownerVault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The owner device vault could not be loaded.");
            var ownerTransport = CreateTransport(httpClient, address, account, ownerLoaded, ownerToken, ownerAccessDigest, entityId);
            var ownerIdle = NewSignal();
            try
            {
                ownerSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, ownerTransport,
                    ownerLoaded, history, ownerPreferences, syncRoot,
                    report: (result, state, _) =>
                    {
                        ownerReports.Enqueue($"{result.Status}/{result.Failure} rev={state?.Confirmed.Revision} pending={state?.Pending} conflicts={state?.Conflicts}");
                        if (result.Status == SettingsRetryStatus.Idle && state?.Confirmed.Revision >= 1 && !state.Pending)
                            ownerIdle.TrySetResult();
                        return Task.CompletedTask;
                    });
                ownerLoaded = null!;
            }
            finally { ownerLoaded?.Dispose(); }

            var joinedLoaded = await joinedVault.LoadForAccountAsync(account.Issuer, account.AccountBinding, CancellationToken.None)
                ?? throw new InvalidOperationException("The joined device vault could not be loaded.");
            var joinedTransport = CreateTransport(httpClient, address, account, joinedLoaded, joinedToken,
                joinedAccessDigest, entityId);
            var joinedIdle = NewSignal();
            try
            {
                joinedSyncSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, joinedTransport,
                    joinedLoaded, history, joinedPreferences, syncRoot,
                    report: (result, state, _) =>
                    {
                        joinedReports.Enqueue($"{result.Status}/{result.Failure} rev={state?.Confirmed.Revision} pending={state?.Pending} conflicts={state?.Conflicts}");
                        if (result.Status == SettingsRetryStatus.Idle && state?.Confirmed.Revision >= 1 && !state.Pending)
                            joinedIdle.TrySetResult();
                        return Task.CompletedTask;
                    });
                joinedLoaded = null!;
            }
            finally { joinedLoaded?.Dispose(); }

            await ownerSession.EnableAsync(ownerConfirmed: true);
            await joinedSyncSession.EnableAsync(ownerConfirmed: true);
            var ownerBefore = await ownerSession.Outbox.LoadAsync();
            var joinedBefore = await joinedSyncSession.Outbox.LoadAsync();
            Assert.NotNull(ownerBefore);
            Assert.NotNull(joinedBefore);
            Assert.True(ownerBefore.SyncConsentEnabled);
            Assert.True(joinedBefore.SyncConsentEnabled);
            Assert.Equal(ownerBefore.Confirmed, joinedBefore.Confirmed);

            ownerLoop = ownerSession.RunAsync(initialStop.Token);
            joinedLoop = joinedSyncSession.RunAsync(initialStop.Token);
            await Task.WhenAll(ownerIdle.Task, joinedIdle.Task).WaitAsync(TimeSpan.FromSeconds(5));

            var ownerUpdatedPreferences = ownerPreferences.Load() with
            {
                Theme = AppTheme.Light,
                UiLanguage = "en",
                LastDurationMinutes = 35
            };
            var ownerIdentityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var ownerIdentity = new IdentitySession(ownerIdentityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(ownerIdentity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            using var ownerAccountService = new HostAccountService(ownerIdentity,
                new UserTokenVault(Path.Combine(root, "owner-identity.dpapi")), ownerIdentityConfiguration,
                Path.Combine(root, "owner-devices"), ownerPreferences);
            SetPrivateField(ownerAccountService, "settingsSyncSession", ownerSession);
            SetPrivateField(ownerAccountService, "settingsSyncEnabled", true);
            SetPrivateField(ownerAccountService, "settingsSyncStatus", "Enabled");
            SetPrivateField(ownerAccountService, "deviceLinked", true);
            using var preferenceLifetime = new DocumentLifetime();
            var preferenceLease = preferenceLifetime.BeginDocument();
            var preferenceBridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-phase3-preferences"),
                new VerifiedNativeHostAuthorization(), preferenceLifetime, settingsStore: ownerPreferences,
                accountService: ownerAccountService);
            var preferenceParameters = JsonSerializer.Serialize(new
            {
                uiLanguage = ownerUpdatedPreferences.UiLanguage,
                theme = ownerUpdatedPreferences.Theme.ToString(),
                reduceMotion = ownerUpdatedPreferences.ReduceMotion,
                sessionEnd = ownerUpdatedPreferences.SessionEnd.ToString(),
                cyclePhaseNotifications = ownerUpdatedPreferences.CyclePhaseNotifications,
                enforcementEnabled = ownerUpdatedPreferences.EnforcementEnabled,
                lastDurationMinutes = ownerUpdatedPreferences.LastDurationMinutes,
                lastSessionMode = ownerUpdatedPreferences.LastSessionMode.ToString(),
                lastCyclePresetId = ownerUpdatedPreferences.LastCyclePresetId?.ToString("N")
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var preferenceRequest = $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{preferenceLease.Token}\",\"method\":\"preferences.set\",\"params\":{preferenceParameters}}}";
            var preferenceResponse = await preferenceBridge.HandleAsync("https://focusguard.app/index.html",
                preferenceRequest, initialStop.Token);
            SetPrivateField(ownerAccountService, "settingsSyncSession", null!);
            Assert.NotNull(preferenceResponse);
            using (var parsedPreferenceResponse = JsonDocument.Parse(preferenceResponse))
            {
                Assert.Equal(JsonValueKind.Null, parsedPreferenceResponse.RootElement.GetProperty("error").ValueKind);
                Assert.Equal("Light", parsedPreferenceResponse.RootElement.GetProperty("result").GetProperty("theme").GetString());
            }
            var ownerConverged = await WaitForStateAsync(ownerSession.Outbox,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values == new GlobalSettings(35, "light", "en") &&
                    !state.Pending, initialStop.Token);
            Assert.True(joinedSyncSession.NotifyConnectivityRestored());
            var expected = new GlobalSettings(35, "light", "en");
            SettingsOutbox joinedConverged;
            try
            {
                joinedConverged = await WaitForStateAsync(joinedSyncSession.Outbox,
                    state => state.Confirmed.Revision == 2 && state.Confirmed.Values == expected && !state.Pending,
                    initialStop.Token);
            }
            catch (Exception error)
            {
                var ownerState = await ownerSession.Outbox.LoadAsync();
                var joinedState = await joinedSyncSession.Outbox.LoadAsync();
                long serverRevision;
                lock (ledgerLock) serverRevision = committedDocument?.ProposedRevision ?? 0;
                throw new Xunit.Sdk.XunitException(
                    $"Host HTTPS sync did not converge. Server={serverRevision}; owner={Describe(ownerState)}; " +
                    $"joined={Describe(joinedState)}; owner reports=[{string.Join(';', ownerReports)}]; " +
                    $"joined reports=[{string.Join(';', joinedReports)}]; cause={error.Message}");
            }
            Assert.Equal(ownerConverged.Confirmed, joinedConverged.Confirmed);

            var ownerLocal = ownerPreferences.Load();
            var joinedLocal = joinedPreferences.Load();
            Assert.Equal(AppTheme.Light, ownerLocal.Theme);
            Assert.Equal(AppTheme.Light, joinedLocal.Theme);
            Assert.Equal("en", ownerLocal.UiLanguage);
            Assert.Equal("en", joinedLocal.UiLanguage);
            Assert.Equal(35, ownerLocal.LastDurationMinutes);
            Assert.Equal(35, joinedLocal.LastDurationMinutes);
            Assert.True(ownerLocal.ReduceMotion);
            Assert.False(joinedLocal.ReduceMotion);
            Assert.False(ownerLocal.CyclePhaseNotifications);
            Assert.True(joinedLocal.CyclePhaseNotifications);
            Assert.True(ownerLocal.EnforcementEnabled);
            Assert.True(joinedLocal.EnforcementEnabled);

            initialStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerLoop!);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joinedLoop!);
            ownerLoop = null;
            joinedLoop = null;

            var beforeOwnerConflict = await ownerSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The owner settings outbox disappeared before conflict testing.");
            var beforeJoinedConflict = await joinedSyncSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The joined settings outbox disappeared before conflict testing.");
            Assert.Equal(2, beforeOwnerConflict.Confirmed.Revision);
            Assert.Equal(beforeOwnerConflict.Confirmed, beforeJoinedConflict.Confirmed);

            Volatile.Write(ref conflictReadBarrierEnabled, 1);
            await ownerSession.Edits.EditSelectedAsync(
                beforeOwnerConflict.Confirmed.Values with { DefaultDurationMinutes = 40 },
                SettingsFields.Duration, new LocalSettingsOverrides(), conflictStop.Token);
            await joinedSyncSession.Edits.EditSelectedAsync(
                beforeJoinedConflict.Confirmed.Values with { DefaultDurationMinutes = 60 },
                SettingsFields.Duration, new LocalSettingsOverrides(), conflictStop.Token);
            ownerLoop = ownerSession.RunAsync(conflictStop.Token);
            joinedLoop = joinedSyncSession.RunAsync(conflictStop.Token);
            await conflictReadsReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref conflictReadCount));
            Volatile.Write(ref conflictReadBarrierEnabled, 0);

            var (losingState, ownerLoses) = await WaitForConcurrentDurationConflictAsync(
                ownerSession.Outbox, joinedSyncSession.Outbox, conflictStop.Token);
            Assert.Equal(SettingsFields.Duration, losingState.Conflicts);
            Assert.NotNull(losingState.Draft);
            var losingSession = ownerLoses ? ownerSession : joinedSyncSession;
            var winningSession = ownerLoses ? joinedSyncSession : ownerSession;
            var winningDuration = losingState.Confirmed.Values.DefaultDurationMinutes;

            conflictStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerLoop!);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joinedLoop!);
            ownerLoop = null;
            joinedLoop = null;

            var identityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var identity = new IdentitySession(identityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            using var conflictService = new HostAccountService(identity,
                new UserTokenVault(Path.Combine(root, "identity.dpapi")), identityConfiguration,
                Path.Combine(root, ownerLoses ? "owner-devices" : "joined-devices"),
                ownerLoses ? ownerPreferences : joinedPreferences);
            SetPrivateField(conflictService, "settingsSyncSession", losingSession);
            SetPrivateField(conflictService, "settingsSyncEnabled", true);
            SetPrivateField(conflictService, "settingsSyncStatus", "Conflict");
            SetPrivateField(conflictService, "settingsSyncConflictReview", new HostSettingsSyncConflictReview(
                losingState.Confirmed.Revision, ["duration"], losingState.Confirmed.Values,
                losingState.Draft!, losingState.DraftDeleted));
            SetPrivateField(conflictService, "deviceLinked", true);
            SetPrivateField(conflictService, "nextDeviceCheckUtc", DateTimeOffset.MaxValue);

            var queuedResolution = await conflictService.ExecuteAsync(HostAccountAction.ResolveSettingsSyncConflict,
                CancellationToken.None, settingsSyncResolution: new HostSettingsSyncConflictResolution(
                    losingState.Confirmed.Revision,
                    [new HostSettingsSyncConflictChoice(HostSettingsSyncConflictField.Duration,
                        HostSettingsSyncConflictSource.OtherDevices)]));
            Assert.True(queuedResolution.SettingsSyncBusy, queuedResolution.Error);
            var resolveTask = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(conflictService)!;
            await resolveTask.WaitAsync(TimeSpan.FromSeconds(3));

            _ = await WaitForStateAsync(losingSession.Outbox,
                state => state.Confirmed.Revision == 3 && state.Confirmed.Values.DefaultDurationMinutes == winningDuration &&
                    state.Conflicts == SettingsFields.None && !state.Pending, resolvedStop.Token);
            Assert.True(winningSession.NotifyConnectivityRestored());
            var winningLoop = winningSession.RunAsync(resolvedStop.Token);
            if (ownerLoses) joinedLoop = winningLoop;
            else ownerLoop = winningLoop;
            _ = await WaitForStateAsync(winningSession.Outbox,
                state => state.Confirmed.Revision == 3 && state.Confirmed.Values.DefaultDurationMinutes == winningDuration &&
                    state.Conflicts == SettingsFields.None && !state.Pending, resolvedStop.Token);
            var ownerAfterConflict = ownerPreferences.Load();
            var joinedAfterConflict = joinedPreferences.Load();
            Assert.Equal(winningDuration, ownerAfterConflict.LastDurationMinutes);
            Assert.Equal(winningDuration, joinedAfterConflict.LastDurationMinutes);
            Assert.True(ownerAfterConflict.ReduceMotion);
            Assert.False(joinedAfterConflict.ReduceMotion);
            Assert.False(ownerAfterConflict.CyclePhaseNotifications);
            Assert.True(joinedAfterConflict.CyclePhaseNotifications);

            var beforeReset = await ownerSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The owner outbox disappeared before resetting shared settings.");
            var resetPreview = new HostSettingsSyncResetPreview(beforeReset.Confirmed.Revision,
                beforeReset.Confirmed.EffectiveValues, GlobalSettings.Default);
            _ = await ownerSession.ResetSharedSettingsAsync(resetPreview.Revision, resetPreview.Current,
                ownerConfirmed: true, resolvedStop.Token);
            _ = await WaitForStateAsync(ownerSession.Outbox,
                state => state.Confirmed.Revision == 4 && state.Confirmed.Deleted && !state.Pending, resolvedStop.Token);
            Assert.True(joinedSyncSession.NotifyConnectivityRestored());
            var resetConvergence = await WaitForStateAsync(joinedSyncSession.Outbox,
                state => state.Confirmed.Revision == 4 && state.Confirmed.Deleted && !state.Pending, resolvedStop.Token);
            Assert.Equal(GlobalSettings.Default, resetConvergence.Confirmed.EffectiveValues);

            var ownerAfterReset = await WaitForPreferencesAsync(ownerPreferences,
                settings => DesktopSettingsProjection.Extract(settings) == GlobalSettings.Default, resolvedStop.Token);
            var joinedAfterReset = await WaitForPreferencesAsync(joinedPreferences,
                settings => DesktopSettingsProjection.Extract(settings) == GlobalSettings.Default, resolvedStop.Token);
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(ownerAfterReset));
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(joinedAfterReset));
            Assert.True(ownerAfterReset.ReduceMotion);
            Assert.False(joinedAfterReset.ReduceMotion);
            Assert.False(ownerAfterReset.CyclePhaseNotifications);
            Assert.True(joinedAfterReset.CyclePhaseNotifications);
            Assert.True(ownerAfterReset.EnforcementEnabled);
            Assert.True(joinedAfterReset.EnforcementEnabled);

            resolvedStop.Cancel();
            if (ownerLoop is not null)
            {
                try { await ownerLoop; }
                catch (OperationCanceledException) { }
                ownerLoop = null;
            }
            if (joinedLoop is not null)
            {
                try { await joinedLoop; }
                catch (OperationCanceledException) { }
                joinedLoop = null;
            }

            conflictService.Dispose(); // The Host-owned loser session is closed just like its app process.
            if (ownerLoses) ownerSession = null;
            else joinedSyncSession = null;
            if (ownerSession is not null)
            {
                await ownerSession.DisposeAsync();
                ownerSession = null;
            }
            if (joinedSyncSession is not null)
            {
                await joinedSyncSession.DisposeAsync();
                joinedSyncSession = null;
            }

            var readsBeforeResume = Volatile.Read(ref httpReadCount);
            var postsBeforeResume = Volatile.Read(ref httpPostCount);
            var ownerResumed = NewSignal();
            var joinedResumed = NewSignal();
            var ownerReloaded = await ownerVault.LoadForAccountAsync(account.Issuer, account.AccountBinding,
                CancellationToken.None) ?? throw new InvalidOperationException("The owner vault did not survive the Host restart.");
            var ownerReloadedTransport = CreateTransport(httpClient, address, account, ownerReloaded,
                ownerToken, ownerAccessDigest, entityId);
            ownerSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, ownerReloadedTransport,
                ownerReloaded, history, ownerPreferences, syncRoot,
                report: (result, state, _) =>
                {
                    if (result.Status == SettingsRetryStatus.Idle && state?.Confirmed is { Revision: 4, Deleted: true } &&
                        state.SyncConsentEnabled && !state.Pending)
                        ownerResumed.TrySetResult();
                    return Task.CompletedTask;
                });

            var joinedReloaded = await joinedVault.LoadForAccountAsync(account.Issuer, account.AccountBinding,
                CancellationToken.None) ?? throw new InvalidOperationException("The joined vault did not survive the Host restart.");
            var joinedReloadedTransport = CreateTransport(httpClient, address, account, joinedReloaded,
                joinedToken, joinedAccessDigest, entityId);
            joinedSyncSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, joinedReloadedTransport,
                joinedReloaded, history, joinedPreferences, syncRoot,
                report: (result, state, _) =>
                {
                    if (result.Status == SettingsRetryStatus.Idle && state?.Confirmed is { Revision: 4, Deleted: true } &&
                        state.SyncConsentEnabled && !state.Pending)
                        joinedResumed.TrySetResult();
                    return Task.CompletedTask;
                });

            ownerLoop = ownerSession.RunAsync(reopenedStop.Token);
            joinedLoop = joinedSyncSession.RunAsync(reopenedStop.Token);
            await Task.WhenAll(ownerResumed.Task, joinedResumed.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, owner.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, joinedDevice.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.True(Volatile.Read(ref httpReadCount) >= readsBeforeResume + 2);
            Assert.Equal(postsBeforeResume, Volatile.Read(ref httpPostCount));
            var ownerAfterResume = await ownerSession.Outbox.LoadAsync();
            var joinedAfterResume = await joinedSyncSession.Outbox.LoadAsync();
            Assert.NotNull(ownerAfterResume);
            Assert.NotNull(joinedAfterResume);
            Assert.Equal(ownerAfterResume, joinedAfterResume);
            Assert.True(ownerAfterResume.SyncConsentEnabled);
            Assert.True(ownerAfterResume.Confirmed.Deleted);
            Assert.Equal(4, ownerAfterResume.Confirmed.Revision);
            Assert.False(ownerAfterResume.Pending);
            var ownerPreferencesAfterResume = ownerPreferences.Load();
            var joinedPreferencesAfterResume = joinedPreferences.Load();
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(ownerPreferencesAfterResume));
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(joinedPreferencesAfterResume));
            Assert.True(ownerPreferencesAfterResume.ReduceMotion);
            Assert.False(joinedPreferencesAfterResume.ReduceMotion);
            Assert.False(ownerPreferencesAfterResume.CyclePhaseNotifications);
            Assert.True(joinedPreferencesAfterResume.CyclePhaseNotifications);

            // Recreate a non-deleted shared document, then stage an offline reset against a concurrent remote edit.
            await ownerSession.Edits.EditSelectedAsync(new GlobalSettings(50, "light", "es"), SettingsFields.Theme,
                new LocalSettingsOverrides(), reopenedStop.Token);
            _ = await WaitForStateAsync(ownerSession.Outbox,
                state => state.Confirmed.Revision == 5 && !state.Confirmed.Deleted &&
                    state.Confirmed.Values == new GlobalSettings(50, "light", "es") && !state.Pending, reopenedStop.Token);
            Assert.True(joinedSyncSession.NotifyConnectivityRestored());
            _ = await WaitForStateAsync(joinedSyncSession.Outbox,
                state => state.Confirmed.Revision == 5 && !state.Confirmed.Deleted &&
                    state.Confirmed.Values == new GlobalSettings(50, "light", "es") && !state.Pending, reopenedStop.Token);

            reopenedStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerLoop!);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joinedLoop!);
            ownerLoop = null;
            joinedLoop = null;

            var ownerResetBase = await ownerSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The owner settings outbox disappeared before reset-conflict staging.");
            var joinedResetBase = await joinedSyncSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The joined settings outbox disappeared before reset-conflict staging.");
            _ = await ownerSession.ResetSharedSettingsAsync(ownerResetBase.Confirmed.Revision,
                ownerResetBase.Confirmed.EffectiveValues, ownerConfirmed: true, CancellationToken.None);
            await joinedSyncSession.Edits.EditSelectedAsync(
                joinedResetBase.Confirmed.Values with { UiLanguage = "en" }, SettingsFields.Language,
                new LocalSettingsOverrides());
            joinedLoop = joinedSyncSession.RunAsync(resetPublisherStop.Token);
            _ = await WaitForStateAsync(joinedSyncSession.Outbox,
                state => state.Confirmed.Revision == 6 && !state.Confirmed.Deleted &&
                    state.Confirmed.Values == new GlobalSettings(50, "light", "en") && !state.Pending,
                resetPublisherStop.Token);

            resetPublisherStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joinedLoop!);
            joinedLoop = null;
            ownerLoop = ownerSession.RunAsync(resetConflictStop.Token);
            var resetConflict = await WaitForStateAsync(ownerSession.Outbox,
                state => state.Confirmed.Revision == 6 && state.DraftDeleted && state.Draft == GlobalSettings.Default &&
                    state.Conflicts == (SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration),
                resetConflictStop.Token);
            Assert.Equal(new GlobalSettings(50, "light", "en"), resetConflict.Confirmed.Values);

            resetConflictStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ownerLoop!);
            ownerLoop = null;
            var resetIdentityConfiguration = new IdentityConfiguration(new Uri(account.Issuer), "focusguard-native",
                new Uri("http://127.0.0.1:18444/callback/"), ApiOrigin: new Uri("https://api.example.invalid/"));
            var resetIdentity = new IdentitySession(resetIdentityConfiguration, TimeProvider.System);
            typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(resetIdentity,
                new AccountState(true, "Test account", "test@example.invalid", true));
            resetConflictService = new HostAccountService(resetIdentity,
                new UserTokenVault(Path.Combine(root, "reset-conflict-identity.dpapi")), resetIdentityConfiguration,
                Path.Combine(root, "reset-conflict-devices"), ownerPreferences);
            SetPrivateField(resetConflictService, "settingsSyncSession", ownerSession);
            SetPrivateField(resetConflictService, "settingsSyncEnabled", true);
            SetPrivateField(resetConflictService, "settingsSyncStatus", "Conflict");
            SetPrivateField(resetConflictService, "settingsSyncConflictReview", new HostSettingsSyncConflictReview(
                resetConflict.Confirmed.Revision, ["theme", "language", "duration"],
                resetConflict.Confirmed.EffectiveValues, resetConflict.Draft!, ResetConflict: true));
            SetPrivateField(resetConflictService, "deviceLinked", true);
            SetPrivateField(resetConflictService, "nextDeviceCheckUtc", DateTimeOffset.MaxValue);
            var rebaseReset = await resetConflictService.ExecuteAsync(
                HostAccountAction.ResolveSettingsSyncResetConflict, CancellationToken.None,
                settingsSyncResetResolution: new HostSettingsSyncResetResolution(resetConflict.Confirmed.Revision,
                    HostSettingsSyncResetSource.ThisDeviceReset));
            Assert.True(rebaseReset.SettingsSyncBusy, rebaseReset.Error);
            var resetResolutionTask = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(resetConflictService)!;
            await resetResolutionTask.WaitAsync(TimeSpan.FromSeconds(3));
            _ = await WaitForStateAsync(ownerSession.Outbox,
                state => state.Confirmed.Revision == 7 && state.Confirmed.Deleted && !state.Pending &&
                    state.Conflicts == SettingsFields.None, CancellationToken.None);
            Assert.True(joinedSyncSession.NotifyConnectivityRestored());
            joinedLoop = joinedSyncSession.RunAsync(resetDeliveryStop.Token);
            _ = await WaitForStateAsync(joinedSyncSession.Outbox,
                state => state.Confirmed.Revision == 7 && state.Confirmed.Deleted && !state.Pending &&
                    state.Conflicts == SettingsFields.None, resetDeliveryStop.Token);
            var ownerAfterResetConflict = ownerPreferences.Load();
            var joinedAfterResetConflict = joinedPreferences.Load();
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(ownerAfterResetConflict));
            Assert.Equal(GlobalSettings.Default, DesktopSettingsProjection.Extract(joinedAfterResetConflict));
            Assert.True(ownerAfterResetConflict.ReduceMotion);
            Assert.False(joinedAfterResetConflict.ReduceMotion);
            Assert.False(ownerAfterResetConflict.CyclePhaseNotifications);
            Assert.True(joinedAfterResetConflict.CyclePhaseNotifications);
            Assert.True((await ownerSession.Outbox.LoadAsync())!.SyncConsentEnabled);
            Assert.True((await joinedSyncSession.Outbox.LoadAsync())!.SyncConsentEnabled);

            resetDeliveryStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joinedLoop!);
            joinedLoop = null;

            var ownerBeforeOverride = await ownerSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The owner outbox disappeared before local override coverage.");
            Assert.Equal(7, ownerBeforeOverride.Confirmed.Revision);
            Assert.True(ownerBeforeOverride.Confirmed.Deleted);
            var postsBeforeOverride = Volatile.Read(ref httpPostCount);
            await ownerSession.Edits.UpdateOverridesAsync(ownerBeforeOverride.Confirmed.Revision,
                ownerBeforeOverride.LocalOverrides, new LocalSettingsOverrides(Theme: "dark"),
                CancellationToken.None, ownerBeforeOverride.LocalOverrides.Apply(ownerBeforeOverride.Confirmed.EffectiveValues));
            var ownerAfterOverride = await ownerSession.Outbox.LoadAsync();
            Assert.NotNull(ownerAfterOverride);
            Assert.Equal(7, ownerAfterOverride.Confirmed.Revision);
            Assert.Equal(new LocalSettingsOverrides(Theme: "dark"), ownerAfterOverride.LocalOverrides);
            Assert.Null(ownerAfterOverride.Draft);
            Assert.False(ownerAfterOverride.Pending);
            Assert.Equal(postsBeforeOverride, Volatile.Read(ref httpPostCount));
            Assert.Equal(AppTheme.Dark, ownerPreferences.Load().Theme);
            Assert.True(ownerPreferences.Load().ReduceMotion);
            Assert.False(ownerPreferences.Load().CyclePhaseNotifications);
            Assert.True(ownerPreferences.Load().EnforcementEnabled);

            resetConflictService.Dispose();
            resetConflictService = null;
            ownerSession = null;
            await joinedSyncSession.DisposeAsync();
            joinedSyncSession = null;
            var ownerOverrideReady = NewSignal();
            var joinedOverrideReady = NewSignal();
            var ownerOverrideRestarted = await ownerVault.LoadForAccountAsync(account.Issuer, account.AccountBinding,
                CancellationToken.None) ?? throw new InvalidOperationException("The owner vault did not survive override restart.");
            try
            {
                ownerSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding,
                    CreateTransport(httpClient, address, account, ownerOverrideRestarted,
                        ownerToken, ownerAccessDigest, entityId), ownerOverrideRestarted, history,
                    ownerPreferences, syncRoot, report: (result, state, _) =>
                    {
                        if (result.Status == SettingsRetryStatus.Idle &&
                            state?.Confirmed is { Revision: 7, Deleted: true } && !state.Pending)
                            ownerOverrideReady.TrySetResult();
                        return Task.CompletedTask;
                    });
                ownerOverrideRestarted = null!;
            }
            finally { ownerOverrideRestarted?.Dispose(); }
            var joinedOverrideRestarted = await joinedVault.LoadForAccountAsync(account.Issuer, account.AccountBinding,
                CancellationToken.None) ?? throw new InvalidOperationException("The joined vault did not survive override restart.");
            try
            {
                joinedSyncSession = HostSettingsSyncSession.CreateForTesting(account.AccountBinding,
                    CreateTransport(httpClient, address, account, joinedOverrideRestarted,
                        joinedToken, joinedAccessDigest, entityId), joinedOverrideRestarted, history,
                    joinedPreferences, syncRoot, report: (result, state, _) =>
                    {
                        if (result.Status == SettingsRetryStatus.Idle &&
                            state?.Confirmed is { Revision: 7, Deleted: true } && !state.Pending)
                            joinedOverrideReady.TrySetResult();
                        return Task.CompletedTask;
                    });
                joinedOverrideRestarted = null!;
            }
            finally { joinedOverrideRestarted?.Dispose(); }

            ownerLoop = ownerSession.RunAsync(overrideDeliveryStop.Token);
            joinedLoop = joinedSyncSession.RunAsync(overrideDeliveryStop.Token);
            await Task.WhenAll(ownerOverrideReady.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                joinedOverrideReady.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var ownerAfterOverrideRestart = await ownerSession.Outbox.LoadAsync();
            Assert.NotNull(ownerAfterOverrideRestart);
            Assert.Equal(new LocalSettingsOverrides(Theme: "dark"), ownerAfterOverrideRestart.LocalOverrides);
            Assert.Equal(AppTheme.Dark, ownerPreferences.Load().Theme);

            // Recreate the shared document from the peer. The owner's local override keeps Dark
            // while the joined device publishes and applies the new shared Light value.
            await joinedSyncSession.Edits.EditSelectedAsync(new GlobalSettings(50, "light", "es"),
                SettingsFields.Theme, new LocalSettingsOverrides(), overrideDeliveryStop.Token);
            Assert.True(ownerSession.NotifyConnectivityRestored());
            var latestSharedValues = new GlobalSettings(50, "light", "es");
            SettingsOutbox ownerAfterRemoteTheme;
            try
            {
                ownerAfterRemoteTheme = await WaitForStateAsync(ownerSession.Outbox,
                    state => state.Confirmed.Revision == 8 && !state.Confirmed.Deleted &&
                        state.Confirmed.Values == latestSharedValues && !state.Pending, overrideDeliveryStop.Token);
            }
            catch (Exception error)
            {
                var ownerState = await ownerSession.Outbox.LoadAsync();
                var joinedState = await joinedSyncSession.Outbox.LoadAsync();
                long serverRevision;
                lock (ledgerLock) serverRevision = committedDocument?.ProposedRevision ?? 0;
                throw new Xunit.Sdk.XunitException(
                    $"Local override receiver did not converge. Server={serverRevision}; owner={Describe(ownerState)}; " +
                    $"joined={Describe(joinedState)}; owner reports=[{string.Join(';', ownerReports)}]; " +
                    $"joined reports=[{string.Join(';', joinedReports)}]; cause={error.Message}");
            }
            var joinedAfterRemoteTheme = await WaitForStateAsync(joinedSyncSession.Outbox,
                state => state.Confirmed.Revision == 8 && !state.Confirmed.Deleted &&
                    state.Confirmed.Values == latestSharedValues && !state.Pending, overrideDeliveryStop.Token);
            _ = await WaitForPreferencesAsync(ownerPreferences, settings => settings.Theme == AppTheme.Dark,
                overrideDeliveryStop.Token);
            _ = await WaitForPreferencesAsync(joinedPreferences, settings => settings.Theme == AppTheme.Light,
                overrideDeliveryStop.Token);
            Assert.Equal(new LocalSettingsOverrides(Theme: "dark"), ownerAfterRemoteTheme.LocalOverrides);
            Assert.Equal(new LocalSettingsOverrides(), joinedAfterRemoteTheme.LocalOverrides);
            Assert.Equal(AppTheme.Dark, ownerPreferences.Load().Theme);
            Assert.Equal(AppTheme.Light, joinedPreferences.Load().Theme);
            Assert.True(ownerPreferences.Load().ReduceMotion);
            Assert.False(ownerPreferences.Load().CyclePhaseNotifications);
            Assert.True(ownerPreferences.Load().EnforcementEnabled);
            Assert.False(joinedPreferences.Load().ReduceMotion);
            Assert.True(joinedPreferences.Load().CyclePhaseNotifications);
            Assert.True(joinedPreferences.Load().EnforcementEnabled);
            var postsAfterRemoteTheme = Volatile.Read(ref httpPostCount);

            var ownerBeforeClearingOverride = await ownerSession.Outbox.LoadAsync() ??
                throw new InvalidOperationException("The owner outbox disappeared before clearing its override.");
            await ownerSession.Edits.UpdateOverridesAsync(ownerBeforeClearingOverride.Confirmed.Revision,
                ownerBeforeClearingOverride.LocalOverrides, new LocalSettingsOverrides(), CancellationToken.None,
                new GlobalSettings(50, "dark", "es"));
            var ownerAfterClearingOverride = await ownerSession.Outbox.LoadAsync();
            Assert.NotNull(ownerAfterClearingOverride);
            Assert.Equal(8, ownerAfterClearingOverride.Confirmed.Revision);
            Assert.Equal(latestSharedValues, ownerAfterClearingOverride.Confirmed.Values);
            Assert.Equal(new LocalSettingsOverrides(), ownerAfterClearingOverride.LocalOverrides);
            Assert.Null(ownerAfterClearingOverride.Draft);
            Assert.Null(ownerAfterClearingOverride.InFlight);
            Assert.False(ownerAfterClearingOverride.Pending);
            Assert.Equal(AppTheme.Light, ownerPreferences.Load().Theme);
            Assert.Equal(AppTheme.Light, joinedPreferences.Load().Theme);
            Assert.Equal(postsAfterRemoteTheme, Volatile.Read(ref httpPostCount));

            lock (ledgerLock)
            {
                Assert.NotNull(committedDocument);
                Assert.Equal(8, committedDocument.ProposedRevision);
                var wire = JsonSerializer.Serialize(committedDocument, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.DoesNotContain("light", wire);
                Assert.DoesNotContain("uiLanguage", wire);
                Assert.DoesNotContain("localOverrides", wire, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            initialStop.Cancel();
            conflictStop.Cancel();
            resolvedStop.Cancel();
            reopenedStop.Cancel();
            resetPublisherStop.Cancel();
            resetConflictStop.Cancel();
            resetDeliveryStop.Cancel();
            overrideDeliveryStop.Cancel();
            if (ownerLoop is not null)
            {
                try { await ownerLoop; }
                catch (OperationCanceledException) { }
            }
            if (joinedLoop is not null)
            {
                try { await joinedLoop; }
                catch (OperationCanceledException) { }
            }
            resetConflictService?.Dispose();
            if (ownerSession is not null) await ownerSession.DisposeAsync();
            if (joinedSyncSession is not null) await joinedSyncSession.DisposeAsync();
            if (app is not null)
            {
                try { await app.StopAsync(); }
                finally { await app.DisposeAsync(); }
            }
            httpClient?.Dispose();
            serverCertificate.Dispose();
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(ownerSessionHash);
            CryptographicOperations.ZeroMemory(joinedSessionHash);
            CryptographicOperations.ZeroMemory(ownerAccessDigestBytes);
            CryptographicOperations.ZeroMemory(joinedAccessDigestBytes);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static SettingsDocumentHttpTransport CreateTransport(HttpClient httpClient, string address,
        DeviceAccountContext account, WindowsDeviceVault.LoadedDeviceVault loaded, string token,
        string accessDigest, string entityId) => new(new Uri(address + "/"),
        new LoopbackSender(httpClient, account.AccountBinding, token, accessDigest), account,
        loaded.Material.Identity, loaded.Material.DeviceSession!, entityId);

    private static async Task<SettingsOutbox> WaitForStateAsync(SettingsOutboxVault vault,
        Func<SettingsOutbox, bool> predicate, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var state = await vault.LoadAsync(token);
            if (state is not null && predicate(state)) return state;
            await Task.Delay(20, token);
        }
        throw new TimeoutException("Two Host settings sessions did not converge through the loopback HTTPS API.");
    }

    private static async Task<DesktopSettings> WaitForPreferencesAsync(DesktopSettingsStore store,
        Func<DesktopSettings, bool> predicate, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        DesktopSettings? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            last = store.Load();
            if (predicate(last)) return last;
            await Task.Delay(20, token);
        }
        throw new TimeoutException($"Local preferences were not projected in the loopback HTTPS test: {last}.");
    }

    private static async Task<(SettingsOutbox Losing, bool FirstLoses)> WaitForConcurrentDurationConflictAsync(
        SettingsOutboxVault firstVault, SettingsOutboxVault secondVault, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var first = await firstVault.LoadAsync(token);
            var second = await secondVault.LoadAsync(token);
            var firstConflicts = first?.Conflicts == SettingsFields.Duration;
            var secondConflicts = second?.Conflicts == SettingsFields.Duration;
            if (firstConflicts != secondConflicts)
            {
                var winner = firstConflicts ? second : first;
                if (winner is { Confirmed.Revision: 3, Pending: false, Conflicts: SettingsFields.None })
                    return (firstConflicts ? first! : second!, firstConflicts);
            }
            await Task.Delay(20, token);
        }
        throw new TimeoutException("Two Host sessions did not produce a CAS duration conflict.");
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static MembershipDocument Sign(MembershipDocument document, DeviceIdentity signer)
    {
        var canonical = MembershipProtocol.CanonicalMembership(document);
        try { return document with { Signature = signer.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    private static string NewDeviceSession()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return DeviceEncoding.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] TokenDigest(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static byte[] SessionHash(string session)
    {
        var bytes = DeviceEncoding.Decode(session, 32);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Describe(SettingsOutbox? state) => state is null ? "missing" :
        $"rev={state.Confirmed.Revision} values={state.Confirmed.Values} draft={state.Draft} pending={state.Pending} conflicts={state.Conflicts}";

    private sealed record LoopbackClient(DeviceIdentity Identity, string Token, string AccessDigest, byte[] SessionHash);

    private sealed class LoopbackSender(HttpClient client, string accountBinding, string token, string accessDigest)
        : ISettingsRequestSender
    {
        public string? AccountBinding => accountBinding;

        public async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> requestFactory,
            CancellationToken tokenCancellation)
        {
            using var request = requestFactory(accessDigest);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, tokenCancellation);
        }
    }
}
