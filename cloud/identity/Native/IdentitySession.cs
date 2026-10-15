using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;

namespace FocusGuard.Identity;

// Only this record may cross the future native/React bridge. Tokens stay native.
public sealed record AccountState(bool SignedIn, string? DisplayName, string? Email, bool EmailVerified);

public sealed record IdentityConfiguration(Uri Authority, string ClientId, Uri RedirectUri, bool LocalTest = false, Uri? ApiOrigin = null)
{
    public void Validate()
    {
        if (Authority.Query.Length != 0 || Authority.Fragment.Length != 0 || Authority.UserInfo.Length != 0 ||
            Authority.Scheme != "https" && !(LocalTest && Authority.Scheme == "http" && Authority.Host == "127.0.0.1"))
            throw new ArgumentException("Identity authority must use HTTPS.");
        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > 100 ||
            RedirectUri.Scheme != "http" || RedirectUri.Host != "127.0.0.1" || RedirectUri.Port == 80 ||
            RedirectUri.AbsolutePath != "/callback/" || RedirectUri.Query.Length != 0 ||
            RedirectUri.Fragment.Length != 0 || RedirectUri.UserInfo.Length != 0)
            throw new ArgumentException("Native callback must be an exact IPv4 loopback URI.");
        if (ApiOrigin is not null && (ApiOrigin.Scheme != "https" || ApiOrigin.UserInfo.Length != 0 ||
            ApiOrigin.AbsolutePath != "/" || ApiOrigin.Query.Length != 0 || ApiOrigin.Fragment.Length != 0))
            throw new ArgumentException("Cloud API origin must be an exact HTTPS origin.");
    }
}

public sealed class IdentitySession(IdentityConfiguration configuration, TimeProvider timeProvider) : IDisposable
{
    private readonly Func<HttpMessageHandler> apiHandlerFactory = static () => new HttpClientHandler { AllowAutoRedirect = false };

    internal IdentitySession(IdentityConfiguration configuration, TimeProvider timeProvider,
        Func<HttpMessageHandler> apiHandlerFactory) : this(configuration, timeProvider)
    {
        if (!configuration.LocalTest)
            throw new InvalidOperationException("A custom API handler is restricted to local tests.");
        this.apiHandlerFactory = apiHandlerFactory ?? throw new ArgumentNullException(nameof(apiHandlerFactory));
    }

