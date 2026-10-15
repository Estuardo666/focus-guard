using FocusGuard.Sync;
using System.Security.Cryptography;
using Xunit;

public sealed class SettingsRetryControllerTests
{
    [Fact]
    public async Task RetryAfterUsesMonotonicTimeAndWallClockJumpCannotAccelerateIt()
    {
        var time = new Clock(); var calls = 0;
        var controller = new SettingsRetryController(_ =>
        {
            calls++; throw new SettingsTransportException(SettingsTransportFailure.Throttled, TimeSpan.FromMinutes(10));
        }, () => true, time, () => 0.5);
        Assert.Equal(TimeSpan.FromMinutes(10), (await controller.RunIfDueAsync()).Wait);
        time.WallClock = time.WallClock.AddYears(1);
        Assert.Equal(SettingsRetryStatus.Waiting, (await controller.RunIfDueAsync()).Status); Assert.Equal(1, calls);
        time.Advance(TimeSpan.FromMinutes(10));
        await controller.RunIfDueAsync(); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TransientBackoffGrowsAndCapsWithoutCallingDuringCooldown()
    {
        var time = new Clock(); var calls = 0;
        var controller = new SettingsRetryController(_ => { calls++; throw new SettingsTransportException(SettingsTransportFailure.Unavailable); },
            () => true, time, () => 0.5);
        foreach (var expected in new[] { 5, 10, 20, 40, 80, 160, 300, 300 })
        {
            var result = await controller.RunIfDueAsync(); Assert.Equal(TimeSpan.FromSeconds(expected), result.Wait);
            var before = calls; await controller.RunIfDueAsync(); Assert.Equal(before, calls);
            time.Advance(result.Wait!.Value);
        }
    }

    [Theory]
    [InlineData(SettingsTransportFailure.Authentication)]
    [InlineData(SettingsTransportFailure.Permission)]
    [InlineData(SettingsTransportFailure.MembershipChanged)]
    [InlineData(SettingsTransportFailure.RekeyRequired)]
    [InlineData(SettingsTransportFailure.AmbiguousMissing)]
    [InlineData(SettingsTransportFailure.Protocol)]
    public async Task CriticalFailureStaysStoppedUntilDeliberateRecovery(SettingsTransportFailure failure)
    {
        var time = new Clock(); var calls = 0;
        var controller = new SettingsRetryController(_ => { calls++; throw new SettingsTransportException(failure); }, () => true, time);
        Assert.Equal(SettingsRetryStatus.NeedsAttention, (await controller.RunIfDueAsync()).Status);
        time.Advance(TimeSpan.FromDays(1)); await controller.RunIfDueAsync(); Assert.Equal(1, calls);
        await controller.ResumeAfterRecoveryAsync(); await controller.RunIfDueAsync(); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DisabledConsentDoesNotRunTickEvenWhenRetryIsDue()
    {
        var time = new Clock(); var enabled = false; var calls = 0;
        var controller = new SettingsRetryController(_ => { calls++; return Task.FromResult(SettingsSyncStatus.Idle); }, () => enabled, time);
        Assert.Equal(SettingsRetryStatus.Disabled, (await controller.RunIfDueAsync()).Status); Assert.Equal(0, calls);
        enabled = true; Assert.Equal(SettingsRetryStatus.Idle, (await controller.RunIfDueAsync()).Status);
        Assert.Equal(SettingsRetryStatus.Waiting, (await controller.RunIfDueAsync()).Status); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TlsValidationFailureDoesNotRetryAutomatically()
    {
        var time = new Clock(); var calls = 0;
        var controller = new SettingsRetryController(_ =>
        {
            calls++; throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Synthetic TLS rejection");
        }, () => true, time);
        Assert.Equal(SettingsRetryStatus.NeedsAttention, (await controller.RunIfDueAsync()).Status);
        time.Advance(TimeSpan.FromDays(1)); await controller.RunIfDueAsync(); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SuccessfulTickResetsTransientFailureBackoff()
    {
        var time = new Clock(); var fail = true;
        var controller = new SettingsRetryController(_ => fail
            ? throw new SettingsTransportException(SettingsTransportFailure.Unavailable)
            : Task.FromResult(SettingsSyncStatus.Idle), () => true, time, () => 0.5);
        var first = await controller.RunIfDueAsync(); time.Advance(first.Wait!.Value);
        fail = false; var success = await controller.RunIfDueAsync(); time.Advance(success.Wait!.Value);
        fail = true; Assert.Equal(TimeSpan.FromSeconds(5), (await controller.RunIfDueAsync()).Wait);
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAutomaticRetry()
    {
        var time = new Clock(); using var source = new CancellationTokenSource();
        var controller = new SettingsRetryController(_ => { source.Cancel(); throw new OperationCanceledException(source.Token); }, () => true, time);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.RunIfDueAsync(source.Token));
    }

    [Fact]
    public async Task RetryAfterSurvivesRestartAndLocalChangeCannotBypassDurableDelay()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsRetryCheckpointStore(Path.Combine(root, "retry.bin"), Scope());
            var initialCalls = 0;
            var initial = new SettingsRetryController(_ =>
            {
                initialCalls++;
                throw new SettingsTransportException(SettingsTransportFailure.Throttled, TimeSpan.FromMinutes(10));
            }, () => true, new Clock(), () => 0.5, store);

            var throttled = await initial.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.Waiting, throttled.Status);
            Assert.Equal(TimeSpan.FromMinutes(10), throttled.Wait);
            Assert.Equal(1, initialCalls);
            Assert.Equal(new SettingsRetryCheckpoint(1, TimeSpan.FromMinutes(10).Ticks), await store.LoadAsync());

            var restartedClock = new Clock { WallClock = DateTimeOffset.UtcNow.AddYears(10) };
            var resumedCalls = 0;
            var resumed = new SettingsRetryController(_ =>
            {
                resumedCalls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, restartedClock, checkpointStore: store);

            var restartPoll = await resumed.RunIfDueAsync(SettingsSyncWakeReason.Startup);
            Assert.Equal(SettingsRetryStatus.Waiting, restartPoll.Status);
            Assert.Equal(TimeSpan.FromMinutes(10), restartPoll.Wait);
            var localChange = await resumed.RunIfDueAsync(SettingsSyncWakeReason.LocalSettingsChanged);
            Assert.Equal(SettingsRetryStatus.Waiting, localChange.Status);
            Assert.Equal(0, resumedCalls);

            restartedClock.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(SettingsRetryStatus.Idle, (await resumed.RunIfDueAsync()).Status);
            Assert.Equal(1, resumedCalls);
            Assert.Null(await store.LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RetryCheckpointIsProtectedAndBoundToAccountGenerationAndDevice()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            var scope = Scope();
            var checkpoint = new SettingsRetryCheckpoint(3, TimeSpan.FromSeconds(90).Ticks);
            var store = new SettingsRetryCheckpointStore(path, scope);
            await store.SaveAsync(checkpoint);
            Assert.Equal(checkpoint, await new SettingsRetryCheckpointStore(path, scope).LoadAsync());

            var otherScope = scope with { GenerationId = Guid.NewGuid().ToString("D") };
            await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                new SettingsRetryCheckpointStore(path, otherScope).LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CorruptRetryCheckpointFailsClosedUntilExplicitRecoveryCanReadIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            await File.WriteAllTextAsync(path, "invalid DPAPI retry data");
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, new Clock(), checkpointStore: new SettingsRetryCheckpointStore(path, Scope()));

            var first = await controller.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.NeedsAttention, first.Status);
            Assert.Equal(SettingsTransportFailure.Storage, first.Failure);
            await controller.ResumeAfterRecoveryAsync();
            var stillInvalid = await controller.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.NeedsAttention, stillInvalid.Status);
            Assert.Equal(SettingsTransportFailure.Storage, stillInvalid.Failure);
            Assert.Equal(0, calls);
            Assert.Equal("invalid DPAPI retry data", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DisabledConsentDoesNotReadOrClearDurableThrottle()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsRetryCheckpointStore(Path.Combine(root, "retry.bin"), Scope());
            var checkpoint = new SettingsRetryCheckpoint(2, TimeSpan.FromMinutes(1).Ticks);
            await store.SaveAsync(checkpoint);
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => false, new Clock(), checkpointStore: store);

            Assert.Equal(SettingsRetryStatus.Disabled, (await controller.RunIfDueAsync()).Status);
            Assert.Equal(checkpoint, await store.LoadAsync());
            Assert.Equal(0, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OrphanTemporaryCheckpointFailsClosedWithoutStartingNetworkRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Orphan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            var orphan = path + ".interrupted.tmp";
            await File.WriteAllTextAsync(orphan, "partial write");
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, new Clock(), checkpointStore: new SettingsRetryCheckpointStore(path, Scope()));

            var result = await controller.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.NeedsAttention, result.Status);
            Assert.Equal(SettingsTransportFailure.Storage, result.Failure);
            Assert.Equal(0, calls);
            Assert.Equal("partial write", await File.ReadAllTextAsync(orphan));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OversizedCheckpointFailsClosedBeforeNetworkRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Oversized-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            await using (var stream = File.Create(path)) stream.SetLength(16_385);
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, new Clock(), checkpointStore: new SettingsRetryCheckpointStore(path, Scope()));

            var result = await controller.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.NeedsAttention, result.Status);
            Assert.Equal(SettingsTransportFailure.Storage, result.Failure);
            Assert.Equal(0, calls);
            Assert.Equal(16_385, new FileInfo(path).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SettingsSyncScope Scope() => new("https://identity.example/realms/test",
        Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));

    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.UtcNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => WallClock;
        public void Advance(TimeSpan value) => ticks += value.Ticks;
    }
}
