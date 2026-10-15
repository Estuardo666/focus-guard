using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FocusGuard.Devices;

internal static class SodiumBox
{
    private const int PublicKeyBytes = 32;
    private const int SecretKeyBytes = 32;
    private const int SealOverheadBytes = 48;

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seed_keypair")]
    private static extern int SeedKeyPair(byte[] publicKey, byte[] secretKey, byte[] seed);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal")]
    private static extern int SealNative(byte[] ciphertext, byte[] message, ulong messageLength, byte[] recipientPublicKey);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal_open")]
    private static extern int OpenNative(byte[] message, byte[] ciphertext, ulong ciphertextLength,
        byte[] recipientPublicKey, byte[] recipientSecretKey);

    internal static (byte[] PublicKey, byte[] SecretKey) FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32) throw new ArgumentException("Box seed must be 32 bytes.");
        var publicKey = new byte[PublicKeyBytes]; var secretKey = new byte[SecretKeyBytes]; var seedBytes = seed.ToArray();
        try
        {
            if (SeedKeyPair(publicKey, secretKey, seedBytes) != 0) throw new CryptographicException("Box key derivation failed.");
            return (publicKey, secretKey);
        }
        catch { CryptographicOperations.ZeroMemory(secretKey); throw; }
        finally { CryptographicOperations.ZeroMemory(seedBytes); }
    }

    internal static byte[] Seal(ReadOnlySpan<byte> message, string recipientPublicKey)
    {
        if (message.Length is 0 or > 6000) throw new ArgumentException("Invalid sealed-box payload size.");
        var recipient = DeviceEncoding.Decode(recipientPublicKey, PublicKeyBytes);
        var clear = message.ToArray(); var cipher = new byte[clear.Length + SealOverheadBytes];
        try
        {
            if (SealNative(cipher, clear, (ulong)clear.Length, recipient) != 0)
                throw new CryptographicException("Sealed-box encryption failed.");
            return cipher;
        }
        catch { CryptographicOperations.ZeroMemory(cipher); throw; }
        finally { CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(recipient); }
    }

    internal static byte[] Open(ReadOnlySpan<byte> ciphertext, string recipientPublicKey, ReadOnlySpan<byte> recipientSecretKey)
    {
        if (ciphertext.Length is < SealOverheadBytes or > 6144 || recipientSecretKey.Length != SecretKeyBytes)
            throw new CryptographicException("Invalid sealed-box input.");
        var recipient = DeviceEncoding.Decode(recipientPublicKey, PublicKeyBytes);
        var cipher = ciphertext.ToArray(); var secret = recipientSecretKey.ToArray();
        var clear = new byte[cipher.Length - SealOverheadBytes];
        try
        {
            if (OpenNative(clear, cipher, (ulong)cipher.Length, recipient, secret) != 0)
                throw new CryptographicException("Sealed-box authentication failed.");
            return clear;
        }
        catch { CryptographicOperations.ZeroMemory(clear); throw; }
        finally { CryptographicOperations.ZeroMemory(cipher); CryptographicOperations.ZeroMemory(secret); CryptographicOperations.ZeroMemory(recipient); }
    }
}
