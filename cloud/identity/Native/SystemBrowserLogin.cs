using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Identity;

public interface IAuthenticationBrowser
{
    Task LaunchAsync(Uri authorization, CancellationToken cancellationToken);
}

public sealed class ExternalSystemBrowser : IAuthenticationBrowser
{
    public Task LaunchAsync(Uri authorization, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(authorization.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public static class SystemBrowserLogin
{
    private static readonly System.Resources.ResourceManager Strings = new("FocusGuard.Identity.IdentityStrings", typeof(SystemBrowserLogin).Assembly);
    public static async Task<bool> RunAsync(IdentitySession session, Uri redirectUri,
        IAuthenticationBrowser browser, CancellationToken cancellationToken)
    {
        // User-space IPv4 socket: no HTTP.sys URL ACL/elevation. No tokens in WebView.
        if (redirectUri.Scheme != "http" || redirectUri.Host != "127.0.0.1" || redirectUri.AbsolutePath != "/callback/")
            throw new ArgumentException("Exact loopback callback required.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, redirectUri.Port);
            options.Limits.MaxRequestLineSize = 8192;
            options.Limits.MaxRequestBodySize = 0;
            options.AddServerHeader = false;
        });
        await using var app = builder.Build();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapGet("/callback/", async (HttpContext context) =>
        {
            var accepted = false;
            if (context.Request.Host.Value == redirectUri.Authority && context.Request.QueryString.Value?.Length <= 8000)
            {
                try { accepted = await session.CompleteAsync(new Uri(redirectUri + context.Request.QueryString.Value), timeout.Token); }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException) { }
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.StatusCode = accepted ? 200 : 400;
            await context.Response.WriteAsync(Strings.GetString(accepted ? "CallbackAccepted" : "CallbackRejected")!, timeout.Token);
            completed.TrySetResult(accepted);
        });
        await app.StartAsync(timeout.Token);
        try
        {
            var authorization = await session.BeginAsync(timeout.Token);
            await browser.LaunchAsync(authorization, timeout.Token);
            return await completed.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await session.CancelAsync(CancellationToken.None);
        }
    }
}
