using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FocusGuard.Devices;

/// <summary>Separate from OIDC tokens; current Windows user, account and generation bound.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceVault
{
    private static readonly JsonSerializerOptions HeaderJson = new(JsonSerializerDefaults.Web);
    private readonly string path;
    public WindowsDeviceVault(string path) => this.path = Path.GetFullPath(path);
    public bool Exists => File.Exists(path);

    private sealed record VaultHeader(string Protocol, string DeviceId, string Issuer,
        string AccountBinding, string AccountId, string GenerationId);

    public sealed record LoadedDeviceVault(DeviceAccountContext Context, string DeviceId,
        RestoredDeviceMaterial Material) : IDisposable
    {
        public void Dispose() => Material.Dispose();
    }

    private static byte[] Entropy(DeviceAccountContext context, string deviceId)
    {
        context.Validate(); DeviceEncoding.RequireId(deviceId);
        return SHA256.HashData(Encoding.UTF8.GetBytes("FocusGuard.DeviceVault.v1\0" + context.Issuer + "\0" +
            context.AccountBinding + "\0" + context.AccountId + "\0" + context.GenerationId + "\0" + deviceId));
    }

    public async Task SaveAsync(DeviceIdentity identity, RecoveryKit? recoveryKit, DeviceAccountContext context, ReadOnlyMemory<byte> accountContentKey,
        long membershipRevision, string membershipDigest, CancellationToken cancellationToken, string? deviceSession = null,
        string? pendingPairingId = null, bool allowReplace = false, long keyEpoch = 1,
        IReadOnlyDictionary<long, byte[]>? historicalContentKeys = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plaintext = identity.SerializeForVault(context, recoveryKit, accountContentKey.Span,
            keyEpoch, historicalContentKeys, membershipRevision, membershipDigest, deviceSession, pendingPairingId);
        byte[] ciphertext;
        try { ciphertext = ProtectedData.Protect(plaintext, Entropy(context, identity.Public.DeviceId), DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var header = JsonSerializer.SerializeToUtf8Bytes(new VaultHeader("FG-DEVICE-VAULT-HEADER-1",
            identity.Public.DeviceId, context.Issuer, context.AccountBinding, context.AccountId, context.GenerationId), HeaderJson);
        var fileContents = new byte[sizeof(int) + header.Length + ciphertext.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(fileContents, header.Length);
        header.CopyTo(fileContents.AsSpan(sizeof(int)));
        ciphertext.CopyTo(fileContents.AsSpan(sizeof(int) + header.Length));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, fileContents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Initial creation is create-only; signed membership updates may atomically replace
            // this same device's vault after the old context has been verified.
            File.Move(temporary, path, overwrite: allowReplace);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(header);
            CryptographicOperations.ZeroMemory(fileContents);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task UpdateMembershipAsync(DeviceAccountContext context, string deviceId,
        long expectedRevision, long proposedRevision, string proposedDigest,
        CancellationToken cancellationToken)
    {
        using var current = await LoadAsync(context, deviceId, cancellationToken).ConfigureAwait(false);
        if (current.MembershipRevision != expectedRevision || proposedRevision != expectedRevision + 1)
            throw new InvalidOperationException("Device vault membership update is stale.");
        var history = current.CopyHistoricalContentKeys();
        try
        {
            await SaveAsync(current.Identity, current.RecoveryKit, context, current.AccountContentKey,
                proposedRevision, proposedDigest, cancellationToken, current.DeviceSession,
                current.PendingPairingId, allowReplace: true, keyEpoch: current.KeyEpoch,
                historicalContentKeys: history).ConfigureAwait(false);
        }
        finally { foreach (var key in history.Values) CryptographicOperations.ZeroMemory(key); }
    }

    public async Task RotateContentKeyAsync(DeviceAccountContext context, string deviceId,
        long expectedRevision, long proposedRevision, string proposedDigest, long newKeyEpoch,
        ReadOnlyMemory<byte> newContentKey, CancellationToken cancellationToken)
    {
        if (newContentKey.Length != 32) throw new ArgumentException("Content key must contain 32 bytes.");
        using var current = await LoadAsync(context, deviceId, cancellationToken).ConfigureAwait(false);
        if (current.MembershipRevision != expectedRevision || proposedRevision != expectedRevision + 1 ||
            newKeyEpoch != current.KeyEpoch + 1)
            throw new InvalidOperationException("Device vault key rotation is stale.");
        var history = current.CopyHistoricalContentKeys();
        if (history.ContainsKey(current.KeyEpoch)) throw new InvalidOperationException("Content key epoch already exists in the local key ring.");
        history.Add(current.KeyEpoch, current.AccountContentKey.ToArray());
        try
        {
            await SaveAsync(current.Identity, current.RecoveryKit, context, newContentKey,
                proposedRevision, proposedDigest, cancellationToken, current.DeviceSession,
                current.PendingPairingId, allowReplace: true, keyEpoch: newKeyEpoch,
                historicalContentKeys: history).ConfigureAwait(false);
        }
        finally { foreach (var key in history.Values) CryptographicOperations.ZeroMemory(key); }
    }

    public async Task UpdateDeviceSessionAsync(DeviceAccountContext context, DevicePublicIdentity expectedIdentity,
        long expectedRevision, string expectedDigest, string deviceSession, CancellationToken cancellationToken)
    {
        using var current = await LoadAsync(context, expectedIdentity.DeviceId, cancellationToken).ConfigureAwait(false);
        if (current.Identity.Public != expectedIdentity || current.MembershipRevision != expectedRevision ||
            current.MembershipDigest != expectedDigest)
            throw new InvalidOperationException("Device vault session update is stale.");
        var history = current.CopyHistoricalContentKeys();
        try
        {
            await SaveAsync(current.Identity, current.RecoveryKit, context, current.AccountContentKey,
                current.MembershipRevision, current.MembershipDigest, cancellationToken, deviceSession,
                current.PendingPairingId, allowReplace: true, keyEpoch: current.KeyEpoch,
                historicalContentKeys: history).ConfigureAwait(false);
        }
        finally { foreach (var key in history.Values) CryptographicOperations.ZeroMemory(key); }
    }

    public void Delete()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public async Task<RestoredDeviceMaterial> LoadAsync(DeviceAccountContext context, string deviceId, CancellationToken cancellationToken)
    {
        var file = await ReadVaultFileAsync(cancellationToken);
        try
        {
            if (file.Header.DeviceId != deviceId || file.Header.Issuer != context.Issuer ||
                file.Header.AccountBinding != context.AccountBinding || file.Header.AccountId != context.AccountId ||
                file.Header.GenerationId != context.GenerationId)
                throw new InvalidDataException("Device vault context mismatch.");
            var plaintext = ProtectedData.Unprotect(file.Ciphertext, Entropy(context, deviceId), DataProtectionScope.CurrentUser);
            try { return DeviceIdentity.DeserializeVault(plaintext, context, deviceId); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { CryptographicOperations.ZeroMemory(file.Ciphertext); }
    }

    public async Task<LoadedDeviceVault?> LoadForAccountAsync(string issuer, string accountBinding,
        CancellationToken cancellationToken)
    {
        var file = await ReadVaultFileAsync(cancellationToken);
        try
        {
            if (!string.Equals(file.Header.Issuer, issuer, StringComparison.Ordinal) ||
                !string.Equals(file.Header.AccountBinding, accountBinding, StringComparison.Ordinal)) return null;
            var context = new DeviceAccountContext(file.Header.Issuer, file.Header.AccountBinding,
                file.Header.AccountId, file.Header.GenerationId);
            context.Validate();
            var plaintext = ProtectedData.Unprotect(file.Ciphertext, Entropy(context, file.Header.DeviceId), DataProtectionScope.CurrentUser);
            try
            {
                return new(context, file.Header.DeviceId,
                    DeviceIdentity.DeserializeVault(plaintext, context, file.Header.DeviceId));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        finally { CryptographicOperations.ZeroMemory(file.Ciphertext); }
    }

    private async Task<(VaultHeader Header, byte[] Ciphertext)> ReadVaultFileAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 8 or > 24576) throw new InvalidDataException("Invalid device vault size.");
        var file = new byte[(int)stream.Length];
        try
        {
            await stream.ReadExactlyAsync(file, cancellationToken);
            var headerLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(file);
            if (headerLength is < 2 or > 4096 || sizeof(int) + headerLength >= file.Length)
                throw new InvalidDataException("Invalid device vault header.");
            using var document = JsonDocument.Parse(file.AsMemory(sizeof(int), headerLength), new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate device vault header field.");
            if (root.ValueKind != JsonValueKind.Object || !names.SetEquals(
                ["protocol", "deviceId", "issuer", "accountBinding", "accountId", "generationId"]))
                throw new InvalidDataException("Invalid device vault header.");
            var header = JsonSerializer.Deserialize<VaultHeader>(root.GetRawText(), HeaderJson)
                ?? throw new InvalidDataException("Invalid device vault header.");
            if (header.Protocol != "FG-DEVICE-VAULT-HEADER-1") throw new InvalidDataException("Unsupported device vault header.");
            DeviceEncoding.RequireId(header.DeviceId);
            var context = new DeviceAccountContext(header.Issuer, header.AccountBinding, header.AccountId, header.GenerationId);
            context.Validate();
            var ciphertext = file.AsSpan(sizeof(int) + headerLength).ToArray();
            return (header, ciphertext);
        }
        finally { CryptographicOperations.ZeroMemory(file); }
    }
}
