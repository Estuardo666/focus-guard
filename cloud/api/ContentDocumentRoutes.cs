using System.Net.Http.Headers;
using System.Text.Json;
using FocusGuard.Devices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FocusGuard.Cloud.Api;

public static class ContentDocumentRoutes
{
    public static void MapContentDocumentRoutes(this WebApplication app)
    {
        app.MapGet("/v1/sync/cursor", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, ContentDocumentLedger ledger) =>
        {
            var caller = await BootstrapRoutes.GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var cursor = await ledger.ReadAccountCursorAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value,
                    Header(context, "X-FG-Device-Id"), Header(context, "X-FG-Device-Session"),
                    Header(context, "X-FG-Request-Id"), timestamp,
                    Header(context, "X-FG-Proof"), context.RequestAborted);
                context.Response.Headers["X-FG-Cursor"] = cursor.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(new { cursor });
            }
            catch (StaleDeviceSessionException) { return Results.Conflict(new { error = "DeviceMembershipUpdateRequired" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "SyncUnavailable" }); }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "SyncUnavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapGet("/v1/documents/{entityId:guid}", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, ContentDocumentLedger ledger, string entityId) =>
        {
            var caller = await BootstrapRoutes.GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.ReadSnapshotAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value,
                    Header(context, "X-FG-Device-Id"), Header(context, "X-FG-Device-Session"),
                    Header(context, "X-FG-Request-Id"), timestamp,
                    Header(context, "X-FG-Proof"), entityId, context.RequestAborted);
                context.Response.Headers["X-FG-Cursor"] = result.Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (result.Document is null) return Results.NotFound(new { error = "DocumentUnavailable" });
                context.Response.Headers.ETag = $"\"{result.Revision}\"";
                return Results.Json(new EncryptedDocumentResult(result.Revision, result.Document) { Cursor = result.Cursor });
            }
            catch (DocumentKeyEpochConflictException) { return Results.Conflict(new { error = "DocumentRekeyRequired" }); }
            catch (StaleDeviceSessionException) { return Results.Conflict(new { error = "DeviceMembershipUpdateRequired" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "DocumentUnavailable" }); }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "DocumentUnavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");

        app.MapPost("/v1/documents/{entityId:guid}", async (HttpContext context, HttpClient identityHttp,
            IOptionsMonitor<JwtBearerOptions> options, ContentDocumentLedger ledger, string entityId,
            [Microsoft.AspNetCore.Mvc.FromBody] EncryptedContentDocument body) =>
        {
            var caller = await BootstrapRoutes.GetVerifiedCaller(context, identityHttp, options);
            if (caller is null) return Results.Unauthorized();
            if (!TryBearer(context, out var token) || !TryTimestamp(context, out var timestamp))
                return Results.BadRequest(new { error = "InvalidRequest" });
            try
            {
                var result = await ledger.WriteAsync(caller.Value.Issuer, caller.Value.Subject, token,
                    context.Request.Path.Value + context.Request.QueryString.Value,
                    Header(context, "X-FG-Device-Id"), Header(context, "X-FG-Device-Session"),
                    Header(context, "X-FG-Request-Id"), timestamp,
                    Header(context, "X-FG-Proof"), entityId, body, context.RequestAborted);
                context.Response.Headers.ETag = $"\"{result.Revision}\"";
                context.Response.Headers["X-FG-Cursor"] = result.Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(result, statusCode: StatusCodes.Status201Created);
            }
            catch (DocumentKeyEpochConflictException) { return Results.Conflict(new { error = "DocumentRekeyRequired" }); }
            catch (StaleDeviceSessionException) { return Results.Conflict(new { error = "DeviceMembershipUpdateRequired" }); }
            catch (MembershipConflictException) { return Results.Conflict(new { error = "DocumentRevisionConflict" }); }
            catch (InvalidContentDocumentException) { return Results.BadRequest(new { error = "InvalidEncryptedDocument" }); }
            catch (InvalidDeviceProofException) { return Results.Forbid(); }
            catch (InvalidBootstrapProofException) { return Results.NotFound(new { error = "DocumentUnavailable" }); }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            { return Results.BadRequest(new { error = "InvalidRequest" }); }
            catch (NpgsqlException) { return Results.Json(new { error = "DocumentUnavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(524_288))
          .RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");
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
}
