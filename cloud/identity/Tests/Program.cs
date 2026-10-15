using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Duende.IdentityModel.OidcClient;
using FocusGuard.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

var authority = new Uri("http://127.0.0.1:18443/realms/focusguard-local-test");
var callback = new Uri("http://127.0.0.1:18444/callback/");
var configuration = new IdentityConfiguration(authority, "focusguard-native", callback, LocalTest: true);
if (Environment.GetEnvironmentVariable("FG_PHASE3_SYNC_ONLY") == "1")
{
    var syncConnection = Environment.GetEnvironmentVariable("FG_API_TEST_CONNECTION") ??
        throw new InvalidOperationException("A local PostgreSQL connection is required for the Phase 3 ledger test.");
    var syncChecks = 0;
    void SyncCheck(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine("PASS " + label);
        syncChecks++;
    }
    await ApiAutomaticSettingsSyncChecks.RunAsync(syncConnection, SyncCheck);
    Console.WriteLine($"RESULT {syncChecks} automatic-sync/PostgreSQL checks passed.");
    return;
}
var password = Environment.GetEnvironmentVariable("FG_TEST_PASSWORD") ?? throw new InvalidOperationException("Synthetic test account required.");
var count = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS " + label); count++;
}
foreach (var bad in new[]
{
    configuration with { LocalTest = false },
    configuration with { Authority = new Uri("http://identity.example.com" ) },
    configuration with { RedirectUri = new Uri("http://localhost:18444/callback/") },
    configuration with { RedirectUri = new Uri("http://127.0.0.1:18444/callback/?injected=1") }
})
{
    try { bad.Validate(); throw new InvalidOperationException("Invalid configuration accepted"); }
    catch (ArgumentException) { Check(true, "reject unsafe authority/callback"); }
}
var session = new IdentitySession(configuration, TimeProvider.System);
Check(!session.State.SignedIn, "starts local/signed out");
using (var policyHttp = new HttpClient())
{
    using var passwordGrant = await policyHttp.PostAsync(new Uri(authority + "/protocol/openid-connect/token"), new FormUrlEncodedContent(new Dictionary<string, string>
    { ["client_id"] = "focusguard-native", ["grant_type"] = "password", ["username"] = "phase1@example.invalid", ["password"] = password }));
    Check(passwordGrant.StatusCode == HttpStatusCode.BadRequest || passwordGrant.StatusCode == HttpStatusCode.Unauthorized, "identity rejects password grant");
    using var noPkce = await policyHttp.PostAsync(new Uri(authority + "/protocol/openid-connect/ext/par/request"), new FormUrlEncodedContent(new Dictionary<string, string>
    { ["client_id"] = "focusguard-native", ["response_type"] = "code", ["scope"] = "openid", ["redirect_uri"] = callback.AbsoluteUri, ["state"] = "public-test-state" }));
    Check(noPkce.StatusCode == HttpStatusCode.BadRequest, "identity enforces PKCE on public client");
}
var start = await session.BeginAsync(CancellationToken.None);
Check(start.Query.Contains("request_uri=", StringComparison.Ordinal) || start.Query.Contains("code_challenge_method=S256", StringComparison.Ordinal), "authorization uses PAR or explicit PKCE S256");
Check(!start.Query.Contains("client_secret", StringComparison.Ordinal), "public client has no secret");
try { await session.BeginAsync(CancellationToken.None); throw new InvalidOperationException("Concurrent login accepted"); }
catch (InvalidOperationException ex) when (ex.Message == "Login already pending.") { Check(true, "reject parallel login"); }
var response = await AuthenticateAsync(start);
Check(!await session.CompleteAsync(new Uri(response.AbsoluteUri.Replace("state=", "state=wrong", StringComparison.Ordinal)), CancellationToken.None), "reject altered state");
Check(!session.State.SignedIn, "failed callback stays signed out");
Check(!await session.CompleteAsync(response, CancellationToken.None), "callback state consumed after failure");
start = await session.BeginAsync(CancellationToken.None);
response = await AuthenticateAsync(start);
var parameters = response.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
using (var pkceHttp = new HttpClient())
{
    using var withoutVerifier = await pkceHttp.PostAsync(new Uri(authority + "/protocol/openid-connect/token"), new FormUrlEncodedContent(new Dictionary<string, string>
    { ["client_id"] = "focusguard-native", ["grant_type"] = "authorization_code", ["code"] = parameters["code"], ["redirect_uri"] = callback.AbsoluteUri }));
    Check(withoutVerifier.StatusCode == HttpStatusCode.BadRequest, "stolen code without PKCE verifier rejected");
}
await session.CancelAsync(CancellationToken.None);
start = await session.BeginAsync(CancellationToken.None);
response = await AuthenticateAsync(start);
Check(await session.CompleteAsync(response, CancellationToken.None), "real Keycloak code exchange and signed ID token");
Check(session.State is { SignedIn: true, Email: "phase1@example.invalid" }, "sanitized account state");
Check(!await session.CompleteAsync(response, CancellationToken.None), "reject callback replay");
var serialized = JsonSerializer.Serialize(session.State);
Check(!Regex.IsMatch(serialized, "token|secret|password", RegexOptions.IgnoreCase), "bridge state excludes secrets");
var directory = Path.Combine(Path.GetTempPath(), "FocusGuard-Identity-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var vaultPath = Path.Combine(directory, "tokens.bin");
var vault = new UserTokenVault(vaultPath);
try
{
    await session.SaveAsync(vault, CancellationToken.None);
    var plaintext = await vault.ReadAsync(CancellationToken.None);
    string refresh;
    string access;
    using (var doc = JsonDocument.Parse(plaintext))
    {
        refresh = doc.RootElement.GetProperty("RefreshToken").GetString()!;
        access = doc.RootElement.GetProperty("AccessToken").GetString()!;
        Check(new JsonWebToken(refresh).GetClaim("typ").Value == "Offline",
            "native login issues a persistent session independent of the eight-hour browser session");
    }
    using (var doc = JsonDocument.Parse(plaintext))
    {
        var idToken = doc.RootElement.GetProperty("IdentityToken").GetString()!;
        var parsed = new JsonWebToken(idToken); // fixture extracts nonce; identity is never accepted from this reader.
        using var discoveryHttp = new HttpClient();
        var jwks = await discoveryHttp.GetStringAsync(new Uri(authority + "/protocol/openid-connect/certs"));
        var options = new OidcClientOptions
        {
            Authority = authority.AbsoluteUri,
            ClientId = "focusguard-native",
            ProviderInformation = new ProviderInformation { KeySet = new Duende.IdentityModel.Jwk.JsonWebKeySet(jwks) }
        };
        var validator = new SignedIdentityTokenValidator { ExpectedNonce = parsed.GetClaim("nonce").Value };
        Check(!(await validator.ValidateAsync(idToken, options)).IsError, "JWT signature verified against real JWKS");
        options.ClientId = "another-client";
        Check((await validator.ValidateAsync(idToken, options)).IsError, "JWT wrong audience rejected");
        options.ClientId = "focusguard-native";
        options.Authority = "https://another-issuer.example.invalid";
        Check((await validator.ValidateAsync(idToken, options)).IsError, "JWT wrong issuer rejected");
        options.Authority = authority.AbsoluteUri;
        validator.ExpectedNonce = "wrong-nonce";
        Check((await validator.ValidateAsync(idToken, options)).IsError, "JWT wrong nonce rejected");
        validator.ExpectedNonce = parsed.GetClaim("nonce").Value;
        var segments = idToken.Split('.');
        var signature = Convert.FromBase64String(segments[2].Replace('-', '+').Replace('_', '/') + new string('=', (4 - segments[2].Length % 4) % 4));
        signature[0] ^= 1;
        segments[2] = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check((await validator.ValidateAsync(string.Join('.', segments), options)).IsError, "JWT altered signature rejected");
    }
    Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(vaultPath)).Contains(refresh, StringComparison.Ordinal), "DPAPI file contains no plaintext refresh token");
    CryptographicOperations.ZeroMemory(plaintext);

    // Simulate a new host process after the access token expires. The old refresh token must rotate,
    // the replacement must be written back under DPAPI, and the old token must stop working.
    session.Dispose();
    var persisted = JsonNode.Parse(await vault.ReadAsync(CancellationToken.None))!;
    persisted["AccessTokenExpiresUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
    await vault.WriteAsync(Encoding.UTF8.GetBytes(persisted.ToJsonString()), CancellationToken.None);
    using var restored = new IdentitySession(configuration, TimeProvider.System);
    var restoredSuccessfully = await restored.RestoreAsync(vault, CancellationToken.None);
    Check(restoredSuccessfully, "DPAPI session restores after host restart and verifies Keycloak userinfo (" + (restored.LastRestoreError ?? "ok") + ")");
    var refreshedBytes = await vault.ReadAsync(CancellationToken.None);
    string rotatedAccess, rotatedRefresh;
    using (var refreshed = JsonDocument.Parse(refreshedBytes))
    {
        rotatedAccess = refreshed.RootElement.GetProperty("AccessToken").GetString()!;
        rotatedRefresh = refreshed.RootElement.GetProperty("RefreshToken").GetString()!;
    }
    Check(rotatedAccess != access && rotatedRefresh != refresh, "expired access triggers one-use refresh-token rotation");
    Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(vaultPath)).Contains(rotatedRefresh, StringComparison.Ordinal), "rotated refresh token remains encrypted at rest");
    CryptographicOperations.ZeroMemory(refreshedBytes);
    using (var oldRefresh = await httpPost(authority, "focusguard-native", refresh))
        Check(oldRefresh.StatusCode == HttpStatusCode.BadRequest, "previous refresh token rejected after rotation");

    var bytes = await File.ReadAllBytesAsync(vaultPath); bytes[^1] ^= 1;
    await File.WriteAllBytesAsync(vaultPath, bytes);
    try { await vault.ReadAsync(CancellationToken.None); throw new InvalidOperationException("Tamper accepted"); }
    catch (CryptographicException) { Check(true, "DPAPI rejects tampering"); }
    Check(await restored.LogoutAsync(CancellationToken.None), "real server logout succeeds");
    Check(!File.Exists(vaultPath), "logout deletes persisted vault automatically");
    Check(!restored.State.SignedIn, "logout clears native state");
    using var revoked = await httpPost(authority, "focusguard-native", rotatedRefresh);
    Check(revoked.StatusCode == HttpStatusCode.BadRequest, "refresh token rejected after logout");
    vault.Clear(); Check(!File.Exists(vaultPath), "vault deleted on disconnect");
}
finally { vault.Clear(); Directory.Delete(directory); }
var cancelled = new IdentitySession(configuration, TimeProvider.System);
await cancelled.BeginAsync(CancellationToken.None);
await cancelled.CancelAsync(CancellationToken.None);
Check(!await cancelled.CompleteAsync(response, CancellationToken.None), "cancelled login cannot complete");
Check(!cancelled.State.SignedIn, "cancel preserves local mode");
var fakeTime = new AdvancingTime();
var expired = new IdentitySession(configuration, fakeTime);
start = await expired.BeginAsync(CancellationToken.None);
response = await AuthenticateAsync(start);
fakeTime.Advance(TimeSpan.FromMinutes(6));
Check(!await expired.CompleteAsync(response, CancellationToken.None), "monotonic login expiry enforced");
var networkSession = new IdentitySession(configuration, TimeProvider.System);
var transportBrowser = new FixtureBrowser(async authorization =>
{
    var nativeCallback = await AuthenticateAsync(authorization);
    using var http = new HttpClient();
    using var unknownRoute = await http.GetAsync("http://127.0.0.1:18444/unexpected");
    Check(unknownRoute.StatusCode == HttpStatusCode.NotFound, "callback listener exposes no other route");
    using var delivered = await http.GetAsync(nativeCallback);
    Check(delivered.IsSuccessStatusCode, "native loopback receives actual authorization callback");
    Check(delivered.Headers.CacheControl?.NoStore == true, "callback response cannot be cached");
    Check(!((await delivered.Content.ReadAsStringAsync()).Contains("code=", StringComparison.Ordinal)), "callback body excludes authorization code");
});
Check(await SystemBrowserLogin.RunAsync(networkSession, callback, transportBrowser, CancellationToken.None), "unprivileged native callback transport completes login");
Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<ApiEntryPoint>? apiFixture = null;
string? networkAccessToken = null;
var productConnection = Environment.GetEnvironmentVariable("FG_API_TEST_CONNECTION");
if (!string.IsNullOrWhiteSpace(productConnection))
{
    var networkVault = new UserTokenVault(Path.Combine(Path.GetTempPath(), "FocusGuard-Api-Test-" + Guid.NewGuid().ToString("N") + ".bin"));
    try
    {
        await networkSession.SaveAsync(networkVault, CancellationToken.None);
        var privateBytes = await networkVault.ReadAsync(CancellationToken.None);
        try
        {
            using var privateJson = JsonDocument.Parse(privateBytes);
            networkAccessToken = privateJson.RootElement.GetProperty("AccessToken").GetString()!;
            var networkIdToken = privateJson.RootElement.GetProperty("IdentityToken").GetString()!;
            using var otherAccountSession = new IdentitySession(configuration, TimeProvider.System);
            var otherAccountAuthorization = await otherAccountSession.BeginAsync(CancellationToken.None);
            var otherAccountCallback = await AuthenticateAsAsync(otherAccountAuthorization, "phase1-other@example.invalid");
            Check(await otherAccountSession.CompleteAsync(otherAccountCallback, CancellationToken.None),
                "second verified account signs in independently for tenant isolation checks");
            var otherTokenVault = new UserTokenVault(Path.Combine(Path.GetTempPath(),
                "FocusGuard-Api-Other-" + Guid.NewGuid().ToString("N") + ".bin"));
            string otherAccountAccessToken;
            try
            {
                await otherAccountSession.SaveAsync(otherTokenVault, CancellationToken.None);
                var otherTokenBytes = await otherTokenVault.ReadAsync(CancellationToken.None);
                try
                {
                    using var otherTokenJson = JsonDocument.Parse(otherTokenBytes);
                    otherAccountAccessToken = otherTokenJson.RootElement.GetProperty("AccessToken").GetString()!;
                }
                finally { CryptographicOperations.ZeroMemory(otherTokenBytes); }
            }
            finally { otherTokenVault.Clear(); }
            apiFixture = await ApiAcceptanceChecks.RunAsync(authority.AbsoluteUri, networkAccessToken, networkIdToken,
                otherAccountAccessToken, productConnection, Check);
        }
        finally { CryptographicOperations.ZeroMemory(privateBytes); }
    }
    finally { networkVault.Clear(); }
}
Check(await networkSession.LogoutAsync(CancellationToken.None), "transport session revoked");
if (apiFixture is not null)
{
    try { await ApiAcceptanceChecks.VerifyRevocationAsync(apiFixture, networkAccessToken!, Check); }
    finally { await apiFixture.DisposeAsync(); }
}
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
{
    var neverCompletes = new IdentitySession(configuration, TimeProvider.System);
    try
    {
        await SystemBrowserLogin.RunAsync(neverCompletes, callback, new FixtureBrowser(_ => Task.CompletedTask), timeout.Token);
        throw new InvalidOperationException("Cancelled browser accepted.");
    }
    catch (OperationCanceledException) { Check(!neverCompletes.State.SignedIn, "browser cancellation keeps local mode"); }
}
using (var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 18444))
{
    socket.Start(); Check(true, "native callback socket released after cancellation"); socket.Stop();
}
Console.WriteLine($"RESULT {count} checks passed against real Keycloak, PostgreSQL-backed API and Windows DPAPI.");

