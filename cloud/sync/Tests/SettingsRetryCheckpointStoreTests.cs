using System.Security.Cryptography;
using System.Text;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsRetryCheckpointStoreTests
{
    [Fact]
    public async Task CheckpointIsEncryptedAndBoundToOneAccountGenerationAndDevice()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            var scope = Scope();
            var expected = new SettingsRetryCheckpoint(3, TimeSpan.FromMinutes(10).Ticks);
            await new SettingsRetryCheckpointStore(path, scope).SaveAsync(expected);

            var ciphertext = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain(Encoding.UTF8.GetBytes("failureCount"), ciphertext);
            Assert.DoesNotContain(Encoding.UTF8.GetBytes("DelayTicks"), ciphertext);
            Assert.Equal(expected, await new SettingsRetryCheckpointStore(path, scope).LoadAsync());

            var otherScope = scope with { AccountId = Guid.NewGuid().ToString("D") };
            await Assert.ThrowsAnyAsync<CryptographicException>(() => new SettingsRetryCheckpointStore(path, otherScope).LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RetryAfterSurvivesControllerRestartAndCannotBeBypassedByLocalChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsRetryCheckpointStore(Path.Combine(root, "retry.bin"), Scope());
            var firstCalls = 0;
            var first = new SettingsRetryController(_ =>
            {
                firstCalls++;
                throw new SettingsTransportException(SettingsTransportFailure.Throttled, TimeSpan.FromMinutes(10));
            }, () => true, new Clock(), () => 0.5, store);
            var rejected = await first.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.Waiting, rejected.Status);
            Assert.Equal(TimeSpan.FromMinutes(10), rejected.Wait);
            Assert.Equal(1, firstCalls);
            Assert.Equal(new SettingsRetryCheckpoint(1, TimeSpan.FromMinutes(10).Ticks), await store.LoadAsync());

            var restartedClock = new Clock { WallClock = DateTimeOffset.UtcNow.AddYears(10) };
            var nextCalls = 0;
            var restarted = new SettingsRetryController(_ =>
            {
                nextCalls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, restartedClock, checkpointStore: store);

            var afterRestart = await restarted.RunIfDueAsync(SettingsSyncWakeReason.Startup);
            Assert.Equal(SettingsRetryStatus.Waiting, afterRestart.Status);
            Assert.Equal(TimeSpan.FromMinutes(10), afterRestart.Wait);
            var localChange = await restarted.RunIfDueAsync(SettingsSyncWakeReason.LocalSettingsChanged);
            Assert.Equal(SettingsRetryStatus.Waiting, localChange.Status);
            Assert.Equal(0, nextCalls);

            restartedClock.Advance(TimeSpan.FromMinutes(10));
            var due = await restarted.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.Idle, due.Status);
            Assert.Equal(1, nextCalls);
            Assert.Null(await store.LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CorruptCheckpointStopsBeforeAnyNetworkRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            await File.WriteAllTextAsync(path, "not a DPAPI checkpoint");
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
            Assert.Equal("not a DPAPI checkpoint", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CheckpointRepairRequiresOwnerConfirmationAndPreservesNeighborFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            var orphan = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var neighbor = path + "-backup.tmp";
            await File.WriteAllTextAsync(path, "corrupt checkpoint");
            await File.WriteAllTextAsync(orphan, "partial checkpoint");
            await File.WriteAllTextAsync(neighbor, "preserve me");
            var store = new SettingsRetryCheckpointStore(path, Scope());

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResetAfterOwnerRecoveryAsync(ownerConfirmed: false));
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(orphan));

            await store.ResetAfterOwnerRecoveryAsync(ownerConfirmed: true);
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(orphan));
            Assert.Equal("preserve me", await File.ReadAllTextAsync(neighbor));
            Assert.Null(await store.LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RetryControllerResumesOnlyAfterDamagedCheckpointIsExplicitlyRepaired()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "retry.bin");
            await File.WriteAllTextAsync(path, "corrupt checkpoint");
            var store = new SettingsRetryCheckpointStore(path, Scope());
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => true, new Clock(), checkpointStore: store);

            Assert.Equal(SettingsRetryStatus.NeedsAttention, (await controller.RunIfDueAsync()).Status);
            Assert.True((await controller.RunIfDueAsync()).RetryCheckpointRecoveryAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.ResetStorageCheckpointAfterOwnerRecoveryAsync(ownerConfirmed: false));
            Assert.Equal("corrupt checkpoint", await File.ReadAllTextAsync(path));
            await controller.ResetStorageCheckpointAfterOwnerRecoveryAsync(ownerConfirmed: true);
            Assert.False(File.Exists(path));
            Assert.Equal(SettingsRetryStatus.NeedsAttention, (await controller.RunIfDueAsync()).Status);
            Assert.Equal(0, calls);

            await controller.ResumeAfterRecoveryAsync();
            Assert.Equal(SettingsRetryStatus.Idle, (await controller.RunIfDueAsync()).Status);
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StorageFailureFromTheSyncTickCannotOfferRetryCheckpointRepair()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-TickStorage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsRetryCheckpointStore(Path.Combine(root, "retry.bin"), Scope());
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                throw new SettingsTransportException(SettingsTransportFailure.Storage);
            }, () => true, new Clock(), checkpointStore: store);

            var result = await controller.RunIfDueAsync();
            Assert.Equal(SettingsRetryStatus.NeedsAttention, result.Status);
            Assert.Equal(SettingsTransportFailure.Storage, result.Failure);
            Assert.False(result.RetryCheckpointRecoveryAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.ResetStorageCheckpointAfterOwnerRecoveryAsync(ownerConfirmed: true));
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DisabledConsentDoesNotLoadOrClearTheStoredThrottle()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Retry-Consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsRetryCheckpointStore(Path.Combine(root, "retry.bin"), Scope());
            var checkpoint = new SettingsRetryCheckpoint(2, TimeSpan.FromMinutes(1).Ticks);
            await store.SaveAsync(checkpoint);
            var consent = false;
            var calls = 0;
            var controller = new SettingsRetryController(_ =>
            {
                calls++;
                return Task.FromResult(SettingsSyncStatus.Idle);
            }, () => consent, new Clock(), checkpointStore: store);

            Assert.Equal(SettingsRetryStatus.Disabled, (await controller.RunIfDueAsync()).Status);
            Assert.Equal(checkpoint, await store.LoadAsync());
            Assert.Equal(0, calls);
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
