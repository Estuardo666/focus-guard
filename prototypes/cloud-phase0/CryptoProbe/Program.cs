using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using Org.Webpki.JsonCanonicalizer;

// Interoperability input is UTF-8 bytes, never the host console/OEM code page.
Console.InputEncoding = new UTF8Encoding(false, true);
Console.OutputEncoding = new UTF8Encoding(false, true);

if (args.Contains("--canonicalize", StringComparer.Ordinal))
{
    Console.WriteLine(new JsonCanonicalizer(Console.In.ReadToEnd()).GetEncodedString());
    return;
}

// Public deterministic test material only. Never production keys or credentials.
var rawKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
var seed = Enumerable.Range(33, 32).Select(i => (byte)i).ToArray();
var nonce = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray();
var message = Encoding.UTF8.GetBytes("FocusGuard prueba pública: café y enfoque.");
var aad = Encoding.UTF8.GetBytes("FG-E2EE-1:test-generation:test-entity:1");
var aead = AeadAlgorithm.XChaCha20Poly1305;
using var key = Key.Import(aead, rawKey, KeyBlobFormat.RawSymmetricKey);
var cipher = aead.Encrypt(key, nonce, aad, message);
if (!aead.Decrypt(key, nonce, aad, cipher)!.SequenceEqual(message))
    throw new InvalidOperationException("AEAD roundtrip failed");
var signing = SignatureAlgorithm.Ed25519;
using var signKey = Key.Import(signing, seed, KeyBlobFormat.RawPrivateKey);
var signature = signing.Sign(signKey, message);
if (!signing.Verify(signKey.PublicKey, message, signature))
    throw new InvalidOperationException("Signature verify failed");
var boxPublic = new byte[32];
var boxPrivate = new byte[32];
if (NativeBox.SeedKeyPair(boxPublic, boxPrivate, seed) != 0)
    throw new InvalidOperationException("Sealed box test key generation failed");
var sealedCipher = new byte[message.Length + 48];
if (NativeBox.Seal(sealedCipher, message, (ulong)message.Length, boxPublic) != 0)
    throw new InvalidOperationException("Sealed box encryption failed");
if (args.Contains("--open-sealed", StringComparer.Ordinal))
{
    var encoded = Console.ReadLine() ?? throw new InvalidOperationException("Missing public test ciphertext");
    var supplied = Convert.FromHexString(encoded);
    if (supplied.Length < 48) throw new InvalidOperationException("Invalid sealed ciphertext");
    var opened = new byte[supplied.Length - 48];
    if (NativeBox.Open(opened, supplied, (ulong)supplied.Length, boxPublic, boxPrivate) != 0)
        throw new InvalidOperationException("Sealed test ciphertext invalid");
    Console.WriteLine(Convert.ToHexString(opened));
    CryptographicOperations.ZeroMemory(boxPrivate);
    return;
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    suite = "FG-E2EE-1-content-probe",
    publicTestVector = true,
    symmetricKey = Convert.ToHexString(rawKey),
    signingSeed = Convert.ToHexString(seed),
    nonce = Convert.ToHexString(nonce),
    message = Convert.ToHexString(message),
    aad = Convert.ToHexString(aad),
    ciphertext = Convert.ToHexString(cipher),
    signingPublicKey = Convert.ToHexString(signKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
    signature = Convert.ToHexString(signature),
    sealedPublicKey = Convert.ToHexString(boxPublic),
    sealedCiphertext = Convert.ToHexString(sealedCipher)
}));
CryptographicOperations.ZeroMemory(boxPrivate);

// Prototype-only binding to the documented libsodium C API already supplied by NSec.
// This does not constitute selection/audit of a production .NET sealed-box wrapper.
internal static class NativeBox
{
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seed_keypair")]
    internal static extern int SeedKeyPair(byte[] publicKey, byte[] privateKey, byte[] seed);
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal")]
    internal static extern int Seal(byte[] ciphertext, byte[] message, ulong messageLength, byte[] publicKey);
    [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl, EntryPoint = "crypto_box_seal_open")]
    internal static extern int Open(byte[] message, byte[] ciphertext, ulong ciphertextLength, byte[] publicKey, byte[] privateKey);
}

