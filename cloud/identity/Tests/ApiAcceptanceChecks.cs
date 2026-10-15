using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using FocusGuard.Devices;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using Npgsql;
using FocusGuard.Cloud.Api;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Identity;
using FocusGuard.Sync;
using DocumentLifetime = FocusGuard.Host.Windows.Hosting.DocumentLifetime;
using HostAccountAction = FocusGuard.Host.Windows.Hosting.HostAccountAction;
using HostAccountService = FocusGuard.Host.Windows.Hosting.HostAccountService;
using HostSettingsSyncSession = FocusGuard.Host.Windows.Hosting.HostSettingsSyncSession;
using VerifiedNativeHostAuthorization = FocusGuard.Host.Windows.Hosting.VerifiedNativeHostAuthorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

internal static class ApiAcceptanceChecks
{
    public static async Task<WebApplicationFactory<ApiEntryPoint>> RunAsync(string issuer, string accessToken,
        string idToken, string otherAccountAccessToken, string connection, Action<bool, string> check)
    {
        var factory = new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); // Local synthetic fixture only; production diagnostics remain disabled.
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../api")));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Authority"] = "https://identity-fixture.example.invalid/realms/focusguard-local-test",
                ["ConnectionStrings:Product"] = connection
            }));
            // HTTP exception only in this loopback fixture. Production startup
            // still requires HTTPS; no fake signatures or mocked identity server.
            builder.ConfigureServices(services =>
            {
                // Match production's HTTP 400 response for malformed JSON instead
                // of Development throwing BadHttpRequestException through TestServer.
                services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = issuer;
                    options.MetadataAddress = issuer + "/.well-known/openid-configuration";
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters.ValidIssuer = issuer;
                });
            });
        });
        using var client = factory.CreateClient();
        var jwtOptions = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        var manager = (Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>)jwtOptions.ConfigurationManager!;
        check(manager.MetadataAddress == issuer + "/.well-known/openid-configuration", "API test discovery binds to real fixture before metadata manager construction");
        check(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(accessToken).Audiences.Contains("focusguard-api"), "real access token carries explicit API audience");
        using var live = await client.GetAsync("/health/live");
        check(live.IsSuccessStatusCode, "API live endpoint available");
        using var ready = await client.GetAsync("/health/ready");
        check(ready.IsSuccessStatusCode, "API readiness verifies the configured PostgreSQL connection");
        using var anonymous = await client.GetAsync("/v1/account");
        check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "API anonymous account read rejected");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        using var wrongAudience = await client.GetAsync("/v1/account");
        check(wrongAudience.StatusCode == HttpStatusCode.Unauthorized, "API ID token cannot replace access token");
        var parts = accessToken.Split('.');
        var signature = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1; parts[2] = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(signature);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));
        using var altered = await client.GetAsync("/v1/account");
        check(altered.StatusCode == HttpStatusCode.Unauthorized, "API altered JWT signature rejected");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var verifiedSubject = string.Empty;
        using (var identityHttp = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(15) })
        {
            using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, issuer + "/protocol/openid-connect/userinfo");
            userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var userInfoResponse = await identityHttp.SendAsync(userInfoRequest);
            var userInfoContent = await userInfoResponse.Content.ReadAsStringAsync();
            using var userInfo = JsonDocument.Parse(userInfoContent);
            check(userInfoResponse.IsSuccessStatusCode, "live Keycloak userinfo validates access token");
            check(userInfo.RootElement.TryGetProperty("email_verified", out var verifiedClaim) && verifiedClaim.ValueKind == JsonValueKind.True, "live Keycloak confirms verified email");
            verifiedSubject = userInfo.RootElement.GetProperty("sub").GetString()!;
        }
        await CleanupBootstrapFixtureAsync(connection, issuer, verifiedSubject);
        using var account = await client.GetAsync("/v1/account");
        var json = await account.Content.ReadAsStringAsync();
        check(account.IsSuccessStatusCode, "API verified account accepted with real Keycloak JWT/session (HTTP " + (int)account.StatusCode + ", body " + json + ")");
        using var parsed = JsonDocument.Parse(json);
        check(parsed.RootElement.GetProperty("deviceLinked").GetBoolean() == false, "API login does not grant device membership");
        check(!json.Contains("token", StringComparison.OrdinalIgnoreCase) && !json.Contains("password", StringComparison.OrdinalIgnoreCase), "API response excludes credentials");
        check(account.Headers.CacheControl?.NoStore == true, "API account response has no-store");
        using var foreignAccountClient = factory.CreateClient();
        await VerifyBootstrapAsync(factory, client, accessToken, issuer, verifiedSubject, connection, check,
            otherAccountAccessToken, foreignAccountClient);
        if (Environment.GetEnvironmentVariable("FG_PHASE3_API_SYNC_TEST") == "1")
            await ApiAutomaticSettingsSyncChecks.RunAsync(connection, check);
        return factory;
    }

    private static async Task CleanupBootstrapFixtureAsync(string connection, string issuer, string subject)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuer + "\0" + subject)));
        await using var cleanup = new NpgsqlConnection(connection);
        await cleanup.OpenAsync();
        foreach (var commandText in new[]
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
            await using var command = new NpgsqlCommand(commandText, cleanup);
            command.Parameters.AddWithValue(hash);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task VerifyBootstrapAsync(WebApplicationFactory<ApiEntryPoint> factory,
        HttpClient client, string token, string issuer, string subject,
        string connection, Action<bool, string> check, string otherAccountAccessToken, HttpClient foreignAccountClient)
    {
        var accountHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            issuer + "\0" + subject)));
        await using (var initialCleanupConnection = new NpgsqlConnection(connection))
        {
            await initialCleanupConnection.OpenAsync();
            await using var removePairings = new NpgsqlCommand("DELETE FROM focusguard_device.pairings WHERE subject_hash=$1", initialCleanupConnection);
            removePairings.Parameters.AddWithValue(accountHash);
            await removePairings.ExecuteNonQueryAsync();
            await using var removeDocuments = new NpgsqlCommand("DELETE FROM focusguard_device.documents WHERE account_id IN (SELECT account_id FROM focusguard_device.accounts WHERE subject_hash=$1)", initialCleanupConnection);
            removeDocuments.Parameters.AddWithValue(accountHash);
            await removeDocuments.ExecuteNonQueryAsync();
            await using var removeAccounts = new NpgsqlCommand("DELETE FROM focusguard_device.accounts WHERE subject_hash=$1", initialCleanupConnection);
            removeAccounts.Parameters.AddWithValue(accountHash);
            await removeAccounts.ExecuteNonQueryAsync();
            await using var removeChallenges = new NpgsqlCommand("DELETE FROM focusguard_device.challenges WHERE subject_hash=$1", initialCleanupConnection);
            removeChallenges.Parameters.AddWithValue(accountHash);
            await removeChallenges.ExecuteNonQueryAsync();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var identity = DeviceIdentity.Create();
        using var recovery = RecoveryKit.Generate();
        var accountId = Guid.NewGuid().ToString("D");
        var generationId = Guid.NewGuid().ToString("D");
        var initialSessionBytes = RandomNumberGenerator.GetBytes(32);
        var initialDeviceSession = DeviceEncoding.Encode(initialSessionBytes);
        CryptographicOperations.ZeroMemory(initialSessionBytes);
        var request = new BootstrapChallengeRequest(accountId, generationId, identity.Public.DeviceId,
            identity.Public.SigningPublicKey, identity.Public.EncryptionPublicKey,
            recovery.Public.SigningPublicKey, recovery.Public.EncryptionPublicKey);
        using var challengeReply = await client.PostAsJsonAsync("/v1/bootstrap/challenges", request);
        if (challengeReply.StatusCode != HttpStatusCode.Created)
        {
            var safeError = "unclassified";
            try { using var error = JsonDocument.Parse(await challengeReply.Content.ReadAsStringAsync()); if (error.RootElement.TryGetProperty("diagnostic", out var diagnostic)) safeError = diagnostic.GetString() ?? safeError; else if (error.RootElement.TryGetProperty("sqlState", out diagnostic)) safeError = "sqlstate-" + diagnostic.GetString(); else if (error.RootElement.TryGetProperty("error", out var code)) safeError = code.GetString() ?? safeError; }
            catch (JsonException) { }
            throw new InvalidOperationException($"Bootstrap challenge returned HTTP {(int)challengeReply.StatusCode} ({safeError}).");
        }
        check(challengeReply.StatusCode == HttpStatusCode.Created, "Bootstrap requires verified OIDC and issues a bounded challenge");
        var challenge = await challengeReply.Content.ReadFromJsonAsync<BootstrapChallengeResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Bootstrap challenge missing.");
        check(challenge.AccountId == accountId && challenge.GenerationId == generationId && challenge.DeviceId == identity.Public.DeviceId && challenge.ExpiresAt > DateTimeOffset.UtcNow, "Bootstrap challenge binds account, generation and installation");

        var proof = new BootstrapChallengeProof(challenge.ChallengeId, accountId, generationId, identity.Public.DeviceId, challenge.Nonce);
        var proofSignature = identity.Sign(MembershipProtocol.ChallengeProofDomain, MembershipProtocol.CanonicalChallenge(proof));
        var initial = new MembershipDocument("FG-MEMBERSHIP-1", accountId, generationId, 0, 1, 1,
            DeviceEncoding.Encode(new byte[32]), [new DeviceMember(identity.Public.DeviceId, identity.Public.SigningPublicKey,
                identity.Public.EncryptionPublicKey, "Active", ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"])],
            recovery.Public.SigningPublicKey, recovery.Public.EncryptionPublicKey,
            identity.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        initial = initial with { Signature = identity.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(initial)) };
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var pairingRootKey = contentKey.ToArray();
        var membershipDigest = MembershipProtocol.Digest(initial);
        var deviceContext = new KeyEnvelopeContext(accountId, generationId, 1, identity.Public.DeviceId, membershipDigest, identity.Public.DeviceId);
        var recoveryContext = deviceContext with { RecipientId = accountId };
        var envelopes = new[]
        {
            SignedKeyEnvelopeCodec.SealForDevice(contentKey, deviceContext, identity.Public.EncryptionPublicKey, identity),
            SignedKeyEnvelopeCodec.SealForDevice(contentKey, recoveryContext, recovery.Public.EncryptionPublicKey, identity)
        };
        try
        {
            using var recoveredDeviceKey = new SensitiveContentKey(SignedKeyEnvelopeCodec.OpenForDevice(envelopes[0], deviceContext, identity.Public, identity));
            using var recoveredRecoveryKey = new SensitiveContentKey(SignedKeyEnvelopeCodec.OpenForRecovery(envelopes[1], recoveryContext, identity.Public, recovery));
            check(recoveredDeviceKey.Matches(contentKey) && recoveredRecoveryKey.Matches(contentKey), "Account content key opens only for the enrolled device and recovery kit");
        }
        finally { CryptographicOperations.ZeroMemory(contentKey); }

        var invalid = new RegisterFirstDeviceRequest(challenge.ChallengeId, proof, proofSignature + "A",
            initialDeviceSession, initial, envelopes);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await client.PostAsJsonAsync("/v1/bootstrap/accounts", invalid);
            check(rejected.StatusCode == HttpStatusCode.BadRequest, "Bootstrap rejects invalid installation proof without consuming account");
        }
        using (var exhausted = await client.PostAsJsonAsync("/v1/bootstrap/accounts", invalid))
            check(exhausted.StatusCode == HttpStatusCode.BadRequest, "Bootstrap challenge rejects proof replay after five attempts");

        using var secondReply = await client.PostAsJsonAsync("/v1/bootstrap/challenges", request);
        var secondChallenge = await secondReply.Content.ReadFromJsonAsync<BootstrapChallengeResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Second bootstrap challenge missing.");
        var secondProof = new BootstrapChallengeProof(secondChallenge.ChallengeId, accountId, generationId, identity.Public.DeviceId, secondChallenge.Nonce);
        var secondSignature = identity.Sign(MembershipProtocol.ChallengeProofDomain, MembershipProtocol.CanonicalChallenge(secondProof));
        var registration = new RegisterFirstDeviceRequest(secondChallenge.ChallengeId, secondProof, secondSignature,
            initialDeviceSession, initial, envelopes);
        using var created = await client.PostAsJsonAsync("/v1/bootstrap/accounts", registration);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        check(created.StatusCode == HttpStatusCode.Created && createdBody.GetProperty("deviceLinked").GetBoolean() &&
            createdBody.GetProperty("revision").GetInt64() == 1, "First signed device membership atomically anchors account revision 1");
        var initialSessionHash = SHA256.HashData(DeviceEncoding.Decode(initialDeviceSession, 32));
        await using (var initialSessionDb = new NpgsqlConnection(connection))
        {
            await initialSessionDb.OpenAsync();
            await using var storedInitialSession = new NpgsqlCommand("""
                SELECT count(*) FROM focusguard_device.device_sessions
                WHERE session_hash=$1 AND account_id=$2 AND device_id=$3 AND revoked_at IS NULL
                """, initialSessionDb);
            storedInitialSession.Parameters.AddWithValue(initialSessionHash);
            storedInitialSession.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            storedInitialSession.Parameters.AddWithValue(Guid.ParseExact(identity.Public.DeviceId, "D"));
            check((long)(await storedInitialSession.ExecuteScalarAsync())! == 1,
                "First-device bootstrap stores only a hash of its bounded native device session");
        }
        using var replay = await client.PostAsJsonAsync("/v1/bootstrap/accounts", registration);
        check(replay.StatusCode == HttpStatusCode.BadRequest, "Consumed first-device challenge rejects replay");
        using (var linkedState = await client.GetAsync("/v1/account"))
        {
            using var linkedJson = JsonDocument.Parse(await linkedState.Content.ReadAsStringAsync());
            check(linkedJson.RootElement.GetProperty("deviceLinked").GetBoolean(), "OIDC account state reflects the committed device membership");
        }
        var membershipTarget = "/v1/bootstrap/membership";
        var membershipRequestId = Guid.NewGuid().ToString("D");
        var membershipTime = DateTimeOffset.UtcNow;
        membershipTime = new DateTimeOffset(membershipTime.Ticks - membershipTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var membershipProof = new DeviceRequestProof("GET", membershipTarget, accountId, generationId,
            identity.Public.DeviceId, membershipRequestId, membershipTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
        client.DefaultRequestHeaders.Add("X-FG-Device-Id", identity.Public.DeviceId);
        client.DefaultRequestHeaders.Add("X-FG-Device-Session", initialDeviceSession);
        client.DefaultRequestHeaders.Add("X-FG-Request-Id", membershipRequestId);
        client.DefaultRequestHeaders.Add("X-FG-Timestamp", membershipTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var membershipProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(membershipProof);
        try { client.DefaultRequestHeaders.Add("X-FG-Proof", identity.Sign("FG-REQUEST-1", membershipProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(membershipProofBytes); }
        using (var membershipResponse = await client.GetAsync(membershipTarget))
        {
            var snapshot = await membershipResponse.Content.ReadFromJsonAsync<MembershipSnapshotResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Current membership response missing.");
            check(membershipResponse.StatusCode == HttpStatusCode.OK && snapshot.Membership.ProposedRevision == 1 &&
                snapshot.MembershipDigest == MembershipProtocol.Digest(snapshot.Membership) && snapshot.KeyEnvelopes.Length == 2 &&
                snapshot.MembershipChain?.Length == 1,
                "Active device proof reads the signed current membership, public-key envelopes and genesis chain");
        }
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Device-Session", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        using var pairingDevice = DeviceIdentity.Create();
        var pairingChallengeRequest = new BootstrapChallengeRequest(accountId, generationId, pairingDevice.Public.DeviceId,
            pairingDevice.Public.SigningPublicKey, pairingDevice.Public.EncryptionPublicKey, null, null);
        using var pairingChallengeResponse = await client.PostAsJsonAsync("/v1/bootstrap/challenges", pairingChallengeRequest);
        check(pairingChallengeResponse.StatusCode == HttpStatusCode.Created, "Linked account can issue a separate new-device pairing challenge");
        var pairingChallenge = await pairingChallengeResponse.Content.ReadFromJsonAsync<BootstrapChallengeResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Pairing challenge missing.");
        check(pairingChallenge.Purpose == "pairing", "Existing account challenge cannot be mistaken for first-device bootstrap");
        var capabilities = new DeviceCapabilities("Windows", "0.1.149", "Real", "Unsupported", ["Applications"], false, false);
        var challengeProof = new BootstrapChallengeProof(pairingChallenge.ChallengeId, accountId, generationId,
            pairingDevice.Public.DeviceId, pairingChallenge.Nonce);
        var firstDeviceAttemptSignature = pairingDevice.Sign(MembershipProtocol.ChallengeProofDomain,
            MembershipProtocol.CanonicalChallenge(challengeProof));
        using (var invalidFirstDevice = await client.PostAsJsonAsync("/v1/bootstrap/accounts",
            new RegisterFirstDeviceRequest(pairingChallenge.ChallengeId, challengeProof,
                firstDeviceAttemptSignature, initialDeviceSession, initial, envelopes)))
            check(invalidFirstDevice.StatusCode == HttpStatusCode.BadRequest,
                "Pairing challenge cannot be reused as initial account registration");
        var pairingProof = new BootstrapPairingProof(pairingChallenge.ChallengeId, challengeProof, capabilities);
        var pairingSignature = pairingDevice.Sign("FG-PAIRING-1", MembershipProtocol.CanonicalPairingProof(pairingProof));
        var pairingRequest = new BootstrapPairingRequest(pairingProof, pairingSignature);
        using var pairingCreated = await client.PostAsJsonAsync("/v1/bootstrap/pairings", pairingRequest);
        var pairing = await pairingCreated.Content.ReadFromJsonAsync<BootstrapPairingResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Pending pairing response missing.");
        await using (var expiryDb = new NpgsqlConnection(connection))
        {
            await expiryDb.OpenAsync();
            await using var expireConsumed = new NpgsqlCommand("UPDATE focusguard_device.challenges SET expires_at=NOW()-INTERVAL '1 second' WHERE id=$1", expiryDb);
            expireConsumed.Parameters.AddWithValue(Guid.ParseExact(pairingChallenge.ChallengeId, "D"));
            await expireConsumed.ExecuteNonQueryAsync();
        }
        using (var additionalChallenge = await client.PostAsJsonAsync("/v1/bootstrap/challenges", pairingChallengeRequest))
            check(additionalChallenge.StatusCode == HttpStatusCode.Created,
                "Expired challenge retained by a pairing does not break creation or violate its foreign key");
        check(pairingCreated.StatusCode == HttpStatusCode.Created && pairing.State == "Pending" && pairing.DeviceId == pairingDevice.Public.DeviceId,
            "New device possession proof creates a finite pending pairing");
        using var pendingList = await client.GetAsync("/v1/bootstrap/pairings/pending");
        using var pendingJson = JsonDocument.Parse(await pendingList.Content.ReadAsStringAsync());
        check(pendingList.IsSuccessStatusCode && pendingJson.RootElement.GetArrayLength() == 1 &&
            pendingJson.RootElement[0].GetProperty("fingerprint").GetString()!.Length == 43,
            "Account owner can see pending device keys and compare a public fingerprint");

        var nextMembershipDigest = MembershipProtocol.Digest(initial);
        var nextMembership = new MembershipDocument("FG-MEMBERSHIP-1", accountId, generationId, 1, 2, 1,
            nextMembershipDigest,
            [initial.Members[0], new DeviceMember(pairingDevice.Public.DeviceId,
                pairingDevice.Public.SigningPublicKey, pairingDevice.Public.EncryptionPublicKey,
                "Active", ["Read", "StartNormal", "WriteSettings"])],
            initial.RecoverySigningPublicKey, initial.RecoveryEncryptionPublicKey, identity.Public.DeviceId,
            DeviceEncoding.Encode(new byte[64]));
        nextMembership = nextMembership with { Signature = identity.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(nextMembership)) };
        var rekey = pairingRootKey.ToArray();
        var rekeyDigest = SHA256.HashData(rekey);
        var nextDigest = MembershipProtocol.Digest(nextMembership);
        var approvalEnvelopes = new[]
        {
            SignedKeyEnvelopeCodec.SealForDevice(rekey,
                new KeyEnvelopeContext(accountId, generationId, 1, identity.Public.DeviceId, nextDigest, identity.Public.DeviceId),
                identity.Public.EncryptionPublicKey, identity),
            SignedKeyEnvelopeCodec.SealForDevice(rekey,
                new KeyEnvelopeContext(accountId, generationId, 1, pairingDevice.Public.DeviceId, nextDigest, identity.Public.DeviceId),
                pairingDevice.Public.EncryptionPublicKey, identity),
            SignedKeyEnvelopeCodec.SealForDevice(rekey,
                new KeyEnvelopeContext(accountId, generationId, 1, accountId, nextDigest, identity.Public.DeviceId),
                recovery.Public.EncryptionPublicKey, identity)
        };
        CryptographicOperations.ZeroMemory(rekey);
        var approval = new PairingApprovalRequest(pairing.PairingId, nextMembership, approvalEnvelopes);
        var requestId = Guid.NewGuid().ToString("D");
        var requestTime = DateTimeOffset.UtcNow;
        requestTime = new DateTimeOffset(requestTime.Ticks - requestTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var requestTimestamp = requestTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var signedRequest = new DeviceRequestProof("POST", $"/v1/pairings/{pairing.PairingId}/approve",
            accountId, generationId, identity.Public.DeviceId, requestId, requestTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(MembershipProtocol.CanonicalPairingApprovalBody(approval))));
        client.DefaultRequestHeaders.Remove("X-FG-Device-Id");
        client.DefaultRequestHeaders.Remove("X-FG-Request-Id");
        client.DefaultRequestHeaders.Remove("X-FG-Timestamp");
        client.DefaultRequestHeaders.Remove("X-FG-Proof");
        client.DefaultRequestHeaders.Add("X-FG-Device-Id", identity.Public.DeviceId);
        client.DefaultRequestHeaders.Add("X-FG-Request-Id", requestId);
        client.DefaultRequestHeaders.Add("X-FG-Timestamp", requestTimestamp);
        client.DefaultRequestHeaders.Add("X-FG-Proof", identity.Sign("FG-REQUEST-1", MembershipProtocol.CanonicalDeviceRequestProof(signedRequest)));
        await using (var pendingExpiryDb = new NpgsqlConnection(connection))
        {
            await pendingExpiryDb.OpenAsync();
            await using var expirePending = new NpgsqlCommand(
                "UPDATE focusguard_device.pairings SET expires_at=NOW()-INTERVAL '1 second' WHERE pairing_id=$1 AND state='Pending'", pendingExpiryDb);
            expirePending.Parameters.AddWithValue(Guid.ParseExact(pairing.PairingId, "D"));
            await expirePending.ExecuteNonQueryAsync();
            using var lateApproval = await client.PostAsJsonAsync($"/v1/pairings/{pairing.PairingId}/approve", approval);
            check(lateApproval.StatusCode == HttpStatusCode.NotFound,
                "A Pending request cannot gain membership after its approval deadline");
            // Restore the original timestamp only in this synthetic fixture to
            // exercise on-time approval and subsequently delayed key receipt.
            await using var restorePending = new NpgsqlCommand(
                "UPDATE focusguard_device.pairings SET expires_at=$1 WHERE pairing_id=$2 AND state='Pending'", pendingExpiryDb);
            restorePending.Parameters.AddWithValue(pairing.ExpiresAtUtc);
            restorePending.Parameters.AddWithValue(Guid.ParseExact(pairing.PairingId, "D"));
            await restorePending.ExecuteNonQueryAsync();
        }
        using var approvalReply = await client.PostAsJsonAsync($"/v1/pairings/{pairing.PairingId}/approve", approval);
        if (approvalReply.StatusCode != HttpStatusCode.OK)
        {
            var safeError = "unclassified";
            try { using var error = JsonDocument.Parse(await approvalReply.Content.ReadAsStringAsync());
                if (error.RootElement.TryGetProperty("sqlState", out var state)) safeError = "sqlstate-" + state.GetString();
                else if (error.RootElement.TryGetProperty("error", out var code)) safeError = code.GetString() ?? safeError; }
            catch (JsonException) { }
            throw new InvalidOperationException($"Pairing approval returned HTTP {(int)approvalReply.StatusCode} ({safeError}).");
        }
        check(true, "Active owner signs CAS membership revision and wraps the existing content key for each device and recovery");
        var approvalResult = await approvalReply.Content.ReadFromJsonAsync<PairingApprovalResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Pairing approval response missing.");
        check(approvalResult.State == "Approved" && approvalResult.MembershipRevision == 2 && approvalResult.MembershipDigest == nextDigest,
            "Approved pairing commits the new membership revision without rotating the existing content-key epoch");
        await using (var advancedSessionDb = new NpgsqlConnection(connection))
        {
            await advancedSessionDb.OpenAsync();
            await using var query = new NpgsqlCommand("""
                SELECT membership_revision FROM focusguard_device.device_sessions
                WHERE session_hash=$1 AND account_id=$2 AND device_id=$3 AND revoked_at IS NULL
                """, advancedSessionDb);
            query.Parameters.AddWithValue(initialSessionHash);
            query.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            query.Parameters.AddWithValue(Guid.ParseExact(identity.Public.DeviceId, "D"));
            check((long?)(await query.ExecuteScalarAsync()) == 2,
                "Active approver's existing device session advances to the committed membership revision");
        }
        using var approvalReplay = await client.PostAsJsonAsync($"/v1/pairings/{pairing.PairingId}/approve", approval);
        check(approvalReplay.StatusCode == HttpStatusCode.NotFound, "Approved pairing cannot be approved again");

        // Approval already committed membership. Delay key receipt beyond the
        // pending-request deadline, as happens after a client/API version mismatch.
        await using (var expiryDb = new NpgsqlConnection(connection))
        {
            await expiryDb.OpenAsync();
            await using var expireApproved = new NpgsqlCommand(
                "UPDATE focusguard_device.pairings SET expires_at=NOW()-INTERVAL '1 second' WHERE pairing_id=$1 AND state='Approved'", expiryDb);
            expireApproved.Parameters.AddWithValue(Guid.ParseExact(pairing.PairingId, "D"));
            check(await expireApproved.ExecuteNonQueryAsync() == 1,
                "Synthetic approved pairing crosses its original pending approval deadline");
        }

        var readRequestId = Guid.NewGuid().ToString("D"); var readTime = DateTimeOffset.UtcNow;
        readTime = new DateTimeOffset(readTime.Ticks - readTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var readTarget = $"/v1/bootstrap/pairings/{pairing.PairingId}";
        var readProof = new DeviceRequestProof("GET", readTarget, accountId, generationId, pairingDevice.Public.DeviceId,
            readRequestId, readTime.ToUnixTimeMilliseconds(), DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
        using var readPairing = new HttpRequestMessage(HttpMethod.Get, readTarget);
        readPairing.Headers.Add("X-FG-Request-Id", readRequestId);
        readPairing.Headers.Add("X-FG-Timestamp", readTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        readPairing.Headers.Add("X-FG-Pending-Proof", pairingDevice.Sign("FG-PENDING-REQUEST-1", MembershipProtocol.CanonicalDeviceRequestProof(readProof)));
        using var pairingStateReply = await client.SendAsync(readPairing);
        var pairingState = await pairingStateReply.Content.ReadFromJsonAsync<PairingStateResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Pairing state response missing.");
        check(pairingStateReply.StatusCode == HttpStatusCode.OK && pairingState.State == "Approved" &&
            pairingState.Membership?.ProposedRevision == 2 && pairingState.Membership.KeyEpoch == 1 &&
            pairingState.KeyEnvelopes?.Length == 3 && pairingState.MembershipChain is { Length: 2 } chain &&
            chain[0].ProposedRevision == 1 && chain[1].ProposedRevision == 2 &&
            pairingState.CurrentMembership?.ProposedRevision == 2 &&
            pairingState.CurrentMembershipDigest == pairingState.MembershipDigest &&
            pairingState.CurrentKeyEnvelopes.Length == 3 && pairingState.CurrentMembershipChain.Length == 2,
            "Only requesting installation proof can fetch approved and current memberships, envelopes and verifiable history");
        var deviceEnvelope = pairingState.KeyEnvelopes!.Single(item => item.RecipientId == pairingDevice.Public.DeviceId);
        var returnedContentKey = SignedKeyEnvelopeCodec.OpenForDevice(deviceEnvelope,
            new KeyEnvelopeContext(accountId, generationId, 1, pairingDevice.Public.DeviceId,
                pairingState.MembershipDigest, identity.Public.DeviceId), identity.Public, pairingDevice);
        check(CryptographicOperations.FixedTimeEquals(SHA256.HashData(returnedContentKey), rekeyDigest),
            "Approving device envelope decrypts only with the requesting device private key");
        CryptographicOperations.ZeroMemory(rekeyDigest);

        var pairingSessionBytes = RandomNumberGenerator.GetBytes(32);
        string pairingDeviceSession;
        try { pairingDeviceSession = DeviceEncoding.Encode(pairingSessionBytes); }
        finally { CryptographicOperations.ZeroMemory(pairingSessionBytes); }
        var confirmBody = new ConfirmPairingRequest(pairing.PairingId, pairingState.MembershipDigest, pairingDeviceSession);
        var confirmRequestId = Guid.NewGuid().ToString("D"); var confirmTime = DateTimeOffset.UtcNow;
        confirmTime = new DateTimeOffset(confirmTime.Ticks - confirmTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var confirmTarget = $"/v1/bootstrap/pairings/{pairing.PairingId}/confirm";
        var confirmBodyBytes = MembershipProtocol.CanonicalConfirmPairingBody(confirmBody);
        var confirmProof = new DeviceRequestProof("POST", confirmTarget, accountId, generationId, pairingDevice.Public.DeviceId,
            confirmRequestId, confirmTime.ToUnixTimeMilliseconds(), DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(confirmBodyBytes)));
        CryptographicOperations.ZeroMemory(confirmBodyBytes);
        using var confirmMessage = new HttpRequestMessage(HttpMethod.Post, confirmTarget)
        {
            Content = JsonContent.Create(confirmBody, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        confirmMessage.Headers.Add("X-FG-Request-Id", confirmRequestId);
        confirmMessage.Headers.Add("X-FG-Timestamp", confirmTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        confirmMessage.Headers.Add("X-FG-Pending-Proof", pairingDevice.Sign("FG-PAIRING-CONFIRM-1", MembershipProtocol.CanonicalDeviceRequestProof(confirmProof)));
        using var confirmReply = await client.SendAsync(confirmMessage);
        var session = await confirmReply.Content.ReadFromJsonAsync<ConfirmPairingResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Device session response missing.");
        check(confirmReply.StatusCode == HttpStatusCode.OK && session.State == "Consumed" &&
            session.ExpiresAtUtc > DateTimeOffset.UtcNow,
            "New installation confirms key receipt once with a locally generated device session");
        var sessionHash = SHA256.HashData(DeviceEncoding.Decode(pairingDeviceSession, 32));
        await using (var sessionDb = new NpgsqlConnection(connection))
        {
            await sessionDb.OpenAsync();
            await using var storedSession = new NpgsqlCommand("SELECT count(*) FROM focusguard_device.device_sessions WHERE session_hash=$1 AND account_id=$2 AND device_id=$3", sessionDb);
            storedSession.Parameters.AddWithValue(sessionHash); storedSession.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            storedSession.Parameters.AddWithValue(Guid.ParseExact(pairingDevice.Public.DeviceId, "D"));
            check((long)(await storedSession.ExecuteScalarAsync())! == 1, "Database stores only the device-session digest");
        }
        CryptographicOperations.ZeroMemory(sessionHash);

        var entityId = "baf56f37-89a7-4a98-9a1e-e87bb3504a91";
        var documentContext = new ContentDocumentContext(accountId, generationId, entityId,
            "preferences.initial", 0, 1, 1, 2);
        var clearDocument = DeviceEncoding.Canonical(new
            { schemaVersion = 1, theme = "Light", language = "en", durationMinutes = 50 });
        var encryptedDocument = ContentDocumentCodec.Encrypt(clearDocument, returnedContentKey,
            documentContext, pairingDevice);
        var documentTarget = $"/v1/documents/{entityId}";
        var writeRequestId = Guid.NewGuid().ToString("D");
        var writeTime = DateTimeOffset.UtcNow;
        writeTime = new DateTimeOffset(writeTime.Ticks - writeTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var documentBodyBytes = ContentDocumentCodec.CanonicalDocumentBody(encryptedDocument);
        var writeProof = new DeviceRequestProof("POST", documentTarget, accountId, generationId,
            pairingDevice.Public.DeviceId, writeRequestId, writeTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(documentBodyBytes)));
        CryptographicOperations.ZeroMemory(documentBodyBytes);
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add("X-FG-Device-Id", pairingDevice.Public.DeviceId);
        client.DefaultRequestHeaders.Add("X-FG-Device-Session", pairingDeviceSession);
        client.DefaultRequestHeaders.Add("X-FG-Request-Id", writeRequestId);
        client.DefaultRequestHeaders.Add("X-FG-Timestamp", writeTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var writeProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(writeProof);
        try { client.DefaultRequestHeaders.Add("X-FG-Proof", pairingDevice.Sign("FG-REQUEST-1", writeProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(writeProofBytes); }
        using var documentWrite = await client.PostAsJsonAsync(documentTarget, encryptedDocument);
        var documentWriteBody = await documentWrite.Content.ReadAsStringAsync();
        if (documentWrite.StatusCode != HttpStatusCode.Created)
        {
            var safeCode = "unclassified";
            try
            {
                using var error = JsonDocument.Parse(documentWriteBody);
                if (error.RootElement.TryGetProperty("error", out var code)) safeCode = code.GetString() ?? safeCode;
                if (error.RootElement.TryGetProperty("diagnostic", out var diagnostic))
                    safeCode += ":" + (diagnostic.GetString() ?? "unknown");
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"Encrypted document write returned HTTP {(int)documentWrite.StatusCode} ({safeCode}).");
        }
        var writeResult = JsonSerializer.Deserialize<EncryptedDocumentWriteResult>(documentWriteBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Encrypted document write response missing.");
        check(documentWrite.StatusCode == HttpStatusCode.Created && writeResult.State == "Stored" &&
            writeResult.Revision == 1 && writeResult.EntityId == entityId && writeResult.Cursor == 1 &&
            documentWrite.Headers.ETag?.Tag == "\"1\"" &&
            documentWrite.Headers.TryGetValues("X-FG-Cursor", out var writeCursors) && writeCursors.Single() == "1",
            "Second active device stores only its signed encrypted document with CAS revision 1");
        await using (var storedDocumentConnection = new NpgsqlConnection(connection))
        {
            await storedDocumentConnection.OpenAsync();
            await using var storedDocumentQuery = new NpgsqlCommand(
                "SELECT document::text FROM focusguard_device.documents WHERE account_id=$1 AND entity_id=$2",
                storedDocumentConnection);
            storedDocumentQuery.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            storedDocumentQuery.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
            var storedDocumentJson = (string?)await storedDocumentQuery.ExecuteScalarAsync();
            var clearText = Encoding.UTF8.GetString(clearDocument);
            check(storedDocumentJson is not null && !storedDocumentJson.Contains(clearText, StringComparison.Ordinal),
                "PostgreSQL stores the selected preference document only as signed ciphertext");
        }

        var staleDocument = ContentDocumentCodec.Encrypt(clearDocument, returnedContentKey, documentContext, pairingDevice);
        var staleBody = ContentDocumentCodec.CanonicalDocumentBody(staleDocument);
        var staleRequestId = Guid.NewGuid().ToString("D");
        var staleTime = DateTimeOffset.UtcNow;
        staleTime = new DateTimeOffset(staleTime.Ticks - staleTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var staleProof = new DeviceRequestProof("POST", documentTarget, accountId, generationId,
            pairingDevice.Public.DeviceId, staleRequestId, staleTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(staleBody)));
        CryptographicOperations.ZeroMemory(staleBody);
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add("X-FG-Device-Id", pairingDevice.Public.DeviceId);
        client.DefaultRequestHeaders.Add("X-FG-Request-Id", staleRequestId);
        client.DefaultRequestHeaders.Add("X-FG-Timestamp", staleTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var staleProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(staleProof);
        try { client.DefaultRequestHeaders.Add("X-FG-Proof", pairingDevice.Sign("FG-REQUEST-1", staleProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(staleProofBytes); }
        using var staleWrite = await client.PostAsJsonAsync(documentTarget, staleDocument);
        check(staleWrite.StatusCode == HttpStatusCode.Conflict,
            "Document write with a stale base revision loses CAS without replacing ciphertext");

        var documentReadRequestId = Guid.NewGuid().ToString("D");
        var documentReadTime = DateTimeOffset.UtcNow;
        documentReadTime = new DateTimeOffset(documentReadTime.Ticks - documentReadTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var documentReadProof = new DeviceRequestProof("GET", documentTarget, accountId, generationId,
            pairingDevice.Public.DeviceId, documentReadRequestId, documentReadTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        using var documentRead = new HttpRequestMessage(HttpMethod.Get, documentTarget);
        documentRead.Headers.Add("X-FG-Device-Id", pairingDevice.Public.DeviceId);
        documentRead.Headers.Add("X-FG-Device-Session", pairingDeviceSession);
        documentRead.Headers.Add("X-FG-Request-Id", documentReadRequestId);
        documentRead.Headers.Add("X-FG-Timestamp", documentReadTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var readProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(documentReadProof);
        try { documentRead.Headers.Add("X-FG-Proof", pairingDevice.Sign("FG-REQUEST-1", readProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(readProofBytes); }
        using var documentReadReply = await client.SendAsync(documentRead);
        var documentReadBody = await documentReadReply.Content.ReadAsStringAsync();
        if (documentReadReply.StatusCode != HttpStatusCode.OK)
        {
            var safeCode = "unclassified";
            try
            {
                using var error = JsonDocument.Parse(documentReadBody);
                if (error.RootElement.TryGetProperty("error", out var code)) safeCode = code.GetString() ?? safeCode;
                if (error.RootElement.TryGetProperty("diagnostic", out var diagnostic))
                    safeCode += ":" + (diagnostic.GetString() ?? "unknown");
            }
            catch (JsonException) { }
            throw new InvalidOperationException($"Encrypted document read returned HTTP {(int)documentReadReply.StatusCode} ({safeCode}).");
        }
        var readResult = JsonSerializer.Deserialize<EncryptedDocumentResult>(documentReadBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Encrypted document read response missing.");
        if (readResult.Document is null) throw new InvalidOperationException("Encrypted document envelope was missing from the read response.");
        var openedDocument = ContentDocumentCodec.Decrypt(readResult.Document, documentContext,
            returnedContentKey, pairingDevice.Public);
        try
        {
            check(documentReadReply.StatusCode == HttpStatusCode.OK && readResult.Revision == 1 &&
                readResult.Cursor == 1 && documentReadReply.Headers.TryGetValues("X-FG-Cursor", out var readCursors) &&
                readCursors.Single() == "1" &&
                documentReadReply.Headers.ETag?.Tag == "\"1\"" &&
                CryptographicOperations.FixedTimeEquals(openedDocument, clearDocument),
                "Second approved device reads and decrypts its own CAS document through the PostgreSQL-backed API");

            foreignAccountClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", otherAccountAccessToken);
            var foreignRequestId = Guid.NewGuid().ToString("D");
            var foreignTime = DateTimeOffset.UtcNow;
            foreignTime = new DateTimeOffset(foreignTime.Ticks - foreignTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            var foreignProof = new DeviceRequestProof("GET", documentTarget, accountId, generationId,
                pairingDevice.Public.DeviceId, foreignRequestId, foreignTime.ToUnixTimeMilliseconds(),
                DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(otherAccountAccessToken))),
                DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            using var foreignRead = new HttpRequestMessage(HttpMethod.Get, documentTarget);
            foreignRead.Headers.Add("X-FG-Device-Id", pairingDevice.Public.DeviceId);
            foreignRead.Headers.Add("X-FG-Device-Session", pairingDeviceSession);
            foreignRead.Headers.Add("X-FG-Request-Id", foreignRequestId);
            foreignRead.Headers.Add("X-FG-Timestamp", foreignTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            var foreignProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(foreignProof);
            try { foreignRead.Headers.Add("X-FG-Proof", pairingDevice.Sign("FG-REQUEST-1", foreignProofBytes)); }
            finally { CryptographicOperations.ZeroMemory(foreignProofBytes); }
            using var foreignReply = await foreignAccountClient.SendAsync(foreignRead);
            using var foreignError = JsonDocument.Parse(await foreignReply.Content.ReadAsStringAsync());
            check(foreignReply.StatusCode == HttpStatusCode.NotFound &&
                foreignError.RootElement.GetProperty("error").GetString() == "DocumentUnavailable",
                "Another verified OIDC account cannot discover or read the encrypted document");

        }

        finally
        {
            CryptographicOperations.ZeroMemory(openedDocument);
            CryptographicOperations.ZeroMemory(returnedContentKey);
        }

        if (Environment.GetEnvironmentVariable("FG_PHASE3_HOST_IDENTITY_TEST") == "1")
            await VerifyHostProductionOptInAsync(factory, connection, issuer, subject, token,
                accountId, generationId, identity, recovery, pairingRootKey, [initial, nextMembership],
                nextDigest, initialDeviceSession, check);

        var revokedMembers = nextMembership.Members.Select(member => member.DeviceId == pairingDevice.Public.DeviceId
            ? member with { Status = "Revoked" } : member).ToArray();
        var revokeMembership = nextMembership with
        {
            BaseRevision = 2,
            ProposedRevision = 3,
            KeyEpoch = 2,
            PreviousDigest = nextDigest,
            Members = revokedMembers,
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        revokeMembership = revokeMembership with
        {
            Signature = identity.Sign("FG-MEMBERSHIP-1", MembershipProtocol.CanonicalMembership(revokeMembership))
        };
        var revokeDigest = MembershipProtocol.Digest(revokeMembership);
        var rotatedKey = RandomNumberGenerator.GetBytes(32);
        var rotatedKeyDigest = SHA256.HashData(rotatedKey);
        var revokeEnvelopes = new[]
        {
            SignedKeyEnvelopeCodec.SealForDevice(rotatedKey,
                new KeyEnvelopeContext(accountId, generationId, 2, identity.Public.DeviceId, revokeDigest, identity.Public.DeviceId),
                identity.Public.EncryptionPublicKey, identity),
            SignedKeyEnvelopeCodec.SealForDevice(rotatedKey,
                new KeyEnvelopeContext(accountId, generationId, 2, accountId, revokeDigest, identity.Public.DeviceId),
                recovery.Public.EncryptionPublicKey, identity)
        };
        CryptographicOperations.ZeroMemory(rotatedKey);
        var revokeTarget = $"/v1/memberships/devices/{pairingDevice.Public.DeviceId}/revoke";
        var revokeBody = new RevokeDeviceRequest(pairingDevice.Public.DeviceId, revokeMembership, revokeEnvelopes);
        var revokeBodyBytes = MembershipProtocol.CanonicalRevokeDeviceBody(revokeBody);
        var revokeRequestId = Guid.NewGuid().ToString("D");
        var revokeTime = DateTimeOffset.UtcNow;
        revokeTime = new DateTimeOffset(revokeTime.Ticks - revokeTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var revokeProof = new DeviceRequestProof("POST", revokeTarget, accountId, generationId,
            identity.Public.DeviceId, revokeRequestId, revokeTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(revokeBodyBytes)));
        CryptographicOperations.ZeroMemory(revokeBodyBytes);
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add("X-FG-Device-Id", identity.Public.DeviceId);
        client.DefaultRequestHeaders.Add("X-FG-Request-Id", revokeRequestId);
        client.DefaultRequestHeaders.Add("X-FG-Timestamp", revokeTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        client.DefaultRequestHeaders.Add("X-FG-Proof", identity.Sign("FG-REQUEST-1", MembershipProtocol.CanonicalDeviceRequestProof(revokeProof)));
        using var revokeReply = await client.PostAsJsonAsync(revokeTarget, revokeBody);
        var revoked = await revokeReply.Content.ReadFromJsonAsync<DeviceRevocationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Device revocation response missing.");
        check(revokeReply.StatusCode == HttpStatusCode.OK && revoked.State == "Revoked" &&
            revoked.DeviceId == pairingDevice.Public.DeviceId && revoked.MembershipRevision == 3 &&
            revoked.MembershipDigest == revokeDigest,
            "Active device signs revocation, advances membership epoch and retains only current-device/recovery envelopes");
        using (var revokedSession = await client.GetAsync("/v1/account"))
            check(revokedSession.IsSuccessStatusCode, "Revoking one device leaves the account and remaining device active");
        await using (var revokedDb = new NpgsqlConnection(connection))
        {
            await revokedDb.OpenAsync();
            await using var query = new NpgsqlCommand("""
                SELECT count(*) FROM focusguard_device.device_sessions
                WHERE account_id=$1 AND device_id=$2 AND revoked_at IS NOT NULL
                """, revokedDb);
            query.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            query.Parameters.AddWithValue(Guid.ParseExact(pairingDevice.Public.DeviceId, "D"));
            check((long)(await query.ExecuteScalarAsync())! == 1, "Revocation immediately invalidates the removed device's transport session");
            await using var remaining = new NpgsqlCommand("""
                SELECT membership_revision FROM focusguard_device.device_sessions
                WHERE session_hash=$1 AND account_id=$2 AND device_id=$3 AND revoked_at IS NULL
                """, revokedDb);
            remaining.Parameters.AddWithValue(initialSessionHash);
            remaining.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            remaining.Parameters.AddWithValue(Guid.ParseExact(identity.Public.DeviceId, "D"));
            check((long?)(await remaining.ExecuteScalarAsync()) == 3,
                "Remaining member's existing device session advances again after epoch rotation");
        }
        var historicalReadId = Guid.NewGuid().ToString("D");
        var historicalReadTime = DateTimeOffset.UtcNow;
        historicalReadTime = new DateTimeOffset(historicalReadTime.Ticks - historicalReadTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var historicalProof = new DeviceRequestProof("GET", documentTarget, accountId, generationId,
            identity.Public.DeviceId, historicalReadId, historicalReadTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
        using var historicalRead = new HttpRequestMessage(HttpMethod.Get, documentTarget);
        historicalRead.Headers.Add("X-FG-Device-Id", identity.Public.DeviceId);
        historicalRead.Headers.Add("X-FG-Device-Session", initialDeviceSession);
        historicalRead.Headers.Add("X-FG-Request-Id", historicalReadId);
        historicalRead.Headers.Add("X-FG-Timestamp", historicalReadTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var historicalProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(historicalProof);
        try { historicalRead.Headers.Add("X-FG-Proof", identity.Sign("FG-REQUEST-1", historicalProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(historicalProofBytes); }
        using var historicalReply = await client.SendAsync(historicalRead);
        var historicalResult = await historicalReply.Content.ReadFromJsonAsync<EncryptedDocumentResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Historical encrypted document response missing.");
        var historicalClear = ContentDocumentCodec.Decrypt(historicalResult.Document, documentContext,
            pairingRootKey, pairingDevice.Public);
        try
        {
            check(historicalReply.StatusCode == HttpStatusCode.OK && historicalResult.Document.MembershipRevision == 2 &&
                CryptographicOperations.FixedTimeEquals(historicalClear, clearDocument),
                "Remaining member reads history signed by a device active at its original membership revision");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(historicalClear);
            CryptographicOperations.ZeroMemory(clearDocument);
        }

        // The expiry regression adds an API request to this acceptance window.
        // Wait before producing a fresh proof for the revoked read and replays;
        // 429 must never be accepted as evidence of an authorization denial.
        await Task.Delay(TimeSpan.FromMinutes(1));
        var revokedReadId = Guid.NewGuid().ToString("D");
        var revokedReadTime = DateTimeOffset.UtcNow;
        revokedReadTime = new DateTimeOffset(revokedReadTime.Ticks - revokedReadTime.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var revokedReadProof = new DeviceRequestProof("GET", documentTarget, accountId, generationId,
            pairingDevice.Public.DeviceId, revokedReadId, revokedReadTime.ToUnixTimeMilliseconds(),
            DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
        using var revokedRead = new HttpRequestMessage(HttpMethod.Get, documentTarget);
        revokedRead.Headers.Add("X-FG-Device-Id", pairingDevice.Public.DeviceId);
        revokedRead.Headers.Add("X-FG-Device-Session", pairingDeviceSession);
        revokedRead.Headers.Add("X-FG-Request-Id", revokedReadId);
        revokedRead.Headers.Add("X-FG-Timestamp", revokedReadTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        var revokedProofBytes = MembershipProtocol.CanonicalDeviceRequestProof(revokedReadProof);
        try { revokedRead.Headers.Add("X-FG-Proof", pairingDevice.Sign("FG-REQUEST-1", revokedProofBytes)); }
        finally { CryptographicOperations.ZeroMemory(revokedProofBytes); }
        using var revokedReadReply = await client.SendAsync(revokedRead);
        check(revokedReadReply.StatusCode == HttpStatusCode.Forbidden,
            $"Revoked device cannot read its previous encrypted document from the cloud (HTTP {(int)revokedReadReply.StatusCode})");

        var ownerEnvelope = SignedKeyEnvelopeCodec.OpenForDevice(revokeEnvelopes[0],
            new KeyEnvelopeContext(accountId, generationId, 2, identity.Public.DeviceId, revokeDigest, identity.Public.DeviceId),
            identity.Public, identity);
        var recoveryEnvelope = SignedKeyEnvelopeCodec.OpenForRecovery(revokeEnvelopes[1],
            new KeyEnvelopeContext(accountId, generationId, 2, accountId, revokeDigest, identity.Public.DeviceId),
            identity.Public, recovery);
        try
        {
            check(CryptographicOperations.FixedTimeEquals(ownerEnvelope, recoveryEnvelope) &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(ownerEnvelope), rotatedKeyDigest),
                "New key epoch is recoverable only through remaining active device and recovery envelopes");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ownerEnvelope);
            CryptographicOperations.ZeroMemory(recoveryEnvelope);
        }
        CryptographicOperations.ZeroMemory(rotatedKeyDigest);
        CryptographicOperations.ZeroMemory(pairingRootKey);
        CryptographicOperations.ZeroMemory(initialSessionHash);
        using var revokeReplay = await client.PostAsJsonAsync(revokeTarget, revokeBody);
        check(revokeReplay.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Revoked device cannot be revoked or reactivated by replaying a prior signed mutation (HTTP {(int)revokeReplay.StatusCode})");
        using var pairingReplay = await client.PostAsJsonAsync("/v1/bootstrap/pairings", pairingRequest);
        check(pairingReplay.StatusCode == HttpStatusCode.BadRequest,
            $"Pairing challenge proof is consumed once (HTTP {(int)pairingReplay.StatusCode})");

        await CheckSessionRenewalAsync(client, accountId, generationId, identity, pairingDevice,
            revokeDigest, initialDeviceSession, token, otherAccountAccessToken, connection, check);

        var resetPasswordSeed = RandomNumberGenerator.GetBytes(32);
        string resetPassword;
        try { resetPassword = Convert.ToBase64String(resetPasswordSeed); }
        finally { CryptographicOperations.ZeroMemory(resetPasswordSeed); }
        await ResetSyntheticPasswordAsync(issuer, subject, resetPassword);
        check(true, "Synthetic OIDC password reset completed without changing the E2EE device keys");
        var resetPasswordBytes = Encoding.UTF8.GetBytes(resetPassword);
        var passwordAsContentKey = SHA256.HashData(resetPasswordBytes);
        CryptographicOperations.ZeroMemory(resetPasswordBytes);
        try
        {
            try
            {
                var incorrectlyOpened = ContentDocumentCodec.Decrypt(encryptedDocument, documentContext,
                    passwordAsContentKey, pairingDevice.Public);
                CryptographicOperations.ZeroMemory(incorrectlyOpened);
                check(false, "A reset OIDC password alone cannot decrypt the existing E2EE document");
            }
            catch (CryptographicException)
            {
                check(true, "A reset OIDC password alone cannot decrypt the existing E2EE document");
            }
        }
        finally { CryptographicOperations.ZeroMemory(passwordAsContentKey); }

        await using var cleanup = new NpgsqlConnection(connection);
        await cleanup.OpenAsync();
        await using var deletePairings = new NpgsqlCommand("DELETE FROM focusguard_device.pairings WHERE subject_hash=$1", cleanup);
        deletePairings.Parameters.AddWithValue(accountHash);
        await deletePairings.ExecuteNonQueryAsync();
        await using var deleteSessions = new NpgsqlCommand("DELETE FROM focusguard_device.device_sessions WHERE account_id=$1", cleanup);
        deleteSessions.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
        await deleteSessions.ExecuteNonQueryAsync();
        await using var deleteProofs = new NpgsqlCommand("DELETE FROM focusguard_device.request_proofs WHERE account_id=$1", cleanup);
        deleteProofs.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
        await deleteProofs.ExecuteNonQueryAsync();
        await using var deleteDocuments = new NpgsqlCommand("DELETE FROM focusguard_device.documents WHERE account_id=$1", cleanup);
        deleteDocuments.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
        await deleteDocuments.ExecuteNonQueryAsync();
        await using var deleteHistory = new NpgsqlCommand("DELETE FROM focusguard_device.membership_history WHERE account_id=$1", cleanup);
        deleteHistory.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
        await deleteHistory.ExecuteNonQueryAsync();
        await using var deleteAccounts = new NpgsqlCommand("DELETE FROM focusguard_device.accounts WHERE subject_hash=$1", cleanup);
        deleteAccounts.Parameters.AddWithValue(accountHash);
        await deleteAccounts.ExecuteNonQueryAsync();
        await using var deleteChallenges = new NpgsqlCommand("DELETE FROM focusguard_device.challenges WHERE subject_hash=$1", cleanup);
        deleteChallenges.Parameters.AddWithValue(accountHash);
        await deleteChallenges.ExecuteNonQueryAsync();
        await using var verifyCleanup = new NpgsqlCommand("""
            SELECT count(*) FROM focusguard_device.accounts WHERE subject_hash=$1
            """, cleanup);
        verifyCleanup.Parameters.AddWithValue(accountHash);
        check((long)(await verifyCleanup.ExecuteScalarAsync())! == 0,
            "Synthetic account membership is removed after API acceptance checks");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task ResetSyntheticPasswordAsync(string issuer, string userId, string newPassword)
    {
        var adminUsername = Environment.GetEnvironmentVariable("KC_BOOTSTRAP_ADMIN_USERNAME");
        var adminPassword = Environment.GetEnvironmentVariable("KC_BOOTSTRAP_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(adminUsername) || string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException("Synthetic Keycloak admin credentials were not supplied to the acceptance fixture.");
        var origin = new Uri(issuer).GetLeftPart(UriPartial.Authority);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var adminReply = await http.PostAsync(origin + "/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "admin-cli", ["grant_type"] = "password",
                ["username"] = adminUsername, ["password"] = adminPassword
            }));
        if (!adminReply.IsSuccessStatusCode)
            throw new InvalidOperationException("Synthetic Keycloak admin authentication failed during password-reset acceptance.");
        using var adminJson = JsonDocument.Parse(await adminReply.Content.ReadAsStringAsync());
        var adminToken = adminJson.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Synthetic Keycloak admin token was missing.");
        using var resetRequest = new HttpRequestMessage(HttpMethod.Put,
            origin + "/admin/realms/focusguard-local-test/users/" + Uri.EscapeDataString(userId) + "/reset-password");
        resetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        resetRequest.Content = JsonContent.Create(new { type = "password", value = newPassword, temporary = false });
        using var resetReply = await http.SendAsync(resetRequest);
        if (resetReply.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException("Synthetic Keycloak password reset failed during E2EE acceptance.");
    }

    private static async Task VerifyHostProductionOptInAsync(WebApplicationFactory<ApiEntryPoint> apiFactory,
        string connection, string issuer, string subject, string accessToken, string accountId,
        string generationId, DeviceIdentity device, RecoveryKit recovery, byte[] contentKey,
        MembershipDocument[] membershipChain, string membershipDigest, string deviceSession,
        Action<bool, string> check)
    {
        var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(accessToken);
        var tokenExpiry = new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc));
        check(tokenExpiry > DateTimeOffset.UtcNow.AddSeconds(45),
            "Host opt-in starts with a Keycloak bearer token outside IdentitySession's refresh window");

        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Keycloak-HostSync-" + Guid.NewGuid().ToString("N"));
        var deviceRoot = Path.Combine(root, "devices");
        var syncRoot = Path.Combine(root, "sync");
        var preferencesPath = Path.Combine(root, "prefs", "desktop-settings.json");
        var identityVaultPath = Path.Combine(root, "identity.dpapi");
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);

        var hostConfiguration = new IdentityConfiguration(
            new Uri("https://identity.example.invalid/realms/phase3-host-local"), "focusguard-native",
            new Uri("http://127.0.0.1:18444/callback/"), LocalTest: true,
            ApiOrigin: new Uri("https://api.example.invalid/"));
        // Keep the same PostgreSQL database but isolate this Host flow from the
        // per-subject in-memory limiter consumed by the preceding API checks.
        await using var hostApiFactory = apiFactory.WithWebHostBuilder(_ => { });
        var hostApiRequests = new ConcurrentQueue<string>();
        var handlerFactory = (Func<HttpMessageHandler>)(() =>
            new ObservedApiHandler(hostApiFactory.Server.CreateHandler(), hostApiRequests));
        var identityConstructor = typeof(IdentitySession).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, types: [typeof(IdentityConfiguration), typeof(TimeProvider), typeof(Func<HttpMessageHandler>)],
            modifiers: null) ?? throw new InvalidOperationException("Local identity test constructor was unavailable.");
        var identity = (IdentitySession)identityConstructor.Invoke(
            [hostConfiguration, TimeProvider.System, handlerFactory]);
        var tokenType = typeof(IdentitySession).GetNestedType("SessionTokens",
            System.Reflection.BindingFlags.NonPublic)!;
        var tokens = Activator.CreateInstance(tokenType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, binder: null,
            args: [accessToken, "synthetic-refresh-token", null, tokenExpiry, subject, "phase3-host-fixture"], culture: null)!;
        SetPrivateField(identity, "tokens", tokens);
        typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
            new AccountState(true, "Phase Test", "phase1@example.invalid", true));

        var context = new DeviceAccountContext(hostConfiguration.Authority.AbsoluteUri.TrimEnd('/'),
            identity.AccountBinding ?? throw new InvalidOperationException("Host test identity binding was missing."),
            accountId, generationId);
        var hostDeviceVault = new WindowsDeviceVault(Path.Combine(deviceRoot, context.AccountBinding + ".dpapi"));
        await hostDeviceVault.SaveAsync(device, recovery, context, contentKey, membershipChain[^1].ProposedRevision,
            membershipDigest, CancellationToken.None, deviceSession, keyEpoch: membershipChain[^1].KeyEpoch);

        var preferences = new DesktopSettingsStore(preferencesPath);
        preferences.SaveVerified(new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 50,
            ReduceMotion: true, CyclePhaseNotifications: false, EnforcementEnabled: true));

        try
        {
            using var accountService = new HostAccountService(identity, new UserTokenVault(identityVaultPath),
                hostConfiguration, deviceRoot, preferences, settingsSyncTestRoot: syncRoot);
            SetPrivateField(accountService, "deviceLinked", true);
            accountService.SetSettingsSyncConsentPresenter(_ => Task.FromResult(true));

            using var lifetime = new DocumentLifetime();
            var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(new NamedPipeFocusControlClient("phase3-keycloak-host-opt-in"),
                new VerifiedNativeHostAuthorization(), lifetime, settingsStore: preferences,
                accountService: accountService);
            var bridgeRequest = $"{{\"version\":1,\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{lease.Token}\",\"method\":\"account.settings.sync.enable\",\"params\":{{}}}}";
            var bridgeJson = await bridge.HandleAsync("https://focusguard.app/index.html", bridgeRequest,
                CancellationToken.None);
            if (bridgeJson is null) throw new InvalidOperationException("Host account bridge returned no opt-in response.");
            var accepted = JsonSerializer.Deserialize<BridgeAccountResponse>(bridgeJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Host account bridge response was invalid.");
            check(accepted.Error is null && accepted.Result.SettingsSyncBusy &&
                accepted.Result.SettingsSyncStatus == "Enabling",
                "Native bridge requests consent before composing the production Host sync session");

            var syncTask = (Task)typeof(HostAccountService).GetField("settingsSyncTask",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(accountService)!;
            await syncTask.WaitAsync(TimeSpan.FromSeconds(15));
            var completed = await accountService.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            check(completed.SettingsSyncEnabled && completed.SettingsSyncStatus == "Enabled",
                $"Production Host opt-in verifies OIDC membership and enables automatic settings sync (error={completed.Error ?? "none"}, status={completed.SettingsSyncStatus ?? "none"}, API=[{string.Join(";", hostApiRequests)}])");
            var session = (HostSettingsSyncSession)typeof(HostAccountService).GetField("settingsSyncSession",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(accountService)!;
            var outbox = await session.Outbox.LoadAsync()
                ?? throw new InvalidDataException("The Host's initial settings outbox was missing.");
            check(outbox.SyncConsentEnabled && outbox.Confirmed.Revision == 1 &&
                outbox.Confirmed.Values == new GlobalSettings(50, "light", "en") && !outbox.Pending,
                "Real Keycloak bearer and PostgreSQL API commit the Host's encrypted settings baseline");

            await using var database = new NpgsqlConnection(connection);
            await database.OpenAsync();
            await using var storedDocumentCommand = new NpgsqlCommand(
                "SELECT document::text FROM focusguard_device.documents WHERE account_id=$1 AND entity_id=$2", database);
            storedDocumentCommand.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            storedDocumentCommand.Parameters.AddWithValue(Guid.ParseExact(HostSettingsSyncSession.GlobalSettingsEntityId, "D"));
            var storedDocumentJson = (string?)await storedDocumentCommand.ExecuteScalarAsync();
            var storedDocument = storedDocumentJson is null ? null :
                JsonSerializer.Deserialize<EncryptedContentDocument>(storedDocumentJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            check(storedDocument is { ProposedRevision: 1 } &&
                !storedDocumentJson!.Contains("\"theme\"", StringComparison.Ordinal),
                "PostgreSQL stores the Host baseline as the global encrypted document, without plaintext settings");
            check(!File.Exists(identityVaultPath), "Sync opt-in preserves the separate OIDC token vault");
            var finalPreferences = preferences.Load();
            check(finalPreferences.ReduceMotion && !finalPreferences.CyclePhaseNotifications &&
                finalPreferences.EnforcementEnabled, "Host sync retains preferences excluded from the global allowlist");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private sealed class ObservedApiHandler(HttpMessageHandler inner, ConcurrentQueue<string> requests)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                requests.Enqueue($"{request.Method.Method} {request.RequestUri?.AbsolutePath} {(int)response.StatusCode}");
                return response;
            }
            catch (Exception exception)
            {
                requests.Enqueue($"{request.Method.Method} {request.RequestUri?.AbsolutePath} {exception.GetType().Name}");
                throw;
            }
        }
    }

    private static async Task CheckSessionRenewalAsync(HttpClient client, string accountId, string generationId,
        DeviceIdentity owner, DeviceIdentity revokedDevice, string digest, string previousSession,
        string token, string otherToken, string connectionString, Action<bool, string> check)
    {
        foreach (var header in new[] { "X-FG-Device-Id", "X-FG-Device-Session", "X-FG-Request-Id", "X-FG-Timestamp", "X-FG-Proof" })
            client.DefaultRequestHeaders.Remove(header);
        const string target = "/v1/bootstrap/sessions/renew";
        string FreshSession()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            try { return DeviceEncoding.Encode(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        async Task<HttpResponseMessage> Renew(RenewDeviceSessionRequest body, DeviceIdentity signer,
            string? id = null, string? bearer = null, RenewDeviceSessionRequest? signedBody = null, object? wireBody = null)
        {
            var time = DateTimeOffset.UtcNow;
            time = new DateTimeOffset(time.Ticks - time.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            var bodyBytes = MembershipProtocol.CanonicalRenewDeviceSessionBody(signedBody ?? body);
            var proof = new DeviceRequestProof("POST", target, body.AccountId, body.GenerationId, body.DeviceId,
                id ?? Guid.NewGuid().ToString("D"), time.ToUnixTimeMilliseconds(),
                DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(bearer ?? token))),
                DeviceEncoding.Encode(SHA256.HashData(bodyBytes)));
            CryptographicOperations.ZeroMemory(bodyBytes);
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            using var request = new HttpRequestMessage(HttpMethod.Post, target)
            { Content = JsonContent.Create(wireBody ?? body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? token);
            request.Headers.Add("X-FG-Request-Id", proof.RequestId);
            request.Headers.Add("X-FG-Timestamp", time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            try { request.Headers.Add("X-FG-Proof", signer.Sign("FG-DEVICE-SESSION-RENEW-1", proofBytes)); }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
            return await client.SendAsync(request);
        }
        async Task<HttpResponseMessage> ReadMembership(string session)
        {
            const string membershipTarget = "/v1/bootstrap/membership";
            var time = DateTimeOffset.UtcNow;
            time = new DateTimeOffset(time.Ticks - time.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            var proof = new DeviceRequestProof("GET", membershipTarget, accountId, generationId, owner.Public.DeviceId,
                Guid.NewGuid().ToString("D"), time.ToUnixTimeMilliseconds(),
                DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            using var request = new HttpRequestMessage(HttpMethod.Get, membershipTarget);
            request.Headers.Add("X-FG-Device-Id", owner.Public.DeviceId);
            request.Headers.Add("X-FG-Device-Session", session);
            request.Headers.Add("X-FG-Request-Id", proof.RequestId);
            request.Headers.Add("X-FG-Timestamp", time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try { request.Headers.Add("X-FG-Proof", owner.Sign("FG-REQUEST-1", proofBytes)); }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
            return await client.SendAsync(request);
        }
        var body = new RenewDeviceSessionRequest(accountId, generationId, owner.Public.DeviceId, digest, FreshSession());
        var firstId = Guid.NewGuid().ToString("D");
        using var renewed = await Renew(body, owner, firstId);
        check(renewed.StatusCode == HttpStatusCode.OK, "Existing active device renews its transport session with OIDC and key possession");
        var result = await renewed.Content.ReadFromJsonAsync<RenewDeviceSessionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Session renewal result missing.");
        check(result.Snapshot.MembershipDigest == digest && result.Snapshot.Membership.ProposedRevision == 3 &&
            result.ExpiresAtUtc > DateTimeOffset.UtcNow && result.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddHours(8).AddMinutes(1),
            "Session renewal preserves signed membership and has a bounded expiry");
        using (var repeated = await Renew(body, owner))
        {
            var repeat = await repeated.Content.ReadFromJsonAsync<RenewDeviceSessionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            check(repeated.StatusCode == HttpStatusCode.OK && repeat?.ExpiresAtUtc == result.ExpiresAtUtc,
                "Lost-response retry acknowledges the same active secret without extending it");
        }
        using (var replayed = await Renew(body, owner, firstId))
            check(replayed.StatusCode == HttpStatusCode.Forbidden, "Session renewal rejects a repeated request ID");
        using (var oldRead = await ReadMembership(previousSession))
            check(oldRead.StatusCode == HttpStatusCode.Forbidden, "Renewal invalidates the previous device transport secret");
        using (var newRead = await ReadMembership(body.DeviceSession))
            check(newRead.StatusCode == HttpStatusCode.OK, "Renewed session reads its verified current membership");
        using (var wrongKey = await Renew(body, revokedDevice))
            check(wrongKey.StatusCode == HttpStatusCode.Forbidden, "Bearer login alone or another device key cannot renew the owner's device");
        using (var stale = await Renew(body with { MembershipDigest = DeviceEncoding.Encode(new byte[32]) }, owner))
            check(stale.StatusCode == HttpStatusCode.Conflict, "Renewal rejects stale membership without granting a session");
        using (var revoked = await Renew(body with { DeviceId = revokedDevice.Public.DeviceId }, revokedDevice))
            check(revoked.StatusCode == HttpStatusCode.Forbidden, "Revoked device cannot restore its transport authorization");
        using (var foreign = await Renew(body, owner, bearer: otherToken))
            check(foreign.StatusCode == HttpStatusCode.NotFound, "Another verified account cannot renew this membership");
        using (var altered = await Renew(body with { DeviceSession = FreshSession() }, owner, signedBody: body))
            check(altered.StatusCode == HttpStatusCode.Forbidden, "Session proof binds the exact new transport secret");
        using (var extra = await Renew(body, owner, wireBody: new { body.AccountId, body.GenerationId, body.DeviceId,
            body.MembershipDigest, body.DeviceSession, unexpected = true }))
            check(extra.StatusCode == HttpStatusCode.BadRequest, $"Renewal rejects unknown body fields (HTTP {(int)extra.StatusCode})");

        var sessionHash = SHA256.HashData(DeviceEncoding.Decode(body.DeviceSession, 32));
        DateTime forcedExpiry;
        await using (var database = new NpgsqlConnection(connectionString))
        {
            await database.OpenAsync();
            await using var count = new NpgsqlCommand("""
                SELECT count(*) FROM focusguard_device.device_sessions
                WHERE account_id=$1 AND device_id=$2 AND revoked_at IS NULL AND octet_length(session_hash)=32
                """, database);
            count.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            count.Parameters.AddWithValue(Guid.ParseExact(owner.Public.DeviceId, "D"));
            check((long)(await count.ExecuteScalarAsync())! == 1, "Renewal stores only one active hash for this device");
            // The loopback API uses the host clock, while Neon can be a few seconds ahead.
            // Force a past timestamp in the API's clock instead of assuming both clocks agree to one second.
            await using var expire = new NpgsqlCommand("UPDATE focusguard_device.device_sessions SET expires_at=$2 WHERE session_hash=$1 RETURNING expires_at", database);
            expire.Parameters.AddWithValue(sessionHash);
            expire.Parameters.AddWithValue(DateTime.UtcNow.AddMinutes(-5));
            forcedExpiry = (DateTime)(await expire.ExecuteScalarAsync())!;
        }
        CryptographicOperations.ZeroMemory(sessionHash);
        using (var expired = await Renew(body, owner))
            check(expired.StatusCode == HttpStatusCode.Forbidden, $"An expired identical secret is never reactivated by retry (HTTP {(int)expired.StatusCode}, fixture clock gap {(forcedExpiry-DateTime.UtcNow).TotalSeconds:F1}s)");
        using (var fresh = await Renew(body with { DeviceSession = FreshSession() }, owner))
            check(fresh.StatusCode == HttpStatusCode.OK, "An active device restores access after expiry using a fresh signed secret");
    }

    private sealed class SensitiveContentKey(byte[] value) : IDisposable
    {
        public bool Matches(ReadOnlySpan<byte> expected) => CryptographicOperations.FixedTimeEquals(value, expected);
        public void Dispose() => CryptographicOperations.ZeroMemory(value);
    }

    public static async Task VerifyRevocationAsync(WebApplicationFactory<ApiEntryPoint> factory, string accessToken, Action<bool, string> check)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage? reply = null;
        for (var retry = 0; retry < 3; retry++)
        {
            reply = await client.GetAsync("/v1/account");
            if (reply.StatusCode != HttpStatusCode.TooManyRequests) break;
            reply.Dispose();
            reply = null;
            if (retry < 2) await Task.Delay(TimeSpan.FromSeconds(61));
        }
        if (reply is null) throw new InvalidOperationException("API stayed throttled after waiting for the account rate window.");
        using (reply)
            check(reply.StatusCode == HttpStatusCode.Unauthorized,
                $"API rejects still-signed JWT after real session logout (HTTP {(int)reply.StatusCode})");
        // A signed token that was revoked still passes JWT validation. Repeated
        // requests must be throttled before any further remote userinfo calls.
        var throttled = false;
        for (var attempt = 0; attempt < 31; attempt++)
        {
            using var repeated = await client.GetAsync("/v1/account");
            if (repeated.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = true;
                check(repeated.Headers.CacheControl?.NoStore == true, "API throttling response cannot be cached");
                break;
            }
            if (repeated.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Revoked token unexpectedly accepted during rate-limit probe.");
        }
        check(throttled, "API enforces per-subject rate limit with real signed tokens");
    }
}