async Task<Uri> AuthenticateAsync(Uri url)
    => await AuthenticateAsAsync(url, "phase1@example.invalid");

async Task<Uri> AuthenticateAsAsync(Uri url, string username)
{
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    var page = await http.GetAsync(url);
    for (var redirects = 0; page.StatusCode == HttpStatusCode.Found && redirects < 5; redirects++)
    {
        var location = page.Headers.Location ?? throw new InvalidOperationException("Missing identity redirect.");
        var next = new Uri(url, location);
        if (next.Host != "127.0.0.1" || next.Port != 18443)
            throw new InvalidOperationException("Identity authorization rejected: " + next.AbsolutePath);
        // Test HTTP agent only: HttpClient differs from browsers for secure cookies
        // on potentially trustworthy loopback. This never changes native OIDC transport.
        foreach (Cookie cookie in handler.CookieContainer.GetAllCookies()) cookie.Secure = false;
        page.Dispose(); url = next; page = await http.GetAsync(url);
    }
    using var pageLifetime = page;
    page.EnsureSuccessStatusCode();
    var html = await page.Content.ReadAsStringAsync();
    var action = Regex.Match(html, "<form[^>]+id=\"kc-form-login\"[^>]+action=\"([^\"]+)\"");
    if (!action.Success) throw new InvalidOperationException("Keycloak login form missing.");
    var endpoint = new Uri(WebUtility.HtmlDecode(action.Groups[1].Value));
    if (endpoint.Host != "127.0.0.1" || endpoint.Port != 18443) throw new InvalidOperationException("Unexpected identity form origin.");
    foreach (Cookie cookie in handler.CookieContainer.GetAllCookies()) cookie.Secure = false;
    using var login = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
    { ["username"] = username, ["password"] = password, ["credentialId"] = "" }));
    if (login.StatusCode != HttpStatusCode.Found || login.Headers.Location is null)
        throw new InvalidOperationException("Synthetic login failed, status " + (int)login.StatusCode);
    return login.Headers.Location;
}

static async Task<HttpResponseMessage> httpPost(Uri authority, string clientId, string refreshToken)
{
    using var http = new HttpClient();
    return await http.PostAsync(new Uri(authority + "/protocol/openid-connect/token"),
        new FormUrlEncodedContent(new Dictionary<string, string>
        { ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }));
}

sealed class AdvancingTime : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public void Advance(TimeSpan duration) => ticks += duration.Ticks;
}

// Network-driven test agent; not evidence that an installed browser was exercised.
sealed class FixtureBrowser(Func<Uri, Task> launch) : IAuthenticationBrowser
{
    public Task LaunchAsync(Uri authorization, CancellationToken cancellationToken) => launch(authorization);
}
