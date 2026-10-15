using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using FocusGuard.Identity;
using FocusGuard.Client.Preferences;
using FocusGuard.Cloud.Api;
using FocusGuard.Devices;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

internal static class ApiAutomaticSettingsSyncChecks
{
    public static async Task RunAsync(string connection, Action<bool, string> check)
    {
        var issuer = "https://phase3-settings.example.invalid/realms/local";
        var subject = "settings-sync-" + Guid.NewGuid().ToString("N");
        using var owner = DeviceIdentity.Create();
        using var joined = DeviceIdentity.Create();
        await using var dataSource = NpgsqlDataSource.Create(connection);
        await using (var migration = dataSource.CreateCommand(BootstrapChallengeLedger.Schema + ContentDocumentLedger.Schema))
            await migration.ExecuteNonQueryAsync();

        var accountId = Guid.NewGuid().ToString("D");
        var generationId = Guid.NewGuid().ToString("D");
        var entityId = Guid.NewGuid().ToString("D");
        var accountHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(issuer + "\0" + subject));
        var accountHash = Convert.ToHexString(accountHashBytes);
        var accountBinding = DeviceEncoding.Encode(accountHashBytes);
        CryptographicOperations.ZeroMemory(accountHashBytes);
        var account = new DeviceAccountContext(issuer, accountBinding, accountId, generationId);

