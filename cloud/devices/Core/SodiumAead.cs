using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FocusGuard.Devices;

/// <summary>Thin binding to libsodium's XChaCha20-Poly1305-IETF content AEAD.</summary>
internal static class SodiumAead
{
    internal const int KeyBytes = 32;
    internal const int NonceBytes = 24;
    internal const int TagBytes = 16;

    private static readonly Lazy<bool> Initialized = new(() => SodiumInit() >= 0,
        LazyThreadSafetyMode.ExecutionAndPublication);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sodium_init")]
    private static extern int SodiumInit();

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "crypto_aead_xchacha20poly1305_ietf_encrypt")]
    private static extern int EncryptNative(byte[] ciphertext, out ulong ciphertextLength,
        byte[] plaintext, ulong plaintextLength, byte[] associatedData, ulong associatedDataLength,
        IntPtr secretNonce, byte[] nonce, byte[] key);

    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "crypto_aead_xchacha20poly1305_ietf_decrypt")]
    private static extern int DecryptNative(byte[] plaintext, out ulong plaintextLength,
        IntPtr secretNonce, byte[] ciphertext, ulong ciphertextLength, byte[] associatedData,
        ulong associatedDataLength, byte[] nonce, byte[] key);

    internal static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> key)
    {
        EnsureInitialized();
        if (plaintext.Length is 0 or > 262_144 || associatedData.Length is 0 or > 16_384 ||
            nonce.Length != NonceBytes || key.Length != KeyBytes)
            throw new ArgumentException("Invalid XChaCha20-Poly1305 input.");

        var message = plaintext.ToArray();
        var aad = associatedData.ToArray();
        var nonceBytes = nonce.ToArray();
        var keyBytes = key.ToArray();
        var ciphertext = new byte[message.Length + TagBytes];
        try
        {
            if (EncryptNative(ciphertext, out var written, message, (ulong)message.Length, aad,
                (ulong)aad.Length, IntPtr.Zero, nonceBytes, keyBytes) != 0 || written != (ulong)ciphertext.Length)
                throw new CryptographicException("Content encryption failed.");
            return ciphertext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(nonceBytes);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    internal static byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> associatedData,
        ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> key)
    {
        EnsureInitialized();
        if (ciphertext.Length is <= TagBytes or > 262_144 + TagBytes || associatedData.Length is 0 or > 16_384 ||
            nonce.Length != NonceBytes || key.Length != KeyBytes)
            throw new CryptographicException("Invalid XChaCha20-Poly1305 input.");

        var cipher = ciphertext.ToArray();
        var aad = associatedData.ToArray();
        var nonceBytes = nonce.ToArray();
        var keyBytes = key.ToArray();
        var plaintext = new byte[cipher.Length - TagBytes];
        try
        {
            if (DecryptNative(plaintext, out var written, IntPtr.Zero, cipher, (ulong)cipher.Length,
                aad, (ulong)aad.Length, nonceBytes, keyBytes) != 0 || written != (ulong)plaintext.Length)
                throw new CryptographicException("Content authentication failed.");
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(aad);
            CryptographicOperations.ZeroMemory(nonceBytes);
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static void EnsureInitialized()
    {
        if (!Initialized.Value) throw new CryptographicException("libsodium initialization failed.");
    }
}
