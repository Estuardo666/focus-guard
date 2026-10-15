using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
using System.Reflection;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostAccountServiceRetryTests
{
    [Theory]
    [InlineData("DevicePairingRequired")]
    [InlineData("DeviceMembershipUpdateRequired")]
    [InlineData("DeviceVaultMismatch")]
    [InlineData("RecoveryKitNotConfirmed")]
    [InlineData("AccountSettingsConflict")]
    [InlineData("DeviceConnectionUnavailable")]
    [InlineData("DeviceSessionInvalid")]
    public async Task Device_and_settings_outcomes_do_not_restart_a_valid_session(string deviceOutcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-account-outcome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tokenPath = Path.Combine(root, "identity.dpapi");
        await File.WriteAllBytesAsync(tokenPath, [1]);
        var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/"),
            "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"),
            ApiOrigin: new Uri("https://api.example.invalid/"));
        var identity = new IdentitySession(configuration, TimeProvider.System);
        typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
            new AccountState(true, "Test account", "test@example.invalid", true));
        var attempts = 0;
        using var service = new HostAccountService(identity, new UserTokenVault(tokenPath), configuration,
            Path.Combine(root, "devices"), restoreSessionAsync: (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(true);
            });

        try
        {
            _ = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            await BackgroundTask(service, "restoreTask").WaitAsync(TimeSpan.FromSeconds(3));
            await BackgroundTask(service, "deviceCheckTask").WaitAsync(TimeSpan.FromSeconds(3));

            // A device check or consented operation has a result while OIDC remains valid.
            SetField(service, "error", deviceOutcome);
            SetField(service, "accountHasLinkedDevice", true);
            SetField(service, "nextDeviceCheckUtc", DateTimeOffset.UtcNow.AddMinutes(2));
            var snapshot = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);

            Assert.False(snapshot.CheckingSession);
            Assert.False(snapshot.CheckingDevice);
            Assert.True(snapshot.SignedIn);
            Assert.True(snapshot.AccountHasLinkedDevice);
            Assert.False(snapshot.DeviceLinked);
            Assert.Equal(deviceOutcome, snapshot.Error);
            Assert.Equal(1, Volatile.Read(ref attempts));
            for (var poll = 0; poll < 10; poll++)
            {
                var subsequent = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
                Assert.False(subsequent.CheckingSession);
                Assert.False(subsequent.CheckingDevice);
                Assert.Equal(deviceOutcome, subsequent.Error);
            }
            Assert.Equal(1, Volatile.Read(ref attempts));
        }
        finally
        {
            service.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scheduled_session_retry_survives_an_unrelated_outcome_clearing_the_display_error()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-account-retry-reason-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tokenPath = Path.Combine(root, "identity.dpapi");
        await File.WriteAllBytesAsync(tokenPath, [1]);
        var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/"),
            "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"));
        var identity = new IdentitySession(configuration, TimeProvider.System);
        var attempts = 0;
        using var service = new HostAccountService(identity, new UserTokenVault(tokenPath), configuration,
            Path.Combine(root, "devices"), restoreSessionAsync: (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(false);
            });
        try
        {
            _ = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            await BackgroundTask(service, "restoreTask").WaitAsync(TimeSpan.FromSeconds(3));
            SetField(service, "error", null);
            SetField(service, "retryRestoreAfterUtc", DateTimeOffset.UtcNow.AddSeconds(-1));
            var retry = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            Assert.True(retry.CheckingSession);
            await BackgroundTask(service, "restoreTask").WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(2, Volatile.Read(ref attempts));
        }
        finally
        {
            service.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancelled_session_revalidation_reports_failure_and_respects_backoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-account-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tokenPath = Path.Combine(root, "identity.dpapi");
        await File.WriteAllBytesAsync(tokenPath, [1]);

        var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/"),
            "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"),
            ApiOrigin: new Uri("https://api.example.invalid/"));
        var identity = new IdentitySession(configuration, TimeProvider.System);
        typeof(IdentitySession).GetProperty(nameof(IdentitySession.State))!.SetValue(identity,
            new AccountState(true, "Test account", "test@example.invalid", true));

        var attempts = 0;
        using var service = new HostAccountService(identity, new UserTokenVault(tokenPath), configuration,
            Path.Combine(root, "devices"), restoreSessionAsync: (_, cancellationToken) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromException<bool>(new OperationCanceledException(cancellationToken));
            });

        var first = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
        Assert.True(first.CheckingSession);

        HostAccountSnapshot? settled = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            settled = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
            if (!settled.CheckingSession && settled.Error is not null) break;
        }

        Assert.NotNull(settled);
        Assert.False(settled.CheckingSession);
        Assert.Equal("AccountConnectionUnavailable", settled.Error);

        var duringBackoff = await service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
        Assert.False(duringBackoff.CheckingSession);
        Assert.Equal(1, Volatile.Read(ref attempts));

        service.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private static Task BackgroundTask(HostAccountService service, string name) =>
        (Task)typeof(HostAccountService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

    private static void SetField(HostAccountService service, string name, object? value) =>
        typeof(HostAccountService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, value);
}
