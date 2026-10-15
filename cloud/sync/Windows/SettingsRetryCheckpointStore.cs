using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Sync;

public sealed record SettingsRetryCheckpoint(int FailureCount, long DelayTicks)
{
    public SettingsRetryCheckpoint Validate()
    {
        if (FailureCount is < 1 or > 30 || DelayTicks < 1 || DelayTicks > TimeSpan.MaxValue.Ticks)
            throw new InvalidDataException("Invalid settings retry checkpoint.");
        return this;
    }
}

/// <summary>
/// DPAPI-protected retry throttle scoped to one device/account generation. A new process
/// conservatively waits the full stored delay using a fresh monotonic clock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsRetryCheckpointStore
{
    private const int MaxBytes = 8_192;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record StoredCheckpoint(int Version, SettingsSyncScope Scope, SettingsRetryCheckpoint Checkpoint);
    private readonly string path;
    private readonly SettingsSyncScope scope;
    private readonly byte[] entropy;

    public SettingsRetryCheckpointStore(string path, SettingsSyncScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();
        this.path = Path.GetFullPath(path);
        this.scope = scope;
        entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FG-SETTINGS-RETRY-1\0" + scope.Issuer + "\0" +
            scope.AccountId + "\0" + scope.GenerationId + "\0" + scope.DeviceId));
    }

    public async Task<SettingsRetryCheckpoint?> LoadAsync(CancellationToken token = default)
    {
        await using var lease = await AcquireAsync(token);
        return await LoadLockedAsync(token);
    }

    public async Task SaveAsync(SettingsRetryCheckpoint checkpoint, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        checkpoint.Validate();
        await using var lease = await AcquireAsync(token);

        var clear = JsonSerializer.SerializeToUtf8Bytes(new StoredCheckpoint(1, scope, checkpoint), Options);
        byte[] encrypted;
        try
        {
            if (clear.Length > MaxBytes) throw new InvalidDataException("Retry checkpoint is too large.");
            encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Remove a throttle only after its monotonic deadline has elapsed.</summary>
    internal async Task ClearAsync(CancellationToken token = default)
    {
        await using var lease = await AcquireAsync(token);
        if (!File.Exists(path))
        {
            if (Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp").Length != 0)
                throw new InvalidDataException("Uncommitted retry checkpoint requires recovery.");
            return;
        }
        File.Delete(path);
    }

    /// <summary>Delete a damaged throttle only after the owner completed deliberate recovery.</summary>
    public async Task ResetAfterOwnerRecoveryAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        if (!ownerConfirmed) throw new InvalidOperationException("Explicit owner recovery is required to reset the retry checkpoint.");
        await using var lease = await AcquireAsync(token);
        var directory = Path.GetDirectoryName(path)!;
        var prefix = Path.GetFileName(path) + ".";
        var orphaned = Directory.EnumerateFiles(directory)
            .Where(candidate => IsStoreTemporary(Path.GetFileName(candidate), prefix))
            .Take(33)
            .ToArray();
        if (orphaned.Length > 32) throw new InvalidDataException("Too many orphaned retry checkpoint files require manual recovery.");

        token.ThrowIfCancellationRequested();
        foreach (var temporary in orphaned) File.Delete(temporary);
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool IsStoreTemporary(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return false;
        var identifier = name[prefix.Length..^4];
        return Guid.TryParseExact(identifier, "N", out _);
    }

    private async Task<SettingsRetryCheckpoint?> LoadLockedAsync(CancellationToken token)
    {
        if (!File.Exists(path))
        {
            if (Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp").Length != 0)
                throw new InvalidDataException("Uncommitted retry checkpoint requires recovery.");
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length is < 1 or > MaxBytes + 8_192) throw new InvalidDataException("Invalid retry checkpoint size.");
        var encrypted = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(encrypted, token);
        byte[] clear;
        try { clear = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }

        try
        {
            if (clear.Length > MaxBytes) throw new InvalidDataException("Invalid retry checkpoint size.");
            using var document = JsonDocument.Parse(clear, new JsonDocumentOptions { MaxDepth = 8 });
            ValidateUniqueProperties(document.RootElement);
            var stored = JsonSerializer.Deserialize<StoredCheckpoint>(clear, Options);
            if (stored is null || stored.Version != 1 || stored.Scope != scope || stored.Checkpoint is null)
                throw new InvalidDataException("Retry checkpoint scope or version mismatch.");
            return stored.Checkpoint.Validate();
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous retry checkpoint field.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) ValidateUniqueProperties(child);
        }
    }

    private async Task<FileStream> AcquireAsync(CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Retry checkpoint path has no parent.");
        Directory.CreateDirectory(directory);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && attempt < 80)
            { await Task.Delay(25, token); }
        }
    }
}
