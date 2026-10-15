using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace FocusGuard.Devices;

public sealed record RecoveryPublicIdentity(string SigningPublicKey, string EncryptionPublicKey);

/// <summary>Owner-held recovery material. Callers must present/export it locally and zero it after acknowledgement.</summary>
public sealed class RecoveryKit : IDisposable
{
    private readonly byte[] signingSeed;
    private readonly byte[] wrappingSeed;
    private readonly Key signingKey;
    private readonly byte[] encryptionSecret;
    private bool disposed;
    public RecoveryPublicIdentity Public { get; }

    private RecoveryKit(byte[] signingSeed, byte[] wrappingSeed)
    {
        if (signingSeed.Length != 32 || wrappingSeed.Length != 32) throw new ArgumentException("Recovery secrets must each contain 32 bytes.");
        this.signingSeed = signingSeed.ToArray(); this.wrappingSeed = wrappingSeed.ToArray();
        var options = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        signingKey = Key.Import(SignatureAlgorithm.Ed25519, this.signingSeed, KeyBlobFormat.RawPrivateKey, options);
        var box = SodiumBox.FromSeed(this.wrappingSeed);
        encryptionSecret = box.SecretKey;
        Public = new(DeviceEncoding.Encode(signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)), DeviceEncoding.Encode(box.PublicKey));
        CryptographicOperations.ZeroMemory(box.PublicKey);
    }

    public static RecoveryKit Generate()
    {
        var signing = RandomNumberGenerator.GetBytes(32); var wrapping = RandomNumberGenerator.GetBytes(32);
        try { return new RecoveryKit(signing, wrapping); }
        finally { CryptographicOperations.ZeroMemory(signing); CryptographicOperations.ZeroMemory(wrapping); }
    }

    public static RecoveryKit Import(string kit)
    {
        if (string.IsNullOrWhiteSpace(kit) || kit.Length > 160) throw new FormatException("Invalid recovery kit.");
        var parts = kit.Split('.');
        if (parts.Length != 4 || parts[0] != "FG-RK1") throw new FormatException("Invalid recovery kit.");
        var signing = DeviceEncoding.Decode(parts[1], 32); var wrapping = DeviceEncoding.Decode(parts[2], 32);
        var expected = Checksum(signing, wrapping);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[3])))
        {
            CryptographicOperations.ZeroMemory(signing); CryptographicOperations.ZeroMemory(wrapping);
            throw new FormatException("Invalid recovery kit checksum.");
        }
        try { return new RecoveryKit(signing, wrapping); }
        finally { CryptographicOperations.ZeroMemory(signing); CryptographicOperations.ZeroMemory(wrapping); }
    }

    public string Export()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return "FG-RK1." + DeviceEncoding.Encode(signingSeed) + "." + DeviceEncoding.Encode(wrappingSeed) + "." + Checksum(signingSeed, wrappingSeed);
    }

    internal byte[] Sign(string domain, ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return SignatureAlgorithm.Ed25519.Sign(signingKey, DeviceEncoding.DomainBytes(domain, payload));
    }

    internal byte[] Open(ReadOnlySpan<byte> ciphertext) => SodiumBox.Open(ciphertext, Public.EncryptionPublicKey, encryptionSecret);

    private static string Checksum(ReadOnlySpan<byte> signing, ReadOnlySpan<byte> wrapping)
    {
        var bytes = new byte["FG-RK1\0"u8.Length + signing.Length + wrapping.Length];
        try
        {
            "FG-RK1\0"u8.CopyTo(bytes);
            signing.CopyTo(bytes.AsSpan("FG-RK1\0"u8.Length));
            wrapping.CopyTo(bytes.AsSpan("FG-RK1\0"u8.Length + signing.Length));
            return DeviceEncoding.Encode(SHA256.HashData(bytes))[..8];
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; signingKey.Dispose();
        CryptographicOperations.ZeroMemory(signingSeed); CryptographicOperations.ZeroMemory(wrappingSeed);
        CryptographicOperations.ZeroMemory(encryptionSecret);
    }
}
