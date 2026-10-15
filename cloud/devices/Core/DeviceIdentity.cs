using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Org.Webpki.JsonCanonicalizer;

namespace FocusGuard.Devices;

public sealed record DevicePublicIdentity(string DeviceId, string SigningPublicKey, string EncryptionPublicKey)
{
    public string Fingerprint
    {
        get
        {
            Validate();
            return DeviceEncoding.Encode(SHA256.HashData(DeviceEncoding.DomainBytes(
                "FG-DEVICE-1", DeviceEncoding.Canonical(new { deviceId = DeviceId,
                    signingPublicKey = SigningPublicKey, encryptionPublicKey = EncryptionPublicKey }))));
        }
    }

    public void Validate()
    {
        DeviceEncoding.RequireId(DeviceId);
        _ = DeviceEncoding.Decode(SigningPublicKey, 32);
        _ = DeviceEncoding.Decode(EncryptionPublicKey, 32);
    }
}

public sealed record DeviceAccountContext(string Issuer, string AccountBinding, string AccountId, string GenerationId)
{
    public void Validate()
    {
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Exact HTTPS issuer required.");
        _ = DeviceEncoding.Decode(AccountBinding, 32);
        DeviceEncoding.RequireId(AccountId);
        DeviceEncoding.RequireId(GenerationId);
    }
}

/// <summary>Local keys only. Creating this object grants no cloud membership.</summary>
public sealed class DeviceIdentity : IDisposable
{
    private readonly Key signing;
    private readonly Key encryption;
    private readonly object sync = new();
    private bool disposed;
    public DevicePublicIdentity Public { get; }

    private DeviceIdentity(string deviceId, Key signing, Key encryption)
    {
        DeviceEncoding.RequireId(deviceId);
        this.signing = signing;
        this.encryption = encryption;
        Public = new(deviceId, DeviceEncoding.Encode(signing.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            DeviceEncoding.Encode(encryption.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
    }

    public static DeviceIdentity Create()
    {
        var options = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        var signing = Key.Create(SignatureAlgorithm.Ed25519, options);
        try { return new(Guid.NewGuid().ToString("D"), signing, Key.Create(KeyAgreementAlgorithm.X25519, options)); }
        catch { signing.Dispose(); throw; }
    }

    internal static DeviceIdentity ImportForInterop(string deviceId, ReadOnlySpan<byte> signingSeed,
        ReadOnlySpan<byte> encryptionSecret)
    {
        if (signingSeed.Length != 32 || encryptionSecret.Length != 32)
            throw new ArgumentException("Private keys must be 32 bytes.");
        var options = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        var signing = Key.Import(SignatureAlgorithm.Ed25519, signingSeed, KeyBlobFormat.RawPrivateKey, options);
        try { return new(deviceId, signing, Key.Import(KeyAgreementAlgorithm.X25519, encryptionSecret, KeyBlobFormat.RawPrivateKey, options)); }
        catch { signing.Dispose(); throw; }
    }

    // Domains are chosen by protocol code, never supplied by a remote request.
    public string Sign(string domain, ReadOnlySpan<byte> canonicalPayload)
    {
        var bytes = DeviceEncoding.DomainBytes(domain, canonicalPayload);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return DeviceEncoding.Encode(SignatureAlgorithm.Ed25519.Sign(signing, bytes));
        }
    }

    public static bool Verify(DevicePublicIdentity identity, string domain,
        ReadOnlySpan<byte> canonicalPayload, string signature)
    {
        try
        {
            identity.Validate();
            var key = PublicKey.Import(SignatureAlgorithm.Ed25519,
                DeviceEncoding.Decode(identity.SigningPublicKey, 32), KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(key, DeviceEncoding.DomainBytes(domain, canonicalPayload),
                DeviceEncoding.Decode(signature, 64));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return false; }
    }

    internal byte[] SealTo(string encryptionPublicKey, ReadOnlySpan<byte> clear)
    {
        lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); return SodiumBox.Seal(clear, encryptionPublicKey); }
    }

    internal byte[] OpenSealed(ReadOnlySpan<byte> ciphertext)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var secret = encryption.Export(KeyBlobFormat.RawPrivateKey);
            try { return SodiumBox.Open(ciphertext, Public.EncryptionPublicKey, secret); }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
    }

