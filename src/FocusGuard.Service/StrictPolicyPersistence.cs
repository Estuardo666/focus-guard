using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FocusGuard.Service;

public sealed record StrictPolicySnapshot(
    int SchemaVersion,
    EnforcementPolicy Policy,
    DateTimeOffset LastObservedUtc,
    string BootId,
    RecoveryCredential? RecoveryCredential = null,
    TimeSpan RecoveryDelay = default);

public sealed record SignedStrictPolicySnapshot(
    StrictPolicySnapshot Snapshot,
    string Integrity);

public enum StrictPolicyLoadStatus
{
    Absent,
    Active,
    Expired,
    Recovered,
    FailedOpen
}

public sealed record StrictPolicyLoadResult(
    StrictPolicyLoadStatus Status,
    StrictPolicySnapshot? Snapshot,
    string Reason);

public sealed class StrictPolicySnapshotStore
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumRuleCount = 10_000;
    public const int MaximumPayloadBytes = 1_048_576;
    private const int ReplaceAttempts = 8;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(25);
    public static string DefaultDirectory
    {
        get
        {
#if FOCUSGUARD_PRODUCTION
            return @"C:\ProgramData\FocusGuard\StrictPersistence";
#else
            return @"C:\ProgramData\FocusGuard\Development\StrictPersistence";
#endif
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly byte[] _integrityKey;

    public StrictPolicySnapshotStore(byte[] integrityKey, string? directory = null)
        : this(integrityKey.AsSpan(), directory)
    {
    }

    public StrictPolicySnapshotStore(ReadOnlySpan<byte> integrityKey, string? directory = null)
    {
        if (integrityKey.Length < 32)
        {
            throw new ArgumentException("The integrity key must contain at least 32 bytes.", nameof(integrityKey));
        }

        _integrityKey = integrityKey.ToArray();
        _directory = directory ?? DefaultDirectory;
    }

    public string ActivePath => Path.Combine(_directory, "active-policy.json");

    public async Task SaveAsync(StrictPolicySnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);

        var signed = new SignedStrictPolicySnapshot(snapshot, ComputeIntegrity(snapshot));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(signed, JsonOptions);
        if (bytes.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The strict policy snapshot exceeds the payload limit.");
        }

        Directory.CreateDirectory(_directory);
        var temporaryPath = ActivePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            await ReplaceActiveSnapshotAsync(temporaryPath, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task ReplaceActiveSnapshotAsync(
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, ActivePath, true);
                return;
            }
            catch (Exception exception) when (
                attempt < ReplaceAttempts
                && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(ReplaceRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<StrictPolicyLoadResult> LoadAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ActivePath))
        {
            return new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "NoActivePolicy");
        }

        try
        {
            var file = new FileInfo(ActivePath);
            if (file.Length is <= 0 or > MaximumPayloadBytes)
            {
                return FailOpen("PayloadSizeInvalid");
            }

            var bytes = await ReadSnapshotBytesAsync(cancellationToken);
            var signed = JsonSerializer.Deserialize<SignedStrictPolicySnapshot>(bytes, JsonOptions);
            if (signed is null)
            {
                return FailOpen("SnapshotMissing");
            }

            Validate(signed.Snapshot);
            var expected = Convert.FromHexString(ComputeIntegrity(signed.Snapshot));
            byte[] actual;
            try
            {
                actual = Convert.FromHexString(signed.Integrity);
            }
            catch (FormatException)
            {
                return FailOpen("IntegrityInvalid");
            }

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return FailOpen("IntegrityInvalid");
            }

            var trustedNow = MaxUtc(nowUtc, signed.Snapshot.LastObservedUtc);
            if (trustedNow >= signed.Snapshot.Policy.EndUtc)
            {
                await ReleaseAsync(cancellationToken);
                return new StrictPolicyLoadResult(
                    StrictPolicyLoadStatus.Expired,
                    null,
                    "Expired");
            }

            return new StrictPolicyLoadResult(
                StrictPolicyLoadStatus.Active,
                signed.Snapshot,
                "Recovered");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or ArgumentException)
        {
            return FailOpen(exception.GetType().Name);
        }
    }

    public Task ReleaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(ActivePath))
        {
            File.Delete(ActivePath);
        }

        return Task.CompletedTask;
    }

    private async Task<byte[]> ReadSnapshotBytesAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            ActivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The strict policy snapshot has an invalid payload size.");
        }

        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private StrictPolicyLoadResult FailOpen(string reason)
    {
        try
        {
            if (File.Exists(ActivePath))
            {
                Directory.CreateDirectory(_directory);
                var quarantinePath = Path.Combine(
                    _directory,
                    $"failed-open-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
                File.Move(ActivePath, quarantinePath);
            }
        }
        catch (IOException)
        {
            File.Delete(ActivePath);
        }
        catch (UnauthorizedAccessException)
        {
            // The caller still receives FailedOpen and must not enforce this snapshot.
        }

        return new StrictPolicyLoadResult(StrictPolicyLoadStatus.FailedOpen, null, reason);
    }

    private string ComputeIntegrity(StrictPolicySnapshot snapshot)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        return Convert.ToHexString(HMACSHA256.HashData(_integrityKey, payload));
    }

    private static void Validate(StrictPolicySnapshot snapshot)
    {
        if (snapshot.SchemaVersion != CurrentSchemaVersion
            || snapshot.Policy is null
            || !snapshot.Policy.IsStrict
            || snapshot.Policy.SessionId == Guid.Empty
            || snapshot.Policy.ProfileId is null
            || snapshot.Policy.ProfileId.Value == Guid.Empty
            || snapshot.Policy.StartUtc.Offset != TimeSpan.Zero
            || snapshot.Policy.EndUtc.Offset != TimeSpan.Zero
            || snapshot.Policy.EndUtc <= snapshot.Policy.StartUtc
            || snapshot.Policy.EndUtc - snapshot.Policy.StartUtc > Session.MaximumDuration
            || snapshot.LastObservedUtc.Offset != TimeSpan.Zero
            || snapshot.LastObservedUtc < snapshot.Policy.StartUtc
            || snapshot.LastObservedUtc > snapshot.Policy.EndUtc
            || string.IsNullOrWhiteSpace(snapshot.BootId)
            || snapshot.BootId.Length > 128
            || !RecoveryCodeService.IsValid(snapshot.RecoveryCredential)
            || snapshot.RecoveryDelay < TimeSpan.FromMinutes(1)
            || snapshot.RecoveryDelay > TimeSpan.FromHours(24))
        {
            throw new InvalidDataException("The strict policy snapshot is invalid.");
        }

        var applicationRules = snapshot.Policy.ApplicationRules
            ?? throw new InvalidDataException("Application rules are required.");
        var websiteRules = snapshot.Policy.WebsiteRules
            ?? throw new InvalidDataException("Website rules are required.");
        var breakApplicationRules = snapshot.Policy.BreakApplicationRules ?? [];
        var breakWebsiteRules = snapshot.Policy.BreakWebsiteRules ?? [];
        if (applicationRules.Count + websiteRules.Count
                + breakApplicationRules.Count + breakWebsiteRules.Count > MaximumRuleCount
            || applicationRules.Any(rule => !IsValid(rule))
            || websiteRules.Any(rule => !IsValid(rule))
            // Break exceptions are Block-only at ingress (CycleIpcProtocol, FG-216); snapshots written by older
            // builds carry Allow, so persistence accepts any defined action and leaves the semantics to ingress.
            || breakApplicationRules.Any(rule => !IsValid(rule))
            || breakWebsiteRules.Any(rule => !IsValid(rule))
            || snapshot.Policy.CyclePlan is { } plan && !IsValid(plan, snapshot.Policy.EndUtc))
        {
            throw new InvalidDataException("The strict policy rules are invalid.");
        }
    }

    private static bool IsValid(ApplicationRule? rule) =>
        rule is not null
        && rule.Id != Guid.Empty
        && Enum.IsDefined(rule.Action)
        && Enum.IsDefined(rule.MatchKind)
        && !string.IsNullOrWhiteSpace(rule.Value)
        && rule.Value.Length <= 32_768;

    private static bool IsValid(WebsiteRule? rule) =>
        rule is not null
        && rule.Id != Guid.Empty
        && Enum.IsDefined(rule.Action)
        && !string.IsNullOrWhiteSpace(rule.Pattern)
        && rule.Pattern.Length <= 32_768
        && !rule.Pattern.Contains('?', StringComparison.Ordinal);

    private static bool IsValid(FocusCyclePlan plan, DateTimeOffset policyEndUtc) =>
        plan.Phases.Count > 0
        && plan.EndUtc == policyEndUtc
        && plan.Phases[^1].EndUtc == plan.EndUtc
        && plan.Phases.All(phase =>
            phase.Index >= 0
            && Enum.IsDefined(phase.Kind)
            && phase.FocusCycleNumber > 0
            && phase.StartUtc.Offset == TimeSpan.Zero
            && phase.EndUtc.Offset == TimeSpan.Zero
            && phase.EndUtc > phase.StartUtc);

    private static DateTimeOffset MaxUtc(DateTimeOffset left, DateTimeOffset right) =>
        left.ToUniversalTime() >= right.ToUniversalTime()
            ? left.ToUniversalTime()
            : right.ToUniversalTime();
}

