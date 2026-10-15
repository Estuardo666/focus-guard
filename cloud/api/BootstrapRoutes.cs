using System.Net.Http.Headers;
using System.Text.Json;
using FocusGuard.Devices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FocusGuard.Cloud.Api;

public sealed record RegisterFirstDeviceRequest(string ChallengeId, BootstrapChallengeProof Proof,
    string ProofSignature, string DeviceSession, MembershipDocument Membership, SignedKeyEnvelope[] KeyEnvelopes);
public sealed record BootstrapPairingRequest(BootstrapPairingProof Proof, string ProofSignature);
public sealed record BootstrapPairingResponse(string PairingId, string DeviceId, DateTimeOffset ExpiresAtUtc, string State);
public sealed record DeviceAccountContextResponse(bool DeviceLinked, string? AccountId, string? GenerationId, long? MembershipRevision);
public sealed record PairingApprovalResult(string PairingId, string State, long MembershipRevision, string MembershipDigest);
public sealed record DeviceRevocationResult(string State, string DeviceId, long MembershipRevision, string MembershipDigest);
public sealed record MembershipSnapshotResponse(MembershipDocument Membership, string MembershipDigest,
    SignedKeyEnvelope[] KeyEnvelopes)
{
    public MembershipDocument[] MembershipChain { get; init; } = [];
}
public sealed record ConfirmPairingResult(string PairingId, string State, DateTimeOffset ExpiresAtUtc);

