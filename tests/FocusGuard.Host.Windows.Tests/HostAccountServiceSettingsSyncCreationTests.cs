using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
using FocusGuard.Sync;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostAccountServiceSettingsSyncCreationTests
{
    [Fact]
    public void CustomIdentityApiHandlerIsRejectedOutsideLocalTestConfiguration()
    {
        var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/realms/test"),
            "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"));
        Assert.Throws<InvalidOperationException>(() => _ = new IdentitySession(configuration, TimeProvider.System,
            () => new DelegateHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task AcceptedOptInCreatesProductionSessionAndHostRestartRespectsSavedConsent(
        bool renewDeviceSession, bool keepConsent)
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Host-SettingsSync-Creation-" + Guid.NewGuid().ToString("N"));
        var deviceRoot = Path.Combine(root, "devices");
        var syncRoot = Path.Combine(root, "sync");
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var identityVaultPath = Path.Combine(root, "identity.dpapi");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
        var preferences = new DesktopSettingsStore(preferencesPath);
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 45,
            ReduceMotion: true, CyclePhaseNotifications: false, EnforcementEnabled: true));

        var authority = new Uri("https://identity.example.invalid/realms/phase3-local-test");
        var subject = Guid.NewGuid().ToString("D");
        var bindingBytes = SHA256.HashData(Encoding.UTF8.GetBytes(authority.AbsoluteUri.TrimEnd('/') + "\0" + subject));
        string accountBinding;
        try { accountBinding = DeviceEncoding.Encode(bindingBytes); }
        finally { CryptographicOperations.ZeroMemory(bindingBytes); }
        var account = new DeviceAccountContext(authority.AbsoluteUri.TrimEnd('/'), accountBinding,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        using var device = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var member = new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
            device.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var unsignedMembership = new MembershipDocument("FG-MEMBERSHIP-1", account.AccountId, account.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]), [member], recovery.Public.SigningPublicKey,
            recovery.Public.EncryptionPublicKey, device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var membershipCanonical = MembershipProtocol.CanonicalMembership(unsignedMembership);
        MembershipDocument membership;
        try { membership = unsignedMembership with { Signature = device.Sign("FG-MEMBERSHIP-1", membershipCanonical) }; }
        finally { CryptographicOperations.ZeroMemory(membershipCanonical); }

        var contentKey = RandomNumberGenerator.GetBytes(32);
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string deviceSession;
        try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        var membershipDigest = MembershipProtocol.Digest(membership);
        var accessToken = "synthetic-phase3-host-api-token";
        var identityConfiguration = new IdentityConfiguration(authority, "focusguard-native",
            new Uri("http://127.0.0.1:18444/callback/"), LocalTest: true,
            ApiOrigin: new Uri("https://api.example.invalid/"));
        var api = new LocalSettingsApiFixture(account, device, deviceSession, membership, membershipDigest, accessToken,
            renewDeviceSession);

        try
        {
            var deviceVault = new WindowsDeviceVault(Path.Combine(deviceRoot, accountBinding + ".dpapi"));
            await deviceVault.SaveAsync(device, recovery, account, contentKey, 1, membershipDigest,
                CancellationToken.None, deviceSession);

            var identity = new IdentitySession(identityConfiguration, TimeProvider.System,
                () => new DelegateHttpHandler(api.SendAsync));
            SeedSyntheticIdentitySession(identity, accessToken, subject,
                new AccountState(true, "Local test account", "phase3@example.invalid", true));
            using var service = new HostAccountService(identity, new UserTokenVault(identityVaultPath),
                identityConfiguration, deviceRoot, preferences, settingsSyncTestRoot: syncRoot);
            SetPrivateField(service, "deviceLinked", true);
            service.SetSettingsSyncConsentPresenter(_ => Task.FromResult(true));

            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("unused-settings-sync-create"),
                new VerifiedNativeHostAuthorization(), lifetime, settingsStore: preferences, accountService: service);
            var bridgeJson = await bridge.HandleAsync("https://focusguard.app/index.html",
                BridgeRequest(lease.Token, "account.settings.sync.enable", "{}"), CancellationToken.None);
            Assert.NotNull(bridgeJson);
            var accepted = JsonSerializer.Deserialize<BridgeAccountResponse>(bridgeJson!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Null(accepted.Error);
            Assert.True(accepted.Result.SettingsSyncBusy);
            Assert.Equal("Enabling", accepted.Result.SettingsSyncStatus);

            await GetPrivateTask(service, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(8));
            var completed = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.True(completed.SettingsSyncEnabled, completed.Error);
            Assert.Equal("Enabled", completed.SettingsSyncStatus);

            var created = GetPrivateField<HostSettingsSyncSession>(service, "settingsSyncSession");
            Assert.NotNull(created);
            var outbox = await created.Outbox.LoadAsync();
            Assert.NotNull(outbox);
            Assert.True(outbox.SyncConsentEnabled);
            Assert.Equal(1, outbox.Confirmed.Revision);
            Assert.Equal(new GlobalSettings(45, "light", "en"), outbox.Confirmed.Values);
            Assert.False(outbox.Pending);
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            Assert.True(api.MembershipReadCount >= (renewDeviceSession ? 2 : 1),
                "The production factory must fetch and verify current membership.");
            Assert.Equal(renewDeviceSession ? 1 : 0, api.SessionRenewalCount);
            Assert.True(api.DocumentReadCount >= 2, "Baseline initialization must read the committed encrypted document.");
            Assert.Equal(1, api.DocumentPublishCount);
            Assert.NotNull(api.StoredDocument);
            Assert.False(Encoding.UTF8.GetString(api.StoredDocument).Contains("\"theme\"", StringComparison.Ordinal));
            Assert.False(File.Exists(identityVaultPath), "Settings consent must not persist or replace the OIDC vault.");

            using var persistedDevice = await deviceVault.LoadForAccountAsync(account.Issuer, account.AccountBinding,
                CancellationToken.None);
            Assert.NotNull(persistedDevice);
            Assert.Equal(api.CurrentDeviceSession, persistedDevice.Material.DeviceSession);

            var local = preferences.Load();
            Assert.True(local.ReduceMotion);
            Assert.False(local.CyclePhaseNotifications);
            Assert.True(local.EnforcementEnabled);

            if (!keepConsent)
            {
                await GetPrivateTask(service, "deviceCheckTask").WaitAsync(TimeSpan.FromSeconds(8));
                var disabling = await service.ExecuteAsync(HostAccountAction.DisableSettingsSync, CancellationToken.None);
                Assert.Null(disabling.Error);
                Assert.Equal("Disabling", disabling.SettingsSyncStatus);
                await GetPrivateTask(service, "settingsSyncTask").WaitAsync(TimeSpan.FromSeconds(8));
            }
            service.Dispose();
            Assert.Equal(keepConsent, await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            var readsBeforeRestart = api.DocumentReadCount;
            var publishesBeforeRestart = api.DocumentPublishCount;
            var bootstrapReadsBeforeRestart = api.BootstrapAccountReadCount;
            var consentPromptCount = 0;
            var resumedIdentity = new IdentitySession(identityConfiguration, TimeProvider.System,
                () => new DelegateHttpHandler(api.SendAsync));
            SeedSyntheticIdentitySession(resumedIdentity, accessToken, subject,
                new AccountState(true, "Local test account", "phase3@example.invalid", true));
            using var resumedService = new HostAccountService(resumedIdentity,
                new UserTokenVault(Path.Combine(root, "identity-after-restart.dpapi")), identityConfiguration,
                deviceRoot, preferences, settingsSyncTestRoot: syncRoot);
            resumedService.SetSettingsSyncConsentPresenter(_ =>
            {
                Interlocked.Increment(ref consentPromptCount);
                return Task.FromResult(false);
            });

            _ = await resumedService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            await GetPrivateTask(resumedService, "deviceCheckTask").WaitAsync(TimeSpan.FromSeconds(8));
            var resumedSnapshot = await resumedService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.True(resumedSnapshot.DeviceLinked, resumedSnapshot.Error);
            Assert.Equal(0, Volatile.Read(ref consentPromptCount));
            Assert.True(api.BootstrapAccountReadCount > bootstrapReadsBeforeRestart,
                "A fresh HostAccountService must verify the linked account through its normal device refresh path.");
            if (!keepConsent)
            {
                Assert.False(resumedSnapshot.SettingsSyncEnabled);
                Assert.Equal("Disabled", resumedSnapshot.SettingsSyncStatus);
                Assert.Null(GetPrivateField<HostSettingsSyncSession?>(resumedService, "settingsSyncSession"));
                Assert.Null(GetPrivateField<Task?>(resumedService, "settingsSyncRunnerTask"));
                Assert.Equal(readsBeforeRestart, api.DocumentReadCount);
                Assert.Equal(publishesBeforeRestart, api.DocumentPublishCount);
                Assert.Equal(local, preferences.Load());
                return;
            }
            Assert.True(resumedSnapshot.SettingsSyncEnabled,
                $"Settings sync did not resume. Status={resumedSnapshot.SettingsSyncStatus}; error={resumedSnapshot.Error ?? "<none>"}; " +
                $"accountReads={api.BootstrapAccountReadCount - bootstrapReadsBeforeRestart}; " +
                $"membershipReads={api.MembershipReadCount}; consent={await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId, localSyncRoot: syncRoot)}");
            Assert.Equal("Enabled", resumedSnapshot.SettingsSyncStatus);
            Assert.Equal(0, Volatile.Read(ref consentPromptCount));
            Assert.True(api.BootstrapAccountReadCount > bootstrapReadsBeforeRestart,
                "A fresh HostAccountService must verify the linked account through its normal device refresh path.");
            Assert.True(api.MembershipReadCount >= (renewDeviceSession ? 3 : 2),
                "A fresh HostAccountService must revalidate membership before restoring the runner.");
            await WaitForAsync(() => api.DocumentReadCount > readsBeforeRestart, TimeSpan.FromSeconds(8));
            Assert.Equal(publishesBeforeRestart, api.DocumentPublishCount);

            var resumedSession = GetPrivateField<HostSettingsSyncSession>(resumedService, "settingsSyncSession");
            Assert.NotNull(resumedSession);
            var resumedOutbox = await resumedSession.Outbox.LoadAsync();
            Assert.NotNull(resumedOutbox);
            Assert.True(resumedOutbox.SyncConsentEnabled);
            Assert.Equal(outbox.Confirmed, resumedOutbox.Confirmed);
            Assert.Equal(1, resumedOutbox.Confirmed.Revision);
            Assert.Equal(new GlobalSettings(45, "light", "en"), resumedOutbox.Confirmed.Values);
            Assert.False(resumedOutbox.Pending);
            Assert.True(await HostSettingsSyncSession.HasSavedConsentAsync(account, device.Public.DeviceId,
                localSyncRoot: syncRoot));
            var importPath = Path.Combine(root, "portable-settings.json");
            await File.WriteAllBytesAsync(importPath, SettingsExport.Create(new(90, "dark", "es"), true));
            resumedService.SetPortableSettingsPresenters(
                (_, _) => Task.FromResult<PortableSettingsFileSelection?>(null),
                _ => Task.FromResult<string?>(importPath),
                (_, _) => Task.FromResult<SettingsFields?>(SettingsFields.Duration));
            var importing = await resumedService.ExecuteAsync(HostAccountAction.ImportPortableSettings,
                CancellationToken.None);
            Assert.Null(importing.Error);
            await GetPrivateTask(resumedService, "settingsTransferTask").WaitAsync(TimeSpan.FromSeconds(8));
            var imported = await resumedService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.Null(imported.Error);
            Assert.Equal("PortableImported", imported.SettingsTransferStatus);
            await WaitForAsync(() => api.DocumentPublishCount > publishesBeforeRestart, TimeSpan.FromSeconds(8));
            var importedPreferences = preferences.Load();
            Assert.Equal(90, importedPreferences.LastDurationMinutes);
            Assert.Equal(AppTheme.Light, importedPreferences.Theme);
            Assert.Equal("en", importedPreferences.UiLanguage);
            Assert.True(importedPreferences.ReduceMotion);
            Assert.True(importedPreferences.EnforcementEnabled);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string BridgeRequest(string documentToken, string method, string parameters) =>
        $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{documentToken}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The expected API request did not arrive.");
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }

    private static void SeedSyntheticIdentitySession(IdentitySession identity, string accessToken,
        string subject, AccountState accountState)
    {
        var tokenType = typeof(IdentitySession).GetNestedType("SessionTokens",
            System.Reflection.BindingFlags.NonPublic)!;
        var tokens = Activator.CreateInstance(tokenType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, binder: null,
            args: [accessToken, "synthetic-refresh-token", null, DateTimeOffset.UtcNow.AddHours(1),
                subject, "synthetic-test-nonce"], culture: null)!;
        SetPrivateField(identity, "tokens", tokens);
        typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
            accountState with { SignedIn = true });
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static Task GetPrivateTask(object instance, string name) => GetPrivateField<Task>(instance, name);

    private sealed class DelegateHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class LocalSettingsApiFixture
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly DeviceAccountContext account;
        private readonly DeviceIdentity device;
        private readonly MembershipDocument membership;
        private readonly string membershipDigest;
        private readonly string accessToken;
        private readonly string accessTokenDigest;
        private readonly bool rejectFirstMembership;
        private readonly ConcurrentDictionary<string, byte> requestIds = new(StringComparer.Ordinal);
        private readonly object documentLock = new();
        private EncryptedContentDocument? document;
        private byte[]? storedDocument;
        private string deviceSession;
        private int firstMembershipRejected;

        internal int MembershipReadCount;
        internal int SessionRenewalCount;
        internal int BootstrapAccountReadCount;
        internal int DocumentReadCount;
        internal int DocumentPublishCount;
        internal byte[]? StoredDocument => Volatile.Read(ref storedDocument)?.ToArray();
        internal string CurrentDeviceSession => Volatile.Read(ref deviceSession);

        internal LocalSettingsApiFixture(DeviceAccountContext account, DeviceIdentity device, string deviceSession,
            MembershipDocument membership, string membershipDigest, string accessToken, bool rejectFirstMembership)
        {
            this.account = account;
            this.device = device;
            this.deviceSession = deviceSession;
            this.membership = membership;
            this.membershipDigest = membershipDigest;
            this.accessToken = accessToken;
            this.rejectFirstMembership = rejectFirstMembership;
            accessTokenDigest = DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        }

        internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/bootstrap/account" && request.Method == HttpMethod.Get)
            {
                if (request.Headers.Authorization?.Scheme != "Bearer" ||
                    request.Headers.Authorization.Parameter != accessToken)
                    return JsonResponse(HttpStatusCode.Unauthorized, new { error = "InvalidBearerToken" });
                Interlocked.Increment(ref BootstrapAccountReadCount);
                return JsonResponse(HttpStatusCode.OK, new
                {
                    deviceLinked = true,
                    accountId = account.AccountId,
                    generationId = account.GenerationId,
                    membershipRevision = membership.ProposedRevision
                });
            }
            if (path == "/v1/bootstrap/sessions/renew" && request.Method == HttpMethod.Post)
            {
                var renewal = JsonSerializer.Deserialize<RenewDeviceSessionRequest>(body, Json);
                if (renewal is null || renewal.AccountId != account.AccountId ||
                    renewal.GenerationId != account.GenerationId || renewal.DeviceId != device.Public.DeviceId ||
                    renewal.MembershipDigest != membershipDigest || string.IsNullOrWhiteSpace(renewal.DeviceSession))
                    return JsonResponse(HttpStatusCode.BadRequest, new { error = "InvalidDeviceSessionRenewal" });

                var canonicalBody = MembershipProtocol.CanonicalRenewDeviceSessionBody(renewal);
                bool verified;
                try { verified = VerifyRenewalRequest(request, renewal, canonicalBody); }
                finally { CryptographicOperations.ZeroMemory(canonicalBody); }
                if (!verified) return new(HttpStatusCode.Forbidden);
                Interlocked.Increment(ref SessionRenewalCount);
                Volatile.Write(ref deviceSession, renewal.DeviceSession);
                return JsonResponse(HttpStatusCode.OK, new
                {
                    expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                    snapshot = MembershipSnapshot()
                });
            }

            if (!VerifyDeviceRequest(request, body)) return new(HttpStatusCode.Forbidden);
            if (path == "/v1/bootstrap/membership" && request.Method == HttpMethod.Get)
            {
                Interlocked.Increment(ref MembershipReadCount);
                if (rejectFirstMembership && Interlocked.CompareExchange(ref firstMembershipRejected, 1, 0) == 0)
                    return new(HttpStatusCode.Unauthorized);
                return JsonResponse(HttpStatusCode.OK, MembershipSnapshot());
            }
            if (path == "/v1/sync/cursor" && request.Method == HttpMethod.Get)
                return new(HttpStatusCode.NotFound); // Exercise the existing-API fallback to the document snapshot.
            if (path == "/v1/documents/" + HostSettingsSyncSession.GlobalSettingsEntityId && request.Method == HttpMethod.Get)
            {
                Interlocked.Increment(ref DocumentReadCount);
                EncryptedContentDocument? snapshot;
                lock (documentLock) snapshot = document;
                if (snapshot is null) return new(HttpStatusCode.NotFound);
                var response = JsonResponse(HttpStatusCode.OK, new
                {
                    revision = snapshot.ProposedRevision,
                    document = snapshot,
                    cursor = snapshot.ProposedRevision
                });
                AddDocumentVersionHeaders(response, snapshot.ProposedRevision);
                return response;
            }
            if (path == "/v1/documents/" + HostSettingsSyncSession.GlobalSettingsEntityId && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref DocumentPublishCount);
                var parsed = JsonSerializer.Deserialize<EncryptedContentDocument>(body, Json);
                if (parsed is null || parsed.AuthorDeviceId != device.Public.DeviceId ||
                    parsed.AccountId != account.AccountId || parsed.GenerationId != account.GenerationId ||
                    parsed.EntityId != HostSettingsSyncSession.GlobalSettingsEntityId ||
                    !ContentDocumentCodec.VerifySignedHeader(parsed,
                        new ContentDocumentContext(parsed.AccountId, parsed.GenerationId, parsed.EntityId, parsed.Kind,
                            parsed.BaseRevision, parsed.ProposedRevision, parsed.KeyEpoch, parsed.MembershipRevision),
                        device.Public))
                    return JsonResponse(HttpStatusCode.BadRequest, new { error = "InvalidEncryptedDocument" });

                lock (documentLock)
                {
                    if (document is not null && parsed.BaseRevision != document.ProposedRevision)
                        return JsonResponse(HttpStatusCode.Conflict, new { error = "DocumentRevisionConflict" });
                    document = parsed;
                    Volatile.Write(ref storedDocument, body.ToArray());
                }
                var response = JsonResponse(HttpStatusCode.Created, new
                {
                    state = "Stored",
                    revision = parsed.ProposedRevision,
                    entityId = parsed.EntityId,
                    cursor = parsed.ProposedRevision
                });
                AddDocumentVersionHeaders(response, parsed.ProposedRevision);
                return response;
            }
            return new(HttpStatusCode.NotFound);
        }

        private object MembershipSnapshot() => new
        {
            membership,
            membershipDigest,
            keyEnvelopes = Array.Empty<SignedKeyEnvelope>(),
            membershipChain = new[] { membership }
        };

        private bool VerifyRenewalRequest(HttpRequestMessage request, RenewDeviceSessionRequest renewal,
            byte[] canonicalBody)
        {
            if (request.RequestUri is null || request.RequestUri.Scheme != "https" ||
                request.RequestUri.GetLeftPart(UriPartial.Authority) != "https://api.example.invalid" ||
                request.RequestUri.AbsolutePath != "/v1/bootstrap/sessions/renew" || request.RequestUri.Query.Length != 0 ||
                request.RequestUri.Fragment.Length != 0 || request.Method != HttpMethod.Post ||
                request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != accessToken ||
                !request.Headers.TryGetValues("X-FG-Request-Id", out var requestValues) ||
                !request.Headers.TryGetValues("X-FG-Timestamp", out var timestampValues) ||
                !request.Headers.TryGetValues("X-FG-Proof", out var proofValues)) return false;

            var requestId = requestValues.SingleOrDefault();
            var timestampText = timestampValues.SingleOrDefault();
            var signature = proofValues.SingleOrDefault();
            if (requestId is null || !Guid.TryParseExact(requestId, "D", out _) || timestampText is null || signature is null ||
                !DateTimeOffset.TryParseExact(timestampText, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ||
                (DateTimeOffset.UtcNow - timestamp).Duration() > TimeSpan.FromMinutes(1) ||
                !requestIds.TryAdd(requestId, 0)) return false;

            var proof = new DeviceRequestProof("POST", "/v1/bootstrap/sessions/renew", renewal.AccountId,
                renewal.GenerationId, renewal.DeviceId, requestId, timestamp.ToUnixTimeMilliseconds(), accessTokenDigest,
                DeviceEncoding.Encode(SHA256.HashData(canonicalBody)));
            var canonicalProof = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try { return DeviceIdentity.Verify(device.Public, "FG-DEVICE-SESSION-RENEW-1", canonicalProof, signature); }
            catch (Exception error) when (error is ArgumentException or FormatException or CryptographicException) { return false; }
            finally { CryptographicOperations.ZeroMemory(canonicalProof); }
        }

        private bool VerifyDeviceRequest(HttpRequestMessage request, byte[] body,
            string? expectedDeviceSession = null, byte[]? proofBody = null)
        {
            if (request.RequestUri is null || request.RequestUri.Scheme != "https" ||
                request.RequestUri.GetLeftPart(UriPartial.Authority) != "https://api.example.invalid" ||
                request.RequestUri.Query.Length != 0 || request.RequestUri.Fragment.Length != 0 ||
                request.Headers.Authorization?.Scheme != "Bearer" ||
                request.Headers.Authorization.Parameter != accessToken ||
                !request.Headers.TryGetValues("X-FG-Device-Id", out var deviceValues) ||
                !request.Headers.TryGetValues("X-FG-Device-Session", out var sessionValues) ||
                !request.Headers.TryGetValues("X-FG-Request-Id", out var requestValues) ||
                !request.Headers.TryGetValues("X-FG-Timestamp", out var timestampValues) ||
                !request.Headers.TryGetValues("X-FG-Proof", out var proofValues)) return false;

            var deviceId = deviceValues.SingleOrDefault();
            var session = sessionValues.SingleOrDefault();
            var requestId = requestValues.SingleOrDefault();
            var timestampText = timestampValues.SingleOrDefault();
            var signature = proofValues.SingleOrDefault();
            expectedDeviceSession ??= Volatile.Read(ref deviceSession);
            if (deviceId != device.Public.DeviceId || session != expectedDeviceSession || requestId is null ||
                !Guid.TryParseExact(requestId, "D", out _) || timestampText is null || signature is null ||
                !DateTimeOffset.TryParseExact(timestampText, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ||
                (DateTimeOffset.UtcNow - timestamp).Duration() > TimeSpan.FromMinutes(1) ||
                !requestIds.TryAdd(requestId, 0)) return false;

            var proof = new DeviceRequestProof(request.Method.Method, request.RequestUri.AbsolutePath,
                account.AccountId, account.GenerationId, deviceId, requestId, timestamp.ToUnixTimeMilliseconds(),
                accessTokenDigest, DeviceEncoding.Encode(SHA256.HashData(proofBody ?? body)));
            var canonical = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try { return DeviceIdentity.Verify(device.Public, "FG-REQUEST-1", canonical, signature); }
            catch (Exception error) when (error is ArgumentException or FormatException or CryptographicException) { return false; }
            finally { CryptographicOperations.ZeroMemory(canonical); }
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
            new(status) { Content = JsonContent.Create(body, options: Json) };

        private static void AddDocumentVersionHeaders(HttpResponseMessage response, long revision)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"" + revision.ToString(CultureInfo.InvariantCulture) + "\"");
            response.Headers.TryAddWithoutValidation("X-FG-Cursor", revision.ToString(CultureInfo.InvariantCulture));
        }
    }
}
