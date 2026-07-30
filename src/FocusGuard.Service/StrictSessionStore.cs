using System.Runtime.Versioning;
using System.Security.Cryptography;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service;

public interface IStrictSessionStore
{
    ValueTask ActivateAsync(
        Session session,
        EnforcementPolicy policy,
        RecoveryCredential recoveryCredential,
        CancellationToken cancellationToken);

    ValueTask<StrictPolicyLoadResult> LoadAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed class StrictSessionStore(
    string? directory = null,
    Func<string>? bootIdProvider = null) : IStrictSessionStore
{
    private readonly string _directory = directory ?? StrictPolicySnapshotStore.DefaultDirectory;
    private readonly Func<string> _bootIdProvider =
        bootIdProvider ?? RebootExpirationDrillWorker.GetCurrentBootId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask ActivateAsync(
        Session session,
        EnforcementPolicy policy,
        RecoveryCredential recoveryCredential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(recoveryCredential);
        if (!session.StrictMode.Enabled
            || session.State is not (SessionState.Activating or SessionState.Active)
            || !policy.IsStrict
            || policy.SessionId != session.Id
            || policy.ProfileId != session.ProfileId)
        {
            throw new InvalidOperationException("Only a pending or active strict session can be persisted.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var integrityKey = await LoadOrCreateIntegrityKeyAsync(cancellationToken);
            try
            {
                var snapshot = new StrictPolicySnapshot(
                    StrictPolicySnapshotStore.CurrentSchemaVersion,
                    policy,
                    session.StartUtc,
                    _bootIdProvider(),
                    recoveryCredential,
                    session.StrictMode.RecoveryDelay);
                await new StrictPolicySnapshotStore(integrityKey, _directory)
                    .SaveAsync(snapshot, cancellationToken);
                await new RecoveryAuditLog(_directory).WriteAsync(
                    session.Id,
                    session.StartUtc,
                    "StrictActivated",
                    "DesktopConfirmedImmutablePolicy",
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(integrityKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<StrictPolicyLoadResult> LoadAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var keyPath = Path.Combine(_directory, StrictPolicyPersistenceWorker.IntegrityKeyFileName);
        if (!OperatingSystem.IsWindows() || !File.Exists(keyPath))
        {
            return new StrictPolicyLoadResult(
                StrictPolicyLoadStatus.Absent,
                null,
                "NoProvisionedIntegrityKey");
        }

        var integrityKey = await ReadIntegrityKeyAsync(keyPath, cancellationToken);
        try
        {
            return await new StrictPolicySnapshotStore(integrityKey, _directory)
                .LoadAsync(nowUtc.ToUniversalTime(), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(integrityKey);
        }
    }

    private async Task<byte[]> LoadOrCreateIntegrityKeyAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Strict persistence requires Windows DPAPI.");
        }

        Directory.CreateDirectory(_directory);
        var keyPath = Path.Combine(_directory, StrictPolicyPersistenceWorker.IntegrityKeyFileName);
        if (File.Exists(keyPath))
        {
            return await ReadIntegrityKeyAsync(keyPath, cancellationToken);
        }

        var integrityKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var protectedKey = ProtectedData.Protect(
                integrityKey,
                optionalEntropy: null,
                DataProtectionScope.LocalMachine);
            var temporaryPath = keyPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedKey, cancellationToken);
                try
                {
                    File.Move(temporaryPath, keyPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(keyPath))
                {
                    File.Delete(temporaryPath);
                    CryptographicOperations.ZeroMemory(integrityKey);
                    return await ReadIntegrityKeyAsync(keyPath, cancellationToken);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return integrityKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(integrityKey);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<byte[]> ReadIntegrityKeyAsync(
        string keyPath,
        CancellationToken cancellationToken)
    {
        var protectedKey = await File.ReadAllBytesAsync(keyPath, cancellationToken);
        var integrityKey = ProtectedData.Unprotect(
            protectedKey,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        if (integrityKey.Length < 32)
        {
            CryptographicOperations.ZeroMemory(integrityKey);
            throw new CryptographicException("Strict persistence integrity key is invalid.");
        }

        return integrityKey;
    }
}
