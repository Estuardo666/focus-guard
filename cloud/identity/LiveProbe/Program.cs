using FocusGuard.Identity;

// Owner-present HTTPS acceptance probe. No token, email, or password output.
if (!args.Contains("--owner-present"))
    throw new InvalidOperationException("Run only with the owner present: --owner-present.");
var config = new IdentityConfiguration(
    new Uri("https://auth.focusguard.totemmassmedia.com/realms/focusguard"),
    "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FocusGuard", "acceptance-" + Guid.NewGuid().ToString("N") + ".dpapi");
var vault = new UserTokenVault(path);
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
try
{
    using (var session = new IdentitySession(config, TimeProvider.System))
    {
        Check(await SystemBrowserLogin.RunAsync(session, config.RedirectUri,
            new ExternalSystemBrowser(), timeout.Token), "HTTPS native PKCE callback and signature accepted");
        Check(session.State.SignedIn && session.State.EmailVerified, "native account signed in with verified email");
        await session.SaveAsync(vault, timeout.Token);
        Check(vault.Exists, "native session persisted in owner DPAPI vault");
    }
    using var restored = new IdentitySession(config, TimeProvider.System);
    Check(await restored.RestoreAsync(vault, timeout.Token), "HTTPS session restored and refresh rotated");
    Check(restored.State.SignedIn && restored.State.EmailVerified, "restored account verified online");
    Check(await restored.LogoutAsync(timeout.Token), "native remote logout accepted by public Keycloak");
    Check(!restored.State.SignedIn && !vault.Exists, "native logout clears account and vault");
    Console.WriteLine($"LIVE_NATIVE_RESULT={checks} PASS; no credentials printed.");
}
finally { vault.Clear(); }
