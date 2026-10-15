using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Sync;

public sealed record SettingsSyncScope(string Issuer, string AccountId, string GenerationId, string DeviceId)
{
    public void Validate()
    {
        if (Issuer is null || Issuer.Length > 1024 || !Uri.TryCreate(Issuer, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Invalid sync issuer.");
        foreach (var id in new[] { AccountId, GenerationId, DeviceId })
            if (!Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty || parsed.ToString("D") != id)
                throw new ArgumentException("Invalid sync scope identifier.");
    }
}

/// <summary>Current-user DPAPI, scoped to issuer/account/generation/device. No fallback after corruption.</summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsOutboxVault
{
    private const int MaxBytes = 262_144;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record StoredState(int Version, SettingsSyncScope Scope, SettingsOutbox State);
    private readonly string path;
    private readonly SettingsSyncScope scope;
    private readonly byte[] entropy;

    public SettingsOutboxVault(string path, SettingsSyncScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope); scope.Validate();
        this.path = Path.GetFullPath(path); this.scope = scope;
        entropy = SHA256.HashData(Encoding.UTF8.GetBytes("FG-SETTINGS-OUTBOX-1\0" + scope.Issuer + "\0" +
            scope.AccountId + "\0" + scope.GenerationId + "\0" + scope.DeviceId));
    }

    public void RequireScope(string issuer, string accountId, string generationId, string deviceId)
    {
        if (scope != new SettingsSyncScope(issuer, accountId, generationId, deviceId))
            throw new InvalidOperationException("Sync session does not match the vault scope.");
    }

    public async Task SaveAsync(SettingsOutbox state, CancellationToken token = default)
    {
        await using var lease = await LockAsync(token);
        await SaveLockedAsync(state, token);
    }

    /// <summary>Read-modify-write under an OS file lease, including across vault instances/processes.</summary>
    public async Task<SettingsOutbox> UpdateAsync(Func<SettingsOutbox?, SettingsOutbox> update, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await using var lease = await LockAsync(token);
        var state = update(await LoadLockedAsync(token));
        await SaveLockedAsync(state, token);
        return state; // A caller may send only after this durable commit succeeds.
    }

    /// <summary>Keep the OS lease through projection; cooperating workers cannot advance state mid-application.
    /// The callback must not call this vault again. Outbox commit precedes projection, so failure is recoverable.</summary>
    public async Task<T> UpdateAndProjectAsync<T>(Func<SettingsOutbox?, SettingsOutbox> update,
        Func<SettingsOutbox, CancellationToken, Task<T>> project, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(update); ArgumentNullException.ThrowIfNull(project);
        await using var lease = await LockAsync(token);
        var state = update(await LoadLockedAsync(token));
        await SaveLockedAsync(state, token);
        return await project(state, token);
    }

    public async Task<T> ProjectCurrentAsync<T>(Func<SettingsOutbox?, CancellationToken, Task<T>> project,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        await using var lease = await LockAsync(token);
        return await project(await LoadLockedAsync(token), token);
    }

    private async Task SaveLockedAsync(SettingsOutbox state, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(state); state.Validate();
        var clear = JsonSerializer.SerializeToUtf8Bytes(new StoredState(1, scope, state), Options);
        byte[] encrypted;
        try
        {
            if (clear.Length > MaxBytes) throw new InvalidDataException("Settings state is too large.");
            encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<SettingsOutbox?> LoadAsync(CancellationToken token = default)
    {
        await using var lease = await LockAsync(token);
        return await LoadLockedAsync(token);
    }

    private async Task<SettingsOutbox?> LoadLockedAsync(CancellationToken token)
    {
        if (!File.Exists(path))
        {
            if (Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp").Length != 0)
                throw new InvalidDataException("Uncommitted settings state requires recovery.");
            return null;
        }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length is < 1 or > MaxBytes + 8192) throw new InvalidDataException("Invalid settings vault size.");
        var encrypted = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(encrypted, token);
        var clear = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
        try
        {
            if (clear.Length > MaxBytes) throw new InvalidDataException("Invalid settings vault size.");
            var stored = JsonSerializer.Deserialize<StoredState>(clear, Options);
            if (stored is null || stored.Version != 1 || stored.Scope != scope || stored.State is null)
                throw new InvalidDataException("Invalid settings vault context or version.");
            stored.State.Validate();
            return stored.State;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && attempt < 80)
            {
                await Task.Delay(25, token);
            }
        }
    }
}
