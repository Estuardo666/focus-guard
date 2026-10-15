using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text.Json.Serialization;
using FocusGuard.Devices;
using FocusGuard.Cloud.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
var issuer = builder.Configuration["Identity:Authority"] ?? throw new InvalidOperationException("Identity authority required.");
if (!Uri.TryCreate(issuer, UriKind.Absolute, out var authority) || authority.Scheme != "https" ||
    authority.UserInfo.Length != 0 || authority.Query.Length != 0 || authority.Fragment.Length != 0)
    throw new InvalidOperationException("Exact HTTPS identity authority required.");
builder.Logging.ClearProviders(); // No request URLs, credentials or claim values in platform logs.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(15) });
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = issuer.TrimEnd('/');
    options.Audience = "focusguard-api";
    options.MapInboundClaims = false;
    options.RequireHttpsMetadata = true;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer.TrimEnd('/'),
        ValidateAudience = true,
        ValidAudience = "focusguard-api",
        ValidateLifetime = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("VerifiedAccount", policy => policy.RequireAuthenticatedUser()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("sub") ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var connectionString = builder.Configuration.GetConnectionString("Product");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var dbOptions = new NpgsqlConnectionStringBuilder(connectionString)
    { MaxPoolSize = 4, MinPoolSize = 0, Timeout = 15, CommandTimeout = 10 };
    // Permit plaintext PostgreSQL only for an explicit loopback Development fixture.
    // All other hosts/configurations retain full TLS validation.
    var allowLoopbackDevelopmentDatabase = builder.Environment.IsDevelopment() &&
        dbOptions.SslMode == SslMode.Disable && IPAddress.TryParse(dbOptions.Host, out var databaseAddress) &&
        IPAddress.IsLoopback(databaseAddress);
    dbOptions.SslMode = allowLoopbackDevelopmentDatabase ? SslMode.Disable : SslMode.VerifyFull;
    builder.Services.AddSingleton(NpgsqlDataSource.Create(dbOptions.ConnectionString));
    builder.Services.AddSingleton(sp => new BootstrapChallengeLedger(sp.GetRequiredService<NpgsqlDataSource>()));
    builder.Services.AddSingleton(sp => new ContentDocumentLedger(sp.GetRequiredService<NpgsqlDataSource>()));
}
var app = builder.Build();
if (app.Services.GetService<NpgsqlDataSource>() is { } startupDatabase)
{
    await using var migration = startupDatabase.CreateCommand(BootstrapChallengeLedger.Schema + ContentDocumentLedger.Schema);
    await migration.ExecuteNonQueryAsync();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next(context);
});
app.UseExceptionHandler(error => error.Run(async context =>
{
    context.Response.StatusCode = 503;
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (app.Environment.IsDevelopment())
        await context.Response.WriteAsJsonAsync(new { error = "ServiceUnavailable", diagnostic = exception?.GetType().Name ?? "unknown" }, context.RequestAborted);
    else
        await context.Response.WriteAsJsonAsync(new { error = "ServiceUnavailable" }, context.RequestAborted);
}));
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Json(new { status = "alive" }));
app.MapGet("/health/ready", async (HttpContext context) =>
{
    var db = context.RequestServices.GetService<NpgsqlDataSource>();
    if (db is null) return Results.Json(new { status = "unavailable" }, statusCode: 503);
    try
    {
        await using var command = db.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(context.RequestAborted);
        return Results.Json(new { status = "ready" });
    }
    catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
    { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});
// Identity state only. Account login is NOT device membership or authority to send commands.
app.MapGet("/v1/account", async (HttpContext context, HttpClient identityHttp, IOptionsMonitor<JwtBearerOptions> options,
    BootstrapChallengeLedger deviceLedger) =>
{
    // JWT proves issuer/audience; an online userinfo check additionally rejects
    // a revoked identity session before returning account details. Rate limit
    // runs before this outbound call. This never grants device membership.
    var authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
    var identity = options.Get(JwtBearerDefaults.AuthenticationScheme).Authority!.TrimEnd('/');
    using var request = new HttpRequestMessage(HttpMethod.Get, identity + "/protocol/openid-connect/userinfo");
    request.Headers.Authorization = authorization;
    using var reply = await identityHttp.SendAsync(request, context.RequestAborted);
    if (!reply.IsSuccessStatusCode) return Results.Json(new { error = "IdentitySessionRejected", status = (int)reply.StatusCode }, statusCode: StatusCodes.Status401Unauthorized);
    await reply.Content.LoadIntoBufferAsync(16384, context.RequestAborted);
    using var profile = JsonDocument.Parse(await reply.Content.ReadAsStringAsync(context.RequestAborted));
    var data = profile.RootElement;
    // The same bearer token was already signature/audience/issuer-validated by
    // JwtBearer, then accepted by UserInfo. Preserve the provider's opaque sub
    // without depending on framework claim-mapping aliases.
    if (!data.TryGetProperty("sub", out var subject) || string.IsNullOrWhiteSpace(subject.GetString()) || subject.GetString()!.Length > 255)
        return Results.Unauthorized();
    if (!data.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True)
        return Results.Forbid();
    var deviceLinked = await deviceLedger.IsDeviceLinkedAsync(identity, subject.GetString()!, context.RequestAborted);
    return Results.Json(new
    {
        signedIn = true,
        displayName = data.TryGetProperty("name", out var name) ? name.GetString() : null,
        email = data.TryGetProperty("email", out var email) ? email.GetString() : null,
        emailVerified = true,
        deviceLinked
    });
}).RequireAuthorization("VerifiedAccount").RequireRateLimiting("account");
app.MapBootstrapRoutes();
app.MapContentDocumentRoutes();
await app.RunAsync();

public sealed class ApiEntryPoint;