public static class BootstrapRoutes
{
    public static void MapBootstrapRoutes(this WebApplication app)
    {
        app.MapPost("/v1/bootstrap/challenges", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger,
            BootstrapChallengeRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            try
            {
                var challenge = await ledger.CreateAsync(caller.Value.Issuer, caller.Value.Subject, body, context.RequestAborted);
                return Results.Json(challenge, statusCode: StatusCodes.Status201Created);
            }
            catch (BootstrapAlreadyExistsException) { return Results.Conflict(new { error = "BootstrapUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException exception)
            {
                return app.Environment.IsDevelopment()
                    ? Results.Json(new { error = "BootstrapUnavailable", diagnostic = exception is PostgresException postgres
                        ? "sqlstate-" + postgres.SqlState : exception.GetType().Name + ":" + exception.InnerException?.GetType().Name }, statusCode: 503)
                    : Results.Json(new { error = "BootstrapUnavailable" }, statusCode: 503);
            }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/bootstrap/accounts", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger,
            RegisterFirstDeviceRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            try
            {
                var accountId = await ledger.RegisterFirstDeviceAsync(caller.Value.Issuer, caller.Value.Subject,
                    body.ChallengeId, body.Proof, body.ProofSignature, body.DeviceSession, body.Membership,
                    body.KeyEnvelopes, context.RequestAborted);
                return Results.Json(new { accountId, revision = 1, generationId = body.Membership.GenerationId, deviceLinked = true },
                    statusCode: StatusCodes.Status201Created);
            }
            catch (BootstrapAlreadyExistsException) { return Results.Conflict(new { error = "BootstrapUnavailable" }); }
            catch (InvalidBootstrapProofException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "BootstrapUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapGet("/v1/bootstrap/account", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            return Results.Json(await ledger.GetDeviceAccountContextAsync(caller.Value.Issuer,
                caller.Value.Subject, context.RequestAborted));
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/bootstrap/sessions/renew", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger, RenewDeviceSessionRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                return Results.Json(await ledger.RenewDeviceSessionAsync(caller.Value.Issuer, caller.Value.Subject,
                    token, context.Request.Path.Value + context.Request.QueryString.Value,
                    Header(context, "X-FG-Request-Id"), timestamp, Header(context, "X-FG-Proof"), body, context.RequestAborted));
            }
            catch (StaleDeviceSessionException) { return Results.Conflict(new { error = "DeviceMembershipUpdateRequired" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "MembershipUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "DeviceSessionUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapGet("/v1/bootstrap/membership", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.GetCurrentMembershipAsync(caller.Value.Issuer, caller.Value.Subject,
                    token, context.Request.Path.Value + context.Request.QueryString.Value,
                    Header(context, "X-FG-Device-Id"), Header(context, "X-FG-Device-Session"),
                    Header(context, "X-FG-Request-Id"), timestamp, Header(context, "X-FG-Proof"),
                    context.RequestAborted);
                return Results.Json(result);
            }
            catch (StaleDeviceSessionException) { return Results.Conflict(new { error = "DeviceMembershipUpdateRequired" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "MembershipUnavailable" }); }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "MembershipUnavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/bootstrap/pairings", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger,
            BootstrapPairingRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            try
            {
                var pairing = await ledger.RequestPairingAsync(caller.Value.Issuer, caller.Value.Subject,
                    body.Proof, body.ProofSignature, context.RequestAborted);
                return Results.Json(pairing, statusCode: StatusCodes.Status201Created);
            }
            catch (InvalidBootstrapProofException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (BootstrapAlreadyExistsException) { return Results.Conflict(new { error = "BootstrapUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "BootstrapUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapGet("/v1/bootstrap/pairings/pending", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            return Results.Json(await ledger.ListPendingPairingsAsync(caller.Value.Issuer,
                caller.Value.Subject, context.RequestAborted));
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapGet("/v1/bootstrap/pairings/{pairingId:guid}", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger, string pairingId) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var pairing = await ledger.ReadOwnPairingAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value, Header(context, "X-FG-Request-Id"),
                    timestamp, Header(context, "X-FG-Pending-Proof"), pairingId, context.RequestAborted);
                return Results.Json(pairing);
            }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "PairingUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "PairingUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/bootstrap/pairings/{pairingId:guid}/confirm", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger, string pairingId,
            [Microsoft.AspNetCore.Mvc.FromBody] ConfirmPairingRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (body.PairingId != pairingId || !TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.ConfirmPairingAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value, Header(context, "X-FG-Request-Id"),
                    timestamp, Header(context, "X-FG-Pending-Proof"), body, context.RequestAborted);
                return Results.Json(result);
            }
            catch (MembershipConflictException) { return Results.Conflict(new { error = "PairingConflict" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "PairingUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "PairingUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/pairings/{pairingId:guid}/approve", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger, string pairingId,
            [Microsoft.AspNetCore.Mvc.FromBody] PairingApprovalRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (body.PairingId != pairingId || !TryBearer(context, out var token) ||
                !TryTimestamp(context, out var timestamp)) return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.ApprovePairingAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value, Header(context, "X-FG-Device-Id"),
                    Header(context, "X-FG-Request-Id"), timestamp, Header(context, "X-FG-Proof"), body,
                    context.RequestAborted);
                return Results.Json(result);
            }
            catch (MembershipConflictException) { return Results.Conflict(new { error = "MembershipConflict" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "PairingUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException ex)
            {
                return app.Environment.IsDevelopment()
                    ? Results.Json(new { error = "PairingUnavailable", sqlState = ex.SqlState }, statusCode: 503)
                    : Results.Json(new { error = "PairingUnavailable" }, statusCode: 503);
            }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/memberships/devices/{deviceId:guid}/revoke", async (HttpContext context,
            HttpClient identityHttp, IOptionsMonitor<JwtBearerOptions> options, BootstrapChallengeLedger ledger,
            string deviceId, [Microsoft.AspNetCore.Mvc.FromBody] RevokeDeviceRequest body) =>
        {
            var caller = await GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (body.DeviceId != deviceId || !TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.RevokeDeviceAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value, Header(context, "X-FG-Device-Id"),
                    Header(context, "X-FG-Request-Id"), timestamp, Header(context, "X-FG-Proof"), body,
                    context.RequestAborted);
                return Results.Json(result);
            }
            catch (MembershipConflictException) { return Results.Conflict(new { error = "MembershipConflict" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "MembershipUnavailable" }); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "MembershipUnavailable" }, statusCode: 503); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");
    }

    private static string Header(HttpContext context, string name) => context.Request.Headers[name].ToString();

    private static bool TryBearer(HttpContext context, out string token)
    {
        token = "";
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        token = value[7..].Trim();
        return token.Length is > 0 and <= 16384;
    }

    private static bool TryTimestamp(HttpContext context, out long timestamp)
    {
        timestamp = 0;
        var value = Header(context, "X-FG-Timestamp");
        return value.EndsWith('Z') && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            && (timestamp = parsed.ToUnixTimeMilliseconds()) > 0;
    }

    internal static async Task<(string Issuer, string Subject)?> GetVerifiedCaller(HttpContext context,
        HttpClient identityHttp, IOptionsMonitor<JwtBearerOptions> options)
    {
        var authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
        var identity = options.Get(JwtBearerDefaults.AuthenticationScheme).Authority!.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, identity + "/protocol/openid-connect/userinfo");
        request.Headers.Authorization = authorization;
        using var reply = await identityHttp.SendAsync(request, context.RequestAborted);
        if (!reply.IsSuccessStatusCode) return null;
        await reply.Content.LoadIntoBufferAsync(16384, context.RequestAborted);
        using var profile = JsonDocument.Parse(await reply.Content.ReadAsStringAsync(context.RequestAborted));
        var data = profile.RootElement;
        if (!data.TryGetProperty("sub", out var subject) || string.IsNullOrWhiteSpace(subject.GetString()) ||
            subject.GetString()!.Length > 255 || !data.TryGetProperty("email_verified", out var verified) ||
            verified.ValueKind != JsonValueKind.True) return null;
        return (identity, subject.GetString()!);
    }
}