public sealed class StrictPolicyPersistenceCoordinator
{
    private readonly StrictPolicySnapshotStore _store;
    private readonly DelayedRecoveryRequestStore? _recoveryStore;
    private readonly RecoveryAuditLog? _auditLog;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _bootIdProvider;
    private StrictPolicySnapshot? _active;
    private DateTimeOffset _utcAnchor;
    private long _timestampAnchor;

    public StrictPolicyPersistenceCoordinator(
        StrictPolicySnapshotStore store,
        TimeProvider? timeProvider = null,
        Func<string>? bootIdProvider = null,
        DelayedRecoveryRequestStore? recoveryStore = null,
        RecoveryAuditLog? auditLog = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _bootIdProvider = bootIdProvider ?? RebootExpirationDrillWorker.GetCurrentBootId;
        _recoveryStore = recoveryStore;
        _auditLog = auditLog;
    }

    public StrictPolicySnapshot? ActiveSnapshot => _active;

    public async Task<StrictPolicyLoadResult> RecoverAsync(CancellationToken cancellationToken)
    {
        var wallUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var result = await _store.LoadAsync(wallUtc, cancellationToken);
        _active = result.Snapshot;
        if (_active is not null)
        {
            _utcAnchor = wallUtc >= _active.LastObservedUtc ? wallUtc : _active.LastObservedUtc;
            _timestampAnchor = _timeProvider.GetTimestamp();
        }

        return result;
    }

