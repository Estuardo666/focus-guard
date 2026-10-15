using System.Security.Claims;
using System.Text.Json;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FocusGuard.Identity;

// Cryptographic validation delegates to Microsoft's maintained JWT library.
// This adapter enforces the issuer/client/nonce chosen by our native flow.
public sealed class SignedIdentityTokenValidator : IIdentityTokenValidator
{
    public string? ExpectedNonce { get; set; }
    public bool RequireNonce { get; set; } = true;
    public async Task<IdentityTokenValidationResult> ValidateAsync(string identityToken, OidcClientOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identityToken.Length > 16384 || RequireNonce && ExpectedNonce is null || options.ProviderInformation.KeySet is null)
            return new() { Error = "invalid_identity_token" };
        var keyset = new JsonWebKeySet(JsonSerializer.Serialize(options.ProviderInformation.KeySet));
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var validated = await handler.ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidIssuer = options.Authority.TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = options.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keyset.GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        });
        if (!validated.IsValid) return new() { Error = "invalid_identity_token" };
        var jwt = (JsonWebToken)validated.SecurityToken;
        var claims = validated.ClaimsIdentity;
        var azp = claims.FindFirst("azp")?.Value;
        var tokenNonce = claims.FindFirst("nonce")?.Value;
        if (RequireNonce && tokenNonce != ExpectedNonce || ExpectedNonce is not null && tokenNonce is not null && tokenNonce != ExpectedNonce ||
            string.IsNullOrWhiteSpace(claims.FindFirst("sub")?.Value) ||
            jwt.Audiences.Count() > 1 && azp != options.ClientId || azp is not null && azp != options.ClientId)
            return new() { Error = "invalid_identity_token" };
        return new() { User = new ClaimsPrincipal(claims), SignatureAlgorithm = jwt.Alg };
    }
}