        using var recovery = RecoveryKit.Generate();
        var ownerMember = new DeviceMember(owner.Public.DeviceId, owner.Public.SigningPublicKey,
            owner.Public.EncryptionPublicKey, "Active",
            ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"]);
        var joinedMember = new DeviceMember(joined.Public.DeviceId, joined.Public.SigningPublicKey,
            joined.Public.EncryptionPublicKey, "Active", ["Read", "WriteSettings"]);
        var initialUnsigned = new MembershipDocument("FG-MEMBERSHIP-1", accountId, generationId, 0, 1, 1,
            DeviceEncoding.Encode(new byte[32]), [ownerMember], recovery.Public.SigningPublicKey,
            recovery.Public.EncryptionPublicKey, owner.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var initial = Sign(initialUnsigned, owner);
        var joinedUnsigned = initial with
        {
            BaseRevision = 1,
            ProposedRevision = 2,
            PreviousDigest = MembershipProtocol.Digest(initial),
            Members = [ownerMember, joinedMember],
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var membership = Sign(joinedUnsigned, owner);
        MembershipDocument[] history = [initial, membership];
        check(history.All(MembershipProtocol.VerifyMembership),
            "The two-device test membership chain is cryptographically valid before persistence");

        var key = RandomNumberGenerator.GetBytes(32);
        const string accessToken = "phase3-local-settings-sync-access-token";
        var ownerSession = NewSession();
        var joinedSession = NewSession();
        var ownerSessionHash = SessionHash(ownerSession);
        var joinedSessionHash = SessionHash(joinedSession);
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Api-AutomaticSettings-" + Guid.NewGuid().ToString("N"));
        var ownerScope = new SettingsSyncScope(issuer, accountId, generationId, owner.Public.DeviceId);
        var joinedScope = new SettingsSyncScope(issuer, accountId, generationId, joined.Public.DeviceId);
        SettingsAutomaticSyncRuntime? ownerRuntime = null;
        SettingsAutomaticSyncRuntime? joinedRuntime = null;
        CancellationTokenSource? ownerStop = null;
        CancellationTokenSource? joinedStop = null;
        Task? ownerLoop = null;
        Task? joinedLoop = null;

        try
        {
            await SeedAccountAsync(connection, issuer, accountHash, accountId, generationId, history,
                owner.Public.DeviceId, ownerSessionHash, joined.Public.DeviceId, joinedSessionHash);
            var seeded = await new BootstrapChallengeLedger(dataSource).GetDeviceAccountContextAsync(issuer, subject,
                CancellationToken.None);
            check(seeded.DeviceLinked && seeded.AccountId == accountId && seeded.GenerationId == generationId,
                "The isolated PostgreSQL seed resolves under the same HTTPS issuer and subject hash used by the ledger");
            await VerifyPersistedMembershipAsync(dataSource, accountHash, accountId, generationId, history[^1], check);

            var ownerDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, ownerScope);
            var joinedDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, joinedScope);
            var ownerOutbox = new SettingsOutboxVault(Path.Combine(ownerDirectory, "settings-outbox.dpapi"), ownerScope);
            var joinedOutbox = new SettingsOutboxVault(Path.Combine(joinedDirectory, "settings-outbox.dpapi"), joinedScope);
            var ownerPreferences = NewPreferenceStore(ownerDirectory, new DesktopSettings(Theme: AppTheme.Dark,
                UiLanguage: "es", LastDurationMinutes: 50, ReduceMotion: true, EnforcementEnabled: true));
            var joinedPreferences = NewPreferenceStore(joinedDirectory, new DesktopSettings(Theme: AppTheme.Dark,
                UiLanguage: "es", LastDurationMinutes: 50, ReduceMotion: false, EnforcementEnabled: true));
            var overrides = new LocalSettingsOverrides();
            DevicePublicIdentity ResolveAuthor(long revision, string deviceId)
            {
                var signed = history.SingleOrDefault(item => item.ProposedRevision == revision)
                    ?? throw new CryptographicException("Missing test membership history.");
                var member = signed.Members.SingleOrDefault(item => item.DeviceId == deviceId);
                if (member is null || member.Status != "Active" ||
                    !member.Roles.Contains("WriteSettings", StringComparer.Ordinal))
                    throw new CryptographicException("Test membership does not authorize this settings author.");
                return new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey, member.EncryptionPublicKey);
            }

            using var ownerCrypto = new SettingsSyncCryptography(account, entityId, 1, 2, owner, key, ResolveAuthor);
            using var joinedCrypto = new SettingsSyncCryptography(account, entityId, 1, 2, joined, key, ResolveAuthor);
            var ledger = new ContentDocumentLedger(dataSource);
            var ownerTransport = new LedgerSettingsDocumentTransport(ledger, account, subject, accessToken,
                owner, ownerSession, entityId);
            var joinedTransport = new LedgerSettingsDocumentTransport(ledger, account, subject, accessToken,
                joined, joinedSession, entityId);

            var baseline = new GlobalSettings(50, "dark", "es");
            var ownerInitial = await new SettingsDocumentInitializer(ownerOutbox, ownerCrypto, ownerTransport)
                .InitializeAsync(baseline, ownerConfirmed: true);
            var joinedInitial = await new SettingsDocumentInitializer(joinedOutbox, joinedCrypto, joinedTransport)
                .InitializeAsync(baseline, ownerConfirmed: true);
            check(ownerInitial.Confirmed.Revision == 1 && joinedInitial.Confirmed == ownerInitial.Confirmed,
                "Automatic-sync clients initialize and decrypt the same global settings through the real API");

            ownerRuntime = CreateRuntime(ownerOutbox, ownerCrypto, ownerTransport, ownerPreferences, overrides);
            joinedRuntime = CreateRuntime(joinedOutbox, joinedCrypto, joinedTransport, joinedPreferences, overrides);
            await ownerRuntime.EnableAsync(ownerConfirmed: true);
            await joinedRuntime.EnableAsync(ownerConfirmed: true);
            ownerStop = new CancellationTokenSource();
            joinedStop = new CancellationTokenSource();
            ownerLoop = ownerRuntime.RunAsync(ownerStop.Token);
            joinedLoop = joinedRuntime.RunAsync(joinedStop.Token);
            _ = await WaitForStateAsync(ownerOutbox,
                state => state.SyncConsentEnabled && state.Confirmed.Revision == 1 && !state.Pending, ownerStop.Token);
            _ = await WaitForStateAsync(joinedOutbox,
                state => state.SyncConsentEnabled && state.Confirmed.Revision == 1 && !state.Pending, joinedStop.Token);

            var ownerLocal = new LocalSettingsCoordinator(ownerOutbox, ownerPreferences);
            var joinedLocal = new LocalSettingsCoordinator(joinedOutbox, joinedPreferences);
            await ownerLocal.EditSelectedAsync(baseline with { Theme = "light" }, SettingsFields.Theme,
                overrides, ownerStop.Token);
            check(ownerRuntime.NotifyLocalSettingsChanged(), "A committed local edit wakes the consented API sync loop");
            _ = await WaitForStateAsync(ownerOutbox,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values.Theme == "light" && !state.Pending,
                ownerStop.Token);
            check(joinedRuntime.NotifyConnectivityRestored(), "The second API sync loop accepts a connectivity wake-up");
            var joinedTheme = await WaitForStateAsync(joinedOutbox,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values.Theme == "light" && !state.Pending,
                joinedStop.Token);
            await AssertLocalProjectionAsync(joinedPreferences, expectedTheme: AppTheme.Light, expectedDuration: 50, joinedStop.Token);
            check(joinedTheme.Confirmed.Values == (await ownerOutbox.LoadAsync(joinedStop.Token))!.Confirmed.Values,
                "A second automatic client converges after the signed CAS write is committed in PostgreSQL");

            await StopRuntimeAsync(ownerRuntime, ownerStop, ownerLoop);
            await StopRuntimeAsync(joinedRuntime, joinedStop, joinedLoop);
            ownerRuntime = joinedRuntime = null;
            ownerStop = joinedStop = null;
            ownerLoop = joinedLoop = null;

            var ownerBeforeConflict = (await ownerOutbox.LoadAsync())!;
            var joinedBeforeConflict = (await joinedOutbox.LoadAsync())!;
            await ownerLocal.EditSelectedAsync(ownerBeforeConflict.Confirmed.Values with { DefaultDurationMinutes = 40 },
                SettingsFields.Duration, overrides);
            await joinedLocal.EditSelectedAsync(joinedBeforeConflict.Confirmed.Values with { DefaultDurationMinutes = 60 },
                SettingsFields.Duration, overrides);

            joinedRuntime = CreateRuntime(joinedOutbox, joinedCrypto, joinedTransport, joinedPreferences, overrides);
            joinedStop = new CancellationTokenSource();
            joinedLoop = joinedRuntime.RunAsync(joinedStop.Token); // Owner remains offline while the peer commits 60.
            _ = await WaitForStateAsync(joinedOutbox,
                state => state.Confirmed.Revision == 3 && state.Confirmed.Values.DefaultDurationMinutes == 60 && !state.Pending,
                joinedStop.Token);
            await StopRuntimeAsync(joinedRuntime, joinedStop, joinedLoop);
            joinedRuntime = null;
            joinedStop = null;
            joinedLoop = null;

            ownerRuntime = CreateRuntime(ownerOutbox, ownerCrypto, ownerTransport, ownerPreferences, overrides);
            ownerStop = new CancellationTokenSource();
            ownerLoop = ownerRuntime.RunAsync(ownerStop.Token);
            var conflict = await WaitForStateAsync(ownerOutbox,
                state => state.Confirmed.Revision == 3 && state.Conflicts == SettingsFields.Duration &&
                    state.Draft?.DefaultDurationMinutes == 40, ownerStop.Token);
            var conflictService = new SettingsConflictResolutionService(ownerOutbox, ownerPreferences, overrides, ownerRuntime);
            _ = await conflictService.ResolveAsync(conflict.Draft!, conflict.Draft!, conflict.Confirmed.Revision,
                conflict.Conflicts, ownerConfirmed: true, token: ownerStop.Token);
            var ownerResolved = await WaitForStateAsync(ownerOutbox,
                state => state.Confirmed.Revision == 4 && state.Confirmed.Values.DefaultDurationMinutes == 40 &&
                    state.Conflicts == SettingsFields.None && !state.Pending, ownerStop.Token);
            check(ownerResolved.Confirmed.Values.DefaultDurationMinutes == 40,
                "The real API exposes a duration CAS conflict and accepts an explicit resolution as the next revision");

            joinedRuntime = CreateRuntime(joinedOutbox, joinedCrypto, joinedTransport, joinedPreferences, overrides);
            joinedStop = new CancellationTokenSource();
            joinedLoop = joinedRuntime.RunAsync(joinedStop.Token);
            _ = await WaitForStateAsync(joinedOutbox,
                state => state.Confirmed.Revision == 4 && state.Confirmed.Values.DefaultDurationMinutes == 40 && !state.Pending,
                joinedStop.Token);
            var storedSettingsJson = await ReadStoredDocumentAsync(dataSource, accountId, entityId, joinedStop.Token);

            var beforeReset = (await ownerOutbox.LoadAsync(ownerStop.Token))!;
            _ = await ownerLocal.ResetSharedAsync(beforeReset.Confirmed.Revision, beforeReset.Confirmed.Values,
                ownerConfirmed: true, overrides: overrides, token: ownerStop.Token);
            check(ownerRuntime.NotifyLocalSettingsChanged(), "The committed tombstone reset wakes the automatic API sync loop");
            _ = await WaitForStateAsync(ownerOutbox,
                state => state.Confirmed.Revision == 5 && state.Confirmed.Deleted && !state.Pending, ownerStop.Token);
            check(joinedRuntime.NotifyConnectivityRestored(), "The peer wakes to apply the encrypted reset tombstone");
            var joinedReset = await WaitForStateAsync(joinedOutbox,
                state => state.Confirmed.Revision == 5 && state.Confirmed.Deleted && !state.Pending, joinedStop.Token);
            check(joinedReset.Confirmed.EffectiveValues == GlobalSettings.Default,
                "The real PostgreSQL API delivers the encrypted shared-settings tombstone to the peer");
            await AssertLocalProjectionAsync(joinedPreferences, expectedTheme: AppTheme.Dark, expectedDuration: 50, joinedStop.Token);

            var storedResetJson = await ReadStoredDocumentAsync(dataSource, accountId, entityId, ownerStop.Token);
            var ownerCursor = (await ownerOutbox.LoadAsync(ownerStop.Token))!.Cursor;
            var joinedCursor = (await joinedOutbox.LoadAsync(joinedStop.Token))!.Cursor;
            check(storedSettingsJson is not null && storedResetJson is not null &&
                !storedSettingsJson.Contains("\"theme\"", StringComparison.Ordinal) &&
                !storedSettingsJson.Contains("\"defaultDurationMinutes\"", StringComparison.Ordinal) &&
                !storedSettingsJson.Contains("\"uiLanguage\"", StringComparison.Ordinal) &&
                !storedResetJson.Contains("\"deleted\":true", StringComparison.Ordinal),
                "PostgreSQL retains only the signed ciphertext for settings and reset documents");
            check(ownerCursor == 5 && joinedCursor == ownerCursor,
                "The account-wide cursor advances separately from document revision and persists in both DPAPI outboxes");

            await VerifyHostSessionsAgainstLedgerAsync(connection, account, subject, accessToken, owner, joined,
                ownerSession, joinedSession, key, history, root, check);
        }
        finally
        {
            if (ownerRuntime is not null && ownerStop is not null && ownerLoop is not null)
                await StopRuntimeAsync(ownerRuntime, ownerStop, ownerLoop);
            if (joinedRuntime is not null && joinedStop is not null && joinedLoop is not null)
                await StopRuntimeAsync(joinedRuntime, joinedStop, joinedLoop);
            ownerStop?.Dispose();
            joinedStop?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(ownerSessionHash);
            CryptographicOperations.ZeroMemory(joinedSessionHash);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            await CleanupSubjectAsync(connection, accountHash);
        }
    }

    private static async Task VerifyHostSessionsAgainstLedgerAsync(string connection,
        DeviceAccountContext account, string subject, string accessToken, DeviceIdentity owner,
        DeviceIdentity joined, string ownerDeviceSession, string joinedDeviceSession, byte[] accountContentKey,
        MembershipDocument[] membershipHistory, string root, Action<bool, string> check)
    {
        var digest = MembershipProtocol.Digest(membershipHistory[^1]);
        var ownerVault = new WindowsDeviceVault(Path.Combine(root, "host-owner-device.dpapi"));
        var joinedVault = new WindowsDeviceVault(Path.Combine(root, "host-joined-device.dpapi"));
        await ownerVault.SaveAsync(owner, null, account, accountContentKey, membershipHistory[^1].ProposedRevision,
            digest, CancellationToken.None, ownerDeviceSession);
        await joinedVault.SaveAsync(joined, null, account, accountContentKey, membershipHistory[^1].ProposedRevision,
            digest, CancellationToken.None, joinedDeviceSession);
        WindowsDeviceVault.LoadedDeviceVault? ownerLoaded = await ownerVault.LoadForAccountAsync(account.Issuer,
            account.AccountBinding, CancellationToken.None) ?? throw new InvalidDataException("Owner DPAPI vault did not restore.");
        WindowsDeviceVault.LoadedDeviceVault? joinedLoaded = await joinedVault.LoadForAccountAsync(account.Issuer,
            account.AccountBinding, CancellationToken.None) ?? throw new InvalidDataException("Peer DPAPI vault did not restore.");

        var ownerPreferences = new DesktopSettingsStore(Path.Combine(root, "host-owner-desktop-settings.json"));
        _ = ownerPreferences.Load();
        ownerPreferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es",
            LastDurationMinutes: 50, ReduceMotion: true, EnforcementEnabled: true));
        var joinedPreferences = new DesktopSettingsStore(Path.Combine(root, "host-joined-desktop-settings.json"));
        _ = joinedPreferences.Load();
        joinedPreferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Dark, UiLanguage: "es",
            LastDurationMinutes: 50, ReduceMotion: false, EnforcementEnabled: true));

        var entityId = HostSettingsSyncSession.GlobalSettingsEntityId;
        using var apiEnvironment = new SyntheticApiEnvironmentScope(account.Issuer, connection);
        await using var apiFactory = CreateApiFixture(connection, account.Issuer, subject, accessToken);
        using var apiClient = apiFactory.CreateClient();
        var ownerSender = new TestApiSettingsRequestSender(apiClient, account.AccountBinding, accessToken);
        var joinedSender = new TestApiSettingsRequestSender(apiClient, account.AccountBinding, accessToken);
        var apiOrigin = new Uri("https://api.example.invalid/");
        var ownerTransport = new SettingsDocumentHttpTransport(apiOrigin, ownerSender, account,
            owner, ownerDeviceSession, entityId);
        var joinedTransport = new SettingsDocumentHttpTransport(apiOrigin, joinedSender, account,
            joined, joinedDeviceSession, entityId);
        HostSettingsSyncSession? ownerHost = null;
        HostSettingsSyncSession? joinedHost = null;
        using var stop = new CancellationTokenSource();
        Task? ownerLoop = null;
        Task? joinedLoop = null;
        try
        {
            ownerHost = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, ownerTransport,
                ownerLoaded, membershipHistory, ownerPreferences, Path.Combine(root, "host-sync"));
            ownerLoaded = null;
            joinedHost = HostSettingsSyncSession.CreateForTesting(account.AccountBinding, joinedTransport,
                joinedLoaded, membershipHistory, joinedPreferences, Path.Combine(root, "host-sync"));
            joinedLoaded = null;

            await ownerHost.EnableAsync(ownerConfirmed: true);
            await joinedHost.EnableAsync(ownerConfirmed: true);
            ownerLoop = ownerHost.RunAsync(stop.Token);
            joinedLoop = joinedHost.RunAsync(stop.Token);
            var ownerInitial = await ownerHost.Outbox.LoadAsync(stop.Token) ??
                throw new InvalidDataException("Owner Host settings outbox is missing.");
            var joinedInitial = await joinedHost.Outbox.LoadAsync(stop.Token) ??
                throw new InvalidDataException("Peer Host settings outbox is missing.");
            check(ownerInitial.Confirmed.Revision == 1 && joinedInitial.Confirmed == ownerInitial.Confirmed &&
                ownerInitial.SyncConsentEnabled && joinedInitial.SyncConsentEnabled,
                "Host sessions initialize their own DPAPI state after explicit consent");

            await ownerHost.Edits.EditSelectedAsync(ownerInitial.Confirmed.Values with { Theme = "light" },
                SettingsFields.Theme, new LocalSettingsOverrides(), stop.Token);
            var ownerUpdated = await WaitForStateAsync(ownerHost.Outbox,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values.Theme == "light" && !state.Pending,
                stop.Token);
            check(joinedHost.NotifyConnectivityRestored(), "The peer Host accepts an actual connectivity notification");
            var joinedUpdated = await WaitForStateAsync(joinedHost.Outbox,
                state => state.Confirmed.Revision == 2 && state.Confirmed.Values.Theme == "light" && !state.Pending,
                stop.Token);
            var ownerLocal = ownerPreferences.Load();
            var joinedLocal = joinedPreferences.Load();
            check(ownerUpdated.Cursor == 7 && joinedUpdated.Cursor == 7 &&
                ownerUpdated.Confirmed == joinedUpdated.Confirmed && ownerLocal.Theme == AppTheme.Light &&
                joinedLocal.Theme == AppTheme.Light && ownerLocal.ReduceMotion && !joinedLocal.ReduceMotion &&
                ownerLocal.EnforcementEnabled && joinedLocal.EnforcementEnabled,
                "Host sessions converge through API routes, persist the separate account cursor, and preserve local preferences");

            var cursorPollsBefore = joinedSender.CursorPolls;
            var documentReadsBefore = joinedSender.DocumentReads;
            check(joinedHost.NotifyConnectivityRestored(), "The peer accepts a second unchanged-cursor wake-up");
            var cursorDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (joinedSender.CursorPolls == cursorPollsBefore && DateTimeOffset.UtcNow < cursorDeadline)
                await Task.Delay(20, stop.Token);
            check(joinedSender.CursorPolls > cursorPollsBefore && joinedSender.DocumentReads == documentReadsBefore,
                "An unchanged account cursor avoids downloading the unchanged encrypted document");
        }
        finally
        {
            stop.Cancel();
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
            if (ownerHost is not null) await ownerHost.DisposeAsync();
            else ownerLoaded?.Dispose();
            if (joinedHost is not null) await joinedHost.DisposeAsync();
            else joinedLoaded?.Dispose();
        }
    }

    private const string SyntheticAuthScheme = "Phase3SyntheticBearer";

    private sealed class SyntheticApiEnvironmentScope : IDisposable
    {
        private readonly string? previousAuthority = Environment.GetEnvironmentVariable("Identity__Authority");
        private readonly string? previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Product");

        public SyntheticApiEnvironmentScope(string issuer, string connection)
        {
            Environment.SetEnvironmentVariable("Identity__Authority", issuer);
            Environment.SetEnvironmentVariable("ConnectionStrings__Product", connection);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("Identity__Authority", previousAuthority);
            Environment.SetEnvironmentVariable("ConnectionStrings__Product", previousConnection);
        }
    }

    private static WebApplicationFactory<ApiEntryPoint> CreateApiFixture(string connection, string issuer,
        string subject, string accessToken) => new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../api")));
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Identity:Authority"] = issuer,
                ["ConnectionStrings:Product"] = connection
            }));
        builder.ConfigureServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                options => options.Authority = issuer);
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SyntheticAuthScheme;
                options.DefaultChallengeScheme = SyntheticAuthScheme;
            }).AddScheme<SyntheticBearerOptions, SyntheticBearerHandler>(SyntheticAuthScheme, options =>
            {
                options.ExpectedToken = accessToken;
                options.Subject = subject;
            });
            services.RemoveAll<HttpClient>();
            services.AddSingleton(_ => new HttpClient(new SyntheticUserInfoHandler(issuer, subject, accessToken)));
        });
    });

    private sealed class SyntheticBearerOptions : AuthenticationSchemeOptions
    {
        public string ExpectedToken { get; set; } = "";
        public string Subject { get; set; } = "";
    }

    private sealed class SyntheticBearerHandler(IOptionsMonitor<SyntheticBearerOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<SyntheticBearerOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization.ToString(), "Bearer " + Options.ExpectedToken,
                StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("sub", Options.Subject)], Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class SyntheticUserInfoHandler(string issuer, string subject, string accessToken)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var expectedUri = issuer.TrimEnd('/') + "/protocol/openid-connect/userinfo";
            if (!string.Equals(request.RequestUri?.AbsoluteUri, expectedUri, StringComparison.Ordinal) ||
                !string.Equals(request.Headers.Authorization?.Parameter, accessToken, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { sub = subject, email_verified = true })
            });
        }
    }

    private sealed class TestApiSettingsRequestSender(HttpClient client, string accountBinding,
        string accessToken) : ISettingsRequestSender
    {
        private int cursorPolls;
        private int documentReads;
        public string? AccountBinding => accountBinding;
        public int CursorPolls => Volatile.Read(ref cursorPolls);
        public int DocumentReads => Volatile.Read(ref documentReads);

        public async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> requestFactory,
            CancellationToken token)
        {
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
            var digest = DeviceEncoding.Encode(tokenHash);
            CryptographicOperations.ZeroMemory(tokenHash);
            using var request = requestFactory(digest);
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/v1/sync/cursor")
                Interlocked.Increment(ref cursorPolls);
            else if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.StartsWith(
                "/v1/documents/", StringComparison.Ordinal) == true)
                Interlocked.Increment(ref documentReads);
            request.RequestUri = new Uri("http://localhost" + request.RequestUri!.PathAndQuery);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
    }

    private static DesktopSettingsSyncStore NewPreferenceStore(string directory, DesktopSettings baseline)
    {
        var path = Path.Combine(directory, "desktop-settings.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(baseline, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllBytes(path, bytes);
        return new DesktopSettingsSyncStore(path);
    }

    private static SettingsAutomaticSyncRuntime CreateRuntime(SettingsOutboxVault outbox,
        SettingsSyncCryptography cryptography, ISettingsDocumentTransport transport,
        DesktopSettingsSyncStore preferences, LocalSettingsOverrides overrides)
    {
        var consent = new SettingsConsentStore(outbox);
        return new SettingsAutomaticSyncRuntime(consent, (enabled, persistedConsent) =>
            new SettingsSyncDesktopTick(new SettingsSyncWorker(outbox, cryptography, transport,
                enabled, persistedConsent), outbox, preferences, overrides).RunAsync);
    }

    private static async Task StopRuntimeAsync(SettingsAutomaticSyncRuntime runtime, CancellationTokenSource stop,
        Task loop)
    {
        stop.Cancel();
        try { await loop; }
        catch (OperationCanceledException) { }
        await runtime.DisposeAsync();
        stop.Dispose();
    }

    private static async Task<SettingsOutbox> WaitForStateAsync(SettingsOutboxVault outbox,
        Func<SettingsOutbox, bool> predicate, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var state = await outbox.LoadAsync(token);
            if (state is not null && predicate(state)) return state;
            await Task.Delay(25, token);
        }
        throw new TimeoutException("Automatic settings sync did not converge through the real PostgreSQL API.");
    }

    private static async Task<string?> ReadStoredDocumentAsync(NpgsqlDataSource database, string accountId,
        string entityId, CancellationToken token)
    {
        await using var command = database.CreateCommand(
            "SELECT document::text FROM focusguard_device.documents WHERE account_id=$1 AND entity_id=$2");
        command.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
        command.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
        return (string?)await command.ExecuteScalarAsync(token);
    }

    private static async Task AssertLocalProjectionAsync(DesktopSettingsSyncStore preferences, AppTheme expectedTheme,
        int expectedDuration, CancellationToken token)
    {
        var local = await preferences.LoadAsync(token);
        if (local.Theme != expectedTheme || local.LastDurationMinutes != expectedDuration ||
            !local.EnforcementEnabled || local.ReduceMotion)
            throw new InvalidDataException("The real-API sync changed a device-local preference or missed the shared projection.");
    }

    private static async Task SeedAccountAsync(string connection, string issuer, string subjectHash,
        string accountId, string generationId, MembershipDocument[] history,
        string ownerDeviceId, byte[] ownerSessionHash, string joinedDeviceId, byte[] joinedSessionHash)
    {
        await CleanupSubjectAsync(connection, subjectHash);
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync();
        await using var transaction = await database.BeginTransactionAsync();
        var current = history[^1];
        var currentDigest = DeviceEncoding.Decode(MembershipProtocol.Digest(current), 32);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO focusguard_device.accounts(account_id,issuer,subject_hash,generation_id,membership_revision,membership_digest,membership)
            VALUES ($1,$2,$3,$4,$5,$6,$7::jsonb)
            """, database, transaction))
        {
            insert.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            insert.Parameters.AddWithValue(issuer);
            insert.Parameters.AddWithValue(subjectHash);
            insert.Parameters.AddWithValue(Guid.ParseExact(generationId, "D"));
            insert.Parameters.AddWithValue(current.ProposedRevision);
            insert.Parameters.AddWithValue(currentDigest);
            insert.Parameters.AddWithValue(JsonSerializer.Serialize(current));
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var membership in history)
        {
            var digest = DeviceEncoding.Decode(MembershipProtocol.Digest(membership), 32);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO focusguard_device.membership_history(account_id,generation_id,membership_revision,membership_digest,membership)
                VALUES ($1,$2,$3,$4,$5::jsonb)
                """, database, transaction);
            insert.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            insert.Parameters.AddWithValue(Guid.ParseExact(generationId, "D"));
            insert.Parameters.AddWithValue(membership.ProposedRevision);
            insert.Parameters.AddWithValue(digest);
            insert.Parameters.AddWithValue(JsonSerializer.Serialize(membership));
            await insert.ExecuteNonQueryAsync();
            CryptographicOperations.ZeroMemory(digest);
        }

        foreach (var item in new[] { (ownerDeviceId, ownerSessionHash), (joinedDeviceId, joinedSessionHash) })
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO focusguard_device.device_sessions(session_hash,account_id,generation_id,device_id,membership_revision,expires_at,idle_until)
                VALUES ($1,$2,$3,$4,$5,NOW()+INTERVAL '4 hours',NOW()+INTERVAL '30 minutes')
                """, database, transaction);
            insert.Parameters.AddWithValue(item.Item2);
            insert.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            insert.Parameters.AddWithValue(Guid.ParseExact(generationId, "D"));
            insert.Parameters.AddWithValue(Guid.ParseExact(item.Item1, "D"));
            insert.Parameters.AddWithValue(current.ProposedRevision);
            await insert.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        CryptographicOperations.ZeroMemory(currentDigest);
    }

    private static async Task CleanupSubjectAsync(string connection, string subjectHash)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM focusguard_device.documents WHERE account_id IN (SELECT account_id FROM focusguard_device.accounts WHERE subject_hash=$1)",
            "DELETE FROM focusguard_device.pairings WHERE subject_hash=$1",
            "DELETE FROM focusguard_device.device_sessions WHERE account_id IN (SELECT account_id FROM focusguard_device.accounts WHERE subject_hash=$1)",
            "DELETE FROM focusguard_device.request_proofs WHERE account_id IN (SELECT account_id FROM focusguard_device.accounts WHERE subject_hash=$1)",
            "DELETE FROM focusguard_device.membership_history WHERE account_id IN (SELECT account_id FROM focusguard_device.accounts WHERE subject_hash=$1)",
            "DELETE FROM focusguard_device.accounts WHERE subject_hash=$1",
            "DELETE FROM focusguard_device.challenges WHERE subject_hash=$1"
        })
        {
            await using var command = new NpgsqlCommand(sql, database);
            command.Parameters.AddWithValue(subjectHash);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyPersistedMembershipAsync(NpgsqlDataSource database, string subjectHash,
        string accountId, string generationId, MembershipDocument expected, Action<bool, string> check)
    {
        await using var command = database.CreateCommand("""
            SELECT account_id,generation_id,membership_revision,membership_digest,membership::text
            FROM focusguard_device.accounts WHERE subject_hash=$1
            """);
        command.Parameters.AddWithValue(subjectHash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidDataException("Seeded account row disappeared.");
        var stored = JsonSerializer.Deserialize<MembershipDocument>(reader.GetString(4),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Seeded membership is missing.");
        var digest = DeviceEncoding.Encode((byte[])reader[3]);
        check(stored.AccountId == accountId && stored.GenerationId == generationId &&
            reader.GetInt64(2) == expected.ProposedRevision, "Persisted API account routing and membership revision match the fixture");
        check(MembershipProtocol.VerifyMembership(stored), "PostgreSQL membership serialization preserves its signature");
        check(MembershipProtocol.Digest(stored) == digest && MembershipProtocol.Digest(expected) == digest,
            "PostgreSQL membership digest matches the signed chain");
    }

    private static MembershipDocument Sign(MembershipDocument membership, DeviceIdentity signer)
    {
        var canonical = MembershipProtocol.CanonicalMembership(membership);
        try { return membership with { Signature = signer.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    private static string NewSession()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return DeviceEncoding.Encode(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] SessionHash(string session)
    {
        var bytes = DeviceEncoding.Decode(session, 32);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class LedgerSettingsDocumentTransport(ContentDocumentLedger ledger,
        DeviceAccountContext account, string subject, string accessToken, DeviceIdentity device,
        string deviceSession, string entityId) : ISettingsDocumentTransport
    {
        private readonly string target = "/v1/documents/" + entityId;
        private long lastSeenCursor;
        public long? LastSeenCursor
        {
            get
            {
                var cursor = Interlocked.Read(ref lastSeenCursor);
                return cursor > 0 ? cursor : null;
            }
        }

        public async Task<long?> ReadCursorAsync(CancellationToken token)
        {
            const string cursorTarget = "/v1/sync/cursor";
            var requestId = Guid.NewGuid().ToString("D");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var proof = CreateProof("GET", requestId, timestamp, ReadOnlySpan<byte>.Empty, cursorTarget);
            var cursor = await ledger.ReadAccountCursorAsync(account.Issuer, subject, accessToken,
                cursorTarget, device.Public.DeviceId, deviceSession, requestId, timestamp, proof, token);
            RecordCursor(cursor);
            return cursor;
        }

        public async Task<byte[]?> ReadAsync(CancellationToken token)
        {
            var requestId = Guid.NewGuid().ToString("D");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var proof = CreateProof("GET", requestId, timestamp, ReadOnlySpan<byte>.Empty);
            var result = await ledger.ReadSnapshotAsync(account.Issuer, subject, accessToken, target,
                device.Public.DeviceId, deviceSession, requestId, timestamp, proof, entityId, token);
            RecordCursor(result.Cursor);
            return result.Document is null ? null : JsonSerializer.SerializeToUtf8Bytes(result.Document,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public async Task<SettingsPublishResult> PublishAsync(Guid operationId,
            ReadOnlyMemory<byte> envelope, CancellationToken token)
        {
            if (operationId == Guid.Empty) throw new ArgumentException("A settings operation ID is required.");
            var document = JsonSerializer.Deserialize<EncryptedContentDocument>(envelope.Span,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("The settings envelope is missing.");
            var body = ContentDocumentCodec.CanonicalDocumentBody(document);
            try
            {
                var requestId = Guid.NewGuid().ToString("D");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var proof = CreateProof("POST", requestId, timestamp, body);
                try
                {
                    var result = await ledger.WriteAsync(account.Issuer, subject, accessToken, target,
                        device.Public.DeviceId, deviceSession, requestId, timestamp, proof, entityId, document, token);
                    RecordCursor(result.Cursor);
                    return SettingsPublishResult.Stored;
                }
                catch (MembershipConflictException) { return SettingsPublishResult.RevisionConflict; }
            }
            finally { CryptographicOperations.ZeroMemory(body); }
        }

        private string CreateProof(string method, string requestId, long timestamp, ReadOnlySpan<byte> body,
            string? requestTarget = null)
        {
            var tokenDigest = DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
            var proof = new DeviceRequestProof(method, requestTarget ?? target, account.AccountId, account.GenerationId,
                device.Public.DeviceId, requestId, timestamp, tokenDigest, DeviceEncoding.Encode(SHA256.HashData(body)));
            var canonical = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try { return device.Sign("FG-REQUEST-1", canonical); }
            finally { CryptographicOperations.ZeroMemory(canonical); }
        }

        private void RecordCursor(long cursor)
        {
            if (cursor < Interlocked.Read(ref lastSeenCursor))
                throw new InvalidDataException("Account sync cursor rollback rejected in the API ledger test transport.");
            Interlocked.Exchange(ref lastSeenCursor, cursor);
        }
    }
}