    public async Task<StrictPolicyLoadStatus> ReconcileAsync(CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return StrictPolicyLoadStatus.Absent;
        }

        var monotonicUtc = _utcAnchor + _timeProvider.GetElapsedTime(_timestampAnchor);
        var wallUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var trustedUtc = wallUtc >= monotonicUtc ? wallUtc : monotonicUtc;
        if (trustedUtc >= _active.Policy.EndUtc)
        {
            await _store.ReleaseAsync(cancellationToken);
            if (_recoveryStore is not null)
            {
                await _recoveryStore.ReleaseAsync(cancellationToken);
            }

            _active = null;
            return StrictPolicyLoadStatus.Expired;
        }

        if (_recoveryStore is not null)
        {
            var request = await _recoveryStore.LoadAsync(_active, cancellationToken);
            if (request is not null && trustedUtc >= request.EligibleUtc)
            {
                var sessionId = _active.Policy.SessionId;
                await _store.ReleaseAsync(cancellationToken);
                await _recoveryStore.ReleaseAsync(cancellationToken);
                if (_auditLog is not null)
                {
                    await _auditLog.WriteAsync(
                        sessionId,
                        trustedUtc,
                        "RecoveryReleased",
                        "DelayElapsed",
                        cancellationToken);
                }

                _active = null;
                return StrictPolicyLoadStatus.Recovered;
            }
        }

        _active = _active with
        {
            LastObservedUtc = trustedUtc,
            BootId = _bootIdProvider()
        };
        await _store.SaveAsync(_active, cancellationToken);
        _utcAnchor = trustedUtc;
        _timestampAnchor = _timeProvider.GetTimestamp();
        return StrictPolicyLoadStatus.Active;
    }
}

public sealed class StrictPolicyPersistenceWorker : BackgroundService
{
    public const string IntegrityKeyFileName = "integrity-key.dpapi";
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromSeconds(1);
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StrictPolicyPersistenceWorker> _logger;

    public StrictPolicyPersistenceWorker(
        ILogger<StrictPolicyPersistenceWorker>? logger = null,
        TimeProvider? timeProvider = null,
        string? directory = null)
    {
        _logger = logger ?? NullLogger<StrictPolicyPersistenceWorker>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directory = directory ?? StrictPolicySnapshotStore.DefaultDirectory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcilePersistedPolicyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ServiceFailureLog.WriteAsync(nameof(StrictPolicyPersistenceWorker), exception);
            throw;
        }
    }

    private async Task ReconcilePersistedPolicyAsync(CancellationToken stoppingToken)
    {
        var keyPath = Path.Combine(_directory, IntegrityKeyFileName);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!File.Exists(keyPath))
            {
                await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
                continue;
            }

            byte[] integrityKey;
            try
            {
                var protectedKey = await File.ReadAllBytesAsync(keyPath, stoppingToken);
                integrityKey = ProtectedData.Unprotect(
                    protectedKey,
                    optionalEntropy: null,
                    DataProtectionScope.LocalMachine);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or CryptographicException)
            {
                _logger.LogError(
                    exception,
                    "Strict persistence key loading failed; no persisted policy will be enforced.");
                await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
                continue;
            }

            try
            {
                if (integrityKey.Length < 32)
                {
                    _logger.LogError("Strict persistence key is invalid; no persisted policy will be enforced.");
                    await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
                    continue;
                }

                var store = new StrictPolicySnapshotStore(integrityKey, _directory);
                var recoveryStore = new DelayedRecoveryRequestStore(integrityKey, _directory);
                var coordinator = new StrictPolicyPersistenceCoordinator(
                    store,
                    _timeProvider,
                    recoveryStore: recoveryStore,
                    auditLog: new RecoveryAuditLog(_directory));
                var recovery = await coordinator.RecoverAsync(stoppingToken);
                if (recovery.Status != StrictPolicyLoadStatus.Active)
                {
                    await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
                    continue;
                }

                _logger.LogInformation(
                    "Recovered strict session {SessionId}; it expires at {EndUtc}.",
                    recovery.Snapshot!.Policy.SessionId,
                    recovery.Snapshot.Policy.EndUtc);
                StrictPolicyLoadStatus status;
                do
                {
                    await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
                }
                while ((status = await coordinator.ReconcileAsync(stoppingToken))
                    == StrictPolicyLoadStatus.Active);

                _logger.LogInformation(
                    "The persisted strict session ended with status {Status} and was released.",
                    status);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(integrityKey);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(ReconciliationInterval, _timeProvider, stoppingToken);
            }
        }
    }
}