    private sealed record SessionTokens(string AccessToken, string RefreshToken, string? IdentityToken,
        DateTimeOffset AccessTokenExpiresUtc, string Subject, string Nonce);
    private sealed record StoredSession(int Version, string Issuer, string ClientId, string AccessToken,
        string RefreshToken, string? IdentityToken, DateTimeOffset AccessTokenExpiresUtc, string Subject, string Nonce);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly OidcClient client = CreateClient(configuration);
    private AuthorizeState? pending;
    private string? pendingNonce;
    private long pendingStarted;
    private SessionTokens? tokens;
    private UserTokenVault? savedVault;
    private bool disposed;
    public AccountState State { get; private set; } = new(false, null, null, false);
    public string? LastRestoreError { get; private set; }
    public string? AccountBinding => tokens is { } current
        ? Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.Authority.AbsoluteUri.TrimEnd('/') + "\0" + current.Subject)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_')
        : null;
    /// <summary>A request-proof digest for native API callers; the bearer token itself remains private.</summary>
    public string? AccessTokenDigest => tokens is { } current
        ? Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(current.AccessToken)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_')
        : null;

    private static OidcClient CreateClient(IdentityConfiguration config)
    {
        config.Validate();
        return new(new OidcClientOptions
        {
            Authority = config.Authority.AbsoluteUri.TrimEnd('/'),
            ClientId = config.ClientId,
            RedirectUri = config.RedirectUri.AbsoluteUri,
            Scope = "openid profile email offline_access",
            Policy = new Policy { Discovery = new DiscoveryPolicy { RequireHttps = !config.LocalTest } },
            IdentityTokenValidator = new SignedIdentityTokenValidator(),
            LoadProfile = true
        });
    }

    public async Task<Uri> BeginAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pending is not null) throw new InvalidOperationException("Login already pending.");
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
            validator.RequireNonce = true;
            validator.ExpectedNonce = nonce;
            pendingNonce = nonce;
            try { pending = await client.PrepareLoginAsync(new Parameters { { "nonce", nonce } }, cancellationToken: cancellationToken); }
            catch
            {
                pending = null; pendingNonce = null; validator.ExpectedNonce = null;
                throw;
            }
            if (pending.IsError)
            {
                pending = null; pendingNonce = null; validator.ExpectedNonce = null;
                throw new InvalidOperationException("Identity provider unavailable.");
            }
            pendingStarted = timeProvider.GetTimestamp();
            return new Uri(pending.StartUrl);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> CompleteAsync(Uri callback, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
            var state = pending;
            var expectedNonce = pendingNonce;
            pending = null; // Single use even for failure; a fresh login is required.
            pendingNonce = null;
            try
            {
                if (state is null || expectedNonce is null || callback.GetLeftPart(UriPartial.Path) != configuration.RedirectUri.AbsoluteUri ||
                    callback.Fragment.Length != 0 || callback.AbsoluteUri.Length > 8192 ||
                    timeProvider.GetElapsedTime(pendingStarted) > TimeSpan.FromMinutes(5)) return false;
                var result = await client.ProcessResponseAsync(callback.AbsoluteUri, state, cancellationToken: cancellationToken);
                if (result.IsError || string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken)
                    || string.IsNullOrWhiteSpace(result.User.FindFirst("sub")?.Value)) return false;
                tokens = new(result.AccessToken, result.RefreshToken, result.IdentityToken,
                    result.AccessTokenExpiration.ToUniversalTime(), result.User.FindFirst("sub")!.Value, expectedNonce);
                State = Project(result.User.Claims);
                return true;
            }
            finally { validator.ExpectedNonce = null; validator.RequireNonce = true; }
        }
        finally { gate.Release(); }
    }

    public async Task CancelAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending = null; pendingNonce = null;
            var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
            validator.ExpectedNonce = null; validator.RequireNonce = true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var current = tokens;
            try { savedVault?.Clear(); }
            finally
            {
                savedVault = null; tokens = null; pending = null; pendingNonce = null; State = new(false, null, null, false);
                var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
                validator.ExpectedNonce = null; validator.RequireNonce = true;
            }
            if (current is null) return false;
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            // Browser logout does not revoke offline sessions. Explicitly revoke this device's token.
            var endpoint = new Uri(configuration.Authority.AbsoluteUri.TrimEnd('/') + "/protocol/openid-connect/revoke");
            using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId,
                ["token"] = current.RefreshToken,
                ["token_type_hint"] = "refresh_token"
            }), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(UserTokenVault vault, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (tokens is null) throw new InvalidOperationException("Not signed in.");
            await SaveLockedAsync(vault, tokens, cancellationToken);
        }
        finally { gate.Release(); }
    }

    /// <summary>Sends directly to the configured API; the bearer token never leaves this native identity module.</summary>
    public async Task<HttpResponseMessage> SendApiRequestAsync(Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        return await SendApiRequestCoreAsync(_ => requestFactory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds a proof-bound native API request using the digest of the exact token attached to it.</summary>
    public async Task<HttpResponseMessage> SendApiRequestWithTokenDigestAsync(
        Func<string, HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        return await SendApiRequestCoreAsync(tokenDigest => requestFactory(tokenDigest), cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendApiRequestCoreAsync(Func<string, HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var apiOrigin = configuration.ApiOrigin ?? throw new InvalidOperationException("Cloud API is not configured.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            UserTokenVault? vault = null;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var current = tokens;
                if (current is null) throw new InvalidOperationException("Sign in is required.");
                if (current.AccessTokenExpiresUtc <= timeProvider.GetUtcNow().AddSeconds(30))
                {
                    vault = savedVault;
                    if (vault is null) throw new InvalidOperationException("A renewable native session is required.");
                }
                else
                {
                    using var request = requestFactory(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(current.AccessToken)))
                        .TrimEnd('=').Replace('+', '-').Replace('/', '_'));
                    var target = request.RequestUri;
                    if (target is null || target.Scheme != "https" || target.UserInfo.Length != 0 ||
                        target.GetLeftPart(UriPartial.Authority) != apiOrigin.GetLeftPart(UriPartial.Authority) ||
                        !target.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal) || target.Fragment.Length != 0 ||
                        request.Headers.Authorization is not null)
                        throw new InvalidOperationException("Native API destination rejected.");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", current.AccessToken);
                    using var handler = apiHandlerFactory();
                    using var clientHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
                    return await clientHttp.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                }
            }
            finally { gate.Release(); }
            if (vault is null || !await RestoreAsync(vault, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Native identity session could not be refreshed.");
        }
        throw new InvalidOperationException("Native access token refresh loop rejected.");
    }

    /// <summary>Restores only this user's DPAPI vault and revalidates the live identity session before exposing account state.</summary>
    public async Task<bool> RestoreAsync(UserTokenVault vault, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            LastRestoreError = null;
            savedVault = vault;
            if (tokens is null) State = new(false, null, null, false);
            byte[] plaintext;
            try { plaintext = await vault.ReadAsync(cancellationToken); }
            catch (Exception exception) when (exception is InvalidDataException or CryptographicException or JsonException)
            {
                vault.Clear();
                tokens = null; State = new(false, null, null, false);
                LastRestoreError = "StoredSessionInvalid";
                return false;
            }

            StoredSession? stored;
            try { stored = JsonSerializer.Deserialize<StoredSession>(plaintext); }
            catch (JsonException) { stored = null; }
            finally { CryptographicOperations.ZeroMemory(plaintext); }

            if (stored is null || !IsValid(stored))
            {
                vault.Clear();
                tokens = null; State = new(false, null, null, false);
                LastRestoreError = "StoredSessionInvalid";
                return false;
            }

            var active = new SessionTokens(stored.AccessToken, stored.RefreshToken, stored.IdentityToken,
                stored.AccessTokenExpiresUtc.ToUniversalTime(), stored.Subject, stored.Nonce);
            if (active.AccessTokenExpiresUtc <= timeProvider.GetUtcNow().ToUniversalTime().AddSeconds(30))
            {
                var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
                validator.ExpectedNonce = active.Nonce;
                validator.RequireNonce = false;
                Duende.IdentityModel.OidcClient.Results.RefreshTokenResult refreshed;
                try { refreshed = await client.RefreshTokenAsync(active.RefreshToken, cancellationToken: cancellationToken).ConfigureAwait(false); }
                finally { validator.ExpectedNonce = null; validator.RequireNonce = true; }
                if (refreshed.IsError)
                {
                    if (string.Equals(refreshed.Error, "invalid_grant", StringComparison.Ordinal))
                    {
                        vault.Clear();
                        LastRestoreError = "SessionExpired";
                    }
                    else
                    {
                        var errorCode = SafeOAuthErrorCode(refreshed.Error);
                        LastRestoreError = errorCode switch
                        {
                            "invalid_client" => "RefreshClientRejected",
                            "unauthorized_client" => "RefreshNotAllowed",
                            "invalid_request" => "RefreshRequestRejected",
                            "unsupported_grant_type" => "RefreshGrantUnsupported",
                            "invalid_scope" => "RefreshScopeRejected",
                            "server_error" or "temporarily_unavailable" => "IdentityUnavailable",
                            null => "RefreshRejectedNoCode",
                            _ => "RefreshRejected:" + errorCode
                        };
                    }
                    tokens = null; State = new(false, null, null, false);
                    return false;
                }
                // Keycloak is configured for refresh-token rotation; never fall back to reusing an old token.
                if (string.IsNullOrWhiteSpace(refreshed.AccessToken) || string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    || refreshed.AccessTokenExpiration <= timeProvider.GetUtcNow().ToUniversalTime())
                {
                    vault.Clear();
                    tokens = null; State = new(false, null, null, false);
                    LastRestoreError = "RefreshResponseInvalid";
                    return false;
                }
                active = new(refreshed.AccessToken, refreshed.RefreshToken,
                    string.IsNullOrWhiteSpace(refreshed.IdentityToken) ? active.IdentityToken : refreshed.IdentityToken,
                    refreshed.AccessTokenExpiration.ToUniversalTime(), active.Subject, active.Nonce);
                await SaveLockedAsync(vault, active, cancellationToken).ConfigureAwait(false);
            }

            var userInfo = await client.GetUserInfoAsync(active.AccessToken, cancellationToken).ConfigureAwait(false);
            if (userInfo.IsError)
            {
                tokens = null; State = new(false, null, null, false);
                LastRestoreError = "UserInfoRejected";
                return false; // Preserve the encrypted vault for a later network retry.
            }
            var subject = userInfo.Claims.FirstOrDefault(claim => claim.Type == "sub")?.Value;
            if (!string.Equals(subject, active.Subject, StringComparison.Ordinal))
            {
                vault.Clear();
                tokens = null; State = new(false, null, null, false);
                LastRestoreError = "IdentityRejected";
                return false;
            }

            tokens = active;
            savedVault = vault;
            State = Project(userInfo.Claims);
            return State.SignedIn;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            // Account connectivity is optional. Keep the DPAPI vault so a later request can retry.
            tokens = null;
            State = new(false, null, null, false);
            LastRestoreError = "IdentityNetworkUnavailable";
            return false;
        }
        catch (TimeoutException)
        {
            tokens = null;
            State = new(false, null, null, false);
            LastRestoreError = "IdentityTimeout";
            return false;
        }
        catch (InvalidOperationException)
        {
            tokens = null;
            State = new(false, null, null, false);
            LastRestoreError = "IdentityClientUnavailable";
            return false;
        }
        finally { gate.Release(); }
    }

    private async Task SaveLockedAsync(UserTokenVault vault, SessionTokens current, CancellationToken cancellationToken)
    {
        var stored = new StoredSession(1, configuration.Authority.AbsoluteUri.TrimEnd('/'), configuration.ClientId,
            current.AccessToken, current.RefreshToken, current.IdentityToken, current.AccessTokenExpiresUtc.ToUniversalTime(), current.Subject, current.Nonce);
        await vault.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(stored), cancellationToken).ConfigureAwait(false);
        savedVault = vault;
    }

    private bool IsValid(StoredSession stored) =>
        stored.Version == 1
        && !string.IsNullOrWhiteSpace(stored.Issuer)
        && string.Equals(stored.Issuer.TrimEnd('/'), configuration.Authority.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)
        && string.Equals(stored.ClientId, configuration.ClientId, StringComparison.Ordinal)
        && IsBoundedToken(stored.AccessToken)
        && IsBoundedToken(stored.RefreshToken)
        && (stored.IdentityToken is null || IsBoundedToken(stored.IdentityToken))
        && stored.AccessTokenExpiresUtc.Offset == TimeSpan.Zero
        && !string.IsNullOrWhiteSpace(stored.Nonce) && stored.Nonce.Length <= 128
        && !string.IsNullOrWhiteSpace(stored.Subject) && stored.Subject.Length <= 255;

    private static bool IsBoundedToken(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 32768;

    private static string? SafeOAuthErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Split([' ', ':'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return code is { Length: <= 40 } && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? code : null;
    }

    private static AccountState Project(IEnumerable<System.Security.Claims.Claim> claims)
    {
        static string? SafeClaim(IEnumerable<System.Security.Claims.Claim> values, string name, int maxLength)
        {
            var value = values.FirstOrDefault(claim => claim.Type == name)?.Value;
            if (string.IsNullOrWhiteSpace(value)) return null;
            var safe = new string(value.Where(character => !char.IsControl(character)).Take(maxLength).ToArray()).Trim();
            return safe.Length == 0 ? null : safe;
        }

        return new(true, SafeClaim(claims, "name", 120), SafeClaim(claims, "email", 254),
            string.Equals(SafeClaim(claims, "email_verified", 5), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Releases in-memory tokens when the signed-in user's host process closes; the DPAPI vault is retained.</summary>
    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            tokens = null;
            pending = null;
            pendingNonce = null;
            savedVault = null;
            State = new(false, null, null, false);
            var validator = (SignedIdentityTokenValidator)client.Options.IdentityTokenValidator;
            validator.ExpectedNonce = null;
            validator.RequireNonce = true;
            disposed = true;
        }
        finally
        {
            gate.Release();
        }
    }
}

// A Windows-only primitive. Plaintext token bytes exist only transiently in the caller's memory.
public sealed class UserTokenVault(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FocusGuard.Identity.TokenVault.v1");
    public bool Exists => File.Exists(path);
    public async Task WriteAsync(byte[] plaintext, CancellationToken cancellationToken)
    {
        if (plaintext.Length > 32768) throw new InvalidDataException("Token vault payload too large.");
        byte[] ciphertext;
        try { ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllBytesAsync(temporary, ciphertext, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 65536) throw new InvalidDataException("Invalid token vault.");
        return ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken), Entropy, DataProtectionScope.CurrentUser);
    }
    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