    internal byte[] SerializeForVault(DeviceAccountContext context, RecoveryKit? recoveryKit,
        ReadOnlySpan<byte> accountContentKey, long keyEpoch, IReadOnlyDictionary<long, byte[]>? historicalContentKeys,
        long membershipRevision, string membershipDigest, string? deviceSession, string? pendingPairingId)
    {
        context.Validate();
        if (accountContentKey.Length != 32 || keyEpoch < 1 || membershipRevision < 1)
            throw new ArgumentException("Invalid account vault material.");
        var history = historicalContentKeys ?? new Dictionary<long, byte[]>();
        if (history.Count > 64 || history.Any(item => item.Key < 1 || item.Key >= keyEpoch || item.Value is null || item.Value.Length != 32))
            throw new ArgumentException("Invalid account content-key history.");
        _ = DeviceEncoding.Decode(membershipDigest, 32);
        if (deviceSession is not null)
        {
            var sessionBytes = DeviceEncoding.Decode(deviceSession, 32);
            CryptographicOperations.ZeroMemory(sessionBytes);
        }
        if (pendingPairingId is not null) DeviceEncoding.RequireId(pendingPairingId);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var signingSeed = signing.Export(KeyBlobFormat.RawPrivateKey);
            var encryptionSecret = encryption.Export(KeyBlobFormat.RawPrivateKey);
            try
            {
                using var stream = new MemoryStream();
                try
                {
                    using (var writer = new Utf8JsonWriter(stream))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("protocol", "FG-DEVICE-VAULT-1");
                        writer.WriteString("deviceId", Public.DeviceId);
                        writer.WriteString("issuer", context.Issuer);
                        writer.WriteString("accountBinding", context.AccountBinding);
                        writer.WriteString("accountId", context.AccountId);
                        writer.WriteString("generationId", context.GenerationId);
                        writer.WriteNumber("membershipRevision", membershipRevision);
                        writer.WriteString("membershipDigest", membershipDigest);
                        writer.WriteNumber("keyEpoch", keyEpoch);
                        writer.WriteStartArray("historicalContentKeys");
                        foreach (var historicalKey in history.OrderBy(item => item.Key))
                        {
                            writer.WriteStartObject();
                            writer.WriteNumber("keyEpoch", historicalKey.Key);
                            writer.WriteBase64String("contentKey", historicalKey.Value);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        if (deviceSession is null) writer.WriteNull("deviceSession");
                        else writer.WriteString("deviceSession", deviceSession);
                        if (pendingPairingId is null) writer.WriteNull("pendingPairingId");
                        else writer.WriteString("pendingPairingId", pendingPairingId);
                        if (recoveryKit is null) writer.WriteNull("recoveryKit");
                        else writer.WriteString("recoveryKit", recoveryKit.Export());
                        writer.WriteBase64String("signingSeed", signingSeed);
                        writer.WriteBase64String("encryptionSecret", encryptionSecret);
                        writer.WriteBase64String("accountContentKey", accountContentKey);
                        writer.WriteEndObject();
                    }
                    return stream.ToArray();
                }
                finally { CryptographicOperations.ZeroMemory(stream.GetBuffer().AsSpan()); }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signingSeed);
                CryptographicOperations.ZeroMemory(encryptionSecret);
            }
        }
    }

    internal static RestoredDeviceMaterial DeserializeVault(byte[] plaintext, DeviceAccountContext expected, string deviceId)
    {
        expected.Validate();
        DeviceEncoding.RequireId(deviceId);
        using var json = JsonDocument.Parse(plaintext, new JsonDocumentOptions { MaxDepth = 4 });
        var root = json.RootElement;
        var fields = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid device vault.");
        foreach (var field in root.EnumerateObject())
            if (!fields.Add(field.Name)) throw new InvalidDataException("Duplicate device vault field.");
        string[] legacyFields = ["protocol", "deviceId", "issuer", "accountBinding", "accountId", "generationId", "membershipRevision", "membershipDigest", "recoveryKit", "signingSeed", "encryptionSecret", "accountContentKey"];
        var legacy = fields.SetEquals(legacyFields);
        var intermediate = fields.SetEquals(legacyFields.Concat(["deviceSession", "pendingPairingId"]));
        var current = fields.SetEquals(legacyFields.Concat(["deviceSession", "pendingPairingId", "keyEpoch", "historicalContentKeys"]));
        if (!(legacy || intermediate || current) ||
            root.GetProperty("protocol").GetString() != "FG-DEVICE-VAULT-1" ||
            root.GetProperty("deviceId").GetString() != deviceId ||
            root.GetProperty("issuer").GetString() != expected.Issuer ||
            root.GetProperty("accountBinding").GetString() != expected.AccountBinding ||
            root.GetProperty("accountId").GetString() != expected.AccountId ||
            root.GetProperty("generationId").GetString() != expected.GenerationId)
            throw new InvalidDataException("Device vault context mismatch.");
        var membershipRevision = root.GetProperty("membershipRevision").GetInt64();
        var membershipDigest = root.GetProperty("membershipDigest").GetString() ?? throw new InvalidDataException("Membership digest missing.");
        var keyEpoch = current ? root.GetProperty("keyEpoch").GetInt64() : 1;
        if (keyEpoch < 1) throw new InvalidDataException("Invalid content key epoch.");
        var historicalContentKeys = new Dictionary<long, byte[]>();
        var historyArray = current ? root.GetProperty("historicalContentKeys") : default;
        if (current && (historyArray.ValueKind != JsonValueKind.Array || historyArray.GetArrayLength() > 64))
            throw new InvalidDataException("Invalid content key history.");
        try
        {
            foreach (var entry in current ? historyArray.EnumerateArray().ToArray() : [])
            {
                var entryFields = new HashSet<string>(StringComparer.Ordinal);
                if (entry.ValueKind != JsonValueKind.Object ||
                    entry.EnumerateObject().Any(property => !entryFields.Add(property.Name)) ||
                    !entryFields.SetEquals(["keyEpoch", "contentKey"]) ||
                    !entry.GetProperty("keyEpoch").TryGetInt64(out var historicalEpoch) ||
                    entry.GetProperty("contentKey").ValueKind != JsonValueKind.String ||
                    historicalEpoch < 1 || historicalEpoch >= keyEpoch)
                    throw new InvalidDataException("Invalid historical content key entry.");
                var historicalKey = entry.GetProperty("contentKey").GetBytesFromBase64();
                if (historicalKey.Length != 32 || !historicalContentKeys.TryAdd(historicalEpoch, historicalKey))
                {
                    CryptographicOperations.ZeroMemory(historicalKey);
                    throw new InvalidDataException("Invalid or duplicate historical content key.");
                }
            }
        }
        catch
        {
            foreach (var historicalKey in historicalContentKeys.Values) CryptographicOperations.ZeroMemory(historicalKey);
            throw;
        }
        var sessionElement = legacy ? default : root.GetProperty("deviceSession");
        var deviceSession = legacy || sessionElement.ValueKind == JsonValueKind.Null ? null : sessionElement.GetString();
        if (!legacy && sessionElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new InvalidDataException("Invalid device session.");
        if (deviceSession is not null)
        {
            var sessionBytes = DeviceEncoding.Decode(deviceSession, 32);
            CryptographicOperations.ZeroMemory(sessionBytes);
        }
        var pendingPairingElement = legacy ? default : root.GetProperty("pendingPairingId");
        var pendingPairingId = legacy || pendingPairingElement.ValueKind == JsonValueKind.Null ? null : pendingPairingElement.GetString();
        if (!legacy && pendingPairingElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new InvalidDataException("Invalid pending pairing identifier.");
        if (pendingPairingId is not null) DeviceEncoding.RequireId(pendingPairingId);
        _ = DeviceEncoding.Decode(membershipDigest, 32);
        if (membershipRevision < 1) throw new InvalidDataException("Invalid membership revision.");
        var recoveryElement = root.GetProperty("recoveryKit");
        var recoveryText = recoveryElement.ValueKind == JsonValueKind.Null ? null : recoveryElement.GetString();
        if (recoveryElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new InvalidDataException("Invalid recovery kit field.");
        var signingSeed = root.GetProperty("signingSeed").GetBytesFromBase64();
        byte[]? encryptionSecret = null; byte[]? accountContentKey = null;
        RecoveryKit? recoveryKit = null;
        try
        {
            recoveryKit = recoveryText is null ? null : RecoveryKit.Import(recoveryText);
            encryptionSecret = root.GetProperty("encryptionSecret").GetBytesFromBase64();
            accountContentKey = root.GetProperty("accountContentKey").GetBytesFromBase64();
            if (signingSeed.Length != 32 || encryptionSecret.Length != 32 || accountContentKey.Length != 32) throw new InvalidDataException("Invalid private key size.");
            var options = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            var signing = Key.Import(SignatureAlgorithm.Ed25519, signingSeed, KeyBlobFormat.RawPrivateKey, options);
            try
            {
                var identity = new DeviceIdentity(deviceId, signing, Key.Import(KeyAgreementAlgorithm.X25519, encryptionSecret, KeyBlobFormat.RawPrivateKey, options));
                var content = accountContentKey;
                accountContentKey = null;
                var restored = new RestoredDeviceMaterial(identity, content, membershipRevision, membershipDigest,
                    recoveryKit, deviceSession, pendingPairingId, keyEpoch, historicalContentKeys);
                recoveryKit = null;
                historicalContentKeys = [];
                return restored;
            }
            catch { signing.Dispose(); throw; }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingSeed);
            if (encryptionSecret is not null) CryptographicOperations.ZeroMemory(encryptionSecret);
            if (accountContentKey is not null) CryptographicOperations.ZeroMemory(accountContentKey);
            foreach (var historicalKey in historicalContentKeys.Values) CryptographicOperations.ZeroMemory(historicalKey);
            recoveryKit?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            signing.Dispose(); encryption.Dispose();
        }
    }
}

public sealed class RestoredDeviceMaterial(DeviceIdentity identity, byte[] accountContentKey,
    long membershipRevision, string membershipDigest, RecoveryKit? recoveryKit, string? deviceSession,
    string? pendingPairingId, long keyEpoch, Dictionary<long, byte[]> historicalContentKeys) : IDisposable
{
    public DeviceIdentity Identity { get; } = identity;
    public long MembershipRevision { get; } = membershipRevision;
    public string MembershipDigest { get; } = membershipDigest;
    public RecoveryKit? RecoveryKit { get; } = recoveryKit;
    public string? DeviceSession { get; } = deviceSession;
    public string? PendingPairingId { get; } = pendingPairingId;
    public long KeyEpoch { get; } = keyEpoch;
    public ReadOnlyMemory<byte> AccountContentKey => accountContentKey;
    public ReadOnlyMemory<byte> GetContentKey(long epoch) => epoch == KeyEpoch
        ? accountContentKey
        : historicalContentKeys.TryGetValue(epoch, out var key)
            ? key : throw new KeyNotFoundException("This device has no key for the requested content epoch.");
    internal Dictionary<long, byte[]> CopyHistoricalContentKeys() => historicalContentKeys.ToDictionary(
        item => item.Key, item => item.Value.ToArray());
    public void Dispose()
    {
        Identity.Dispose();
        RecoveryKit?.Dispose();
        CryptographicOperations.ZeroMemory(accountContentKey);
        foreach (var key in historicalContentKeys.Values) CryptographicOperations.ZeroMemory(key);
    }
}

public static class DeviceEncoding
{
    public static byte[] Canonical<T>(T value) =>
        new JsonCanonicalizer(JsonSerializer.Serialize(value)).GetEncodedUTF8();

    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value, int length)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new FormatException("Invalid canonical base64url.");
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (bytes.Length != length || Encode(bytes) != value) throw new FormatException("Invalid encoding or length.");
        return bytes;
    }

    public static byte[] DecodeVariable(string value, int minLength, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength * 2 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new FormatException("Invalid canonical base64url.");
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (bytes.Length < minLength || bytes.Length > maxLength || Encode(bytes) != value)
            throw new FormatException("Invalid encoded size.");
        return bytes;
    }

    public static void RequireId(string id)
    {
        if (!Guid.TryParseExact(id, "D", out var value) || value == Guid.Empty || value.ToString("D") != id)
            throw new ArgumentException("Lowercase nonempty UUID required.");
    }

    internal static byte[] DomainBytes(string domain, ReadOnlySpan<byte> payload)
    {
        if (domain.Length is < 3 or > 64 || domain.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '-'))
            throw new ArgumentException("Fixed ASCII protocol domain required.");
        if (payload.Length > 262144) throw new ArgumentException("Signed payload too large.");
        var prefix = Encoding.ASCII.GetBytes(domain);
        var bytes = new byte[prefix.Length + 1 + payload.Length];
        prefix.CopyTo(bytes, 0);
        payload.CopyTo(bytes.AsSpan(prefix.Length + 1));
        return bytes;
    }
}
