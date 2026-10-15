package app.focusguard.phase0;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.ptr.LongByReference;

/** Pinned libsodium surface for the isolated Android capability probe. */
final class AndroidSodium {
    interface Sodium extends Library {
        int sodium_init();
        Pointer sodium_version_string();
        int crypto_sign_seed_keypair(byte[] publicKey, byte[] secretKey, byte[] seed);
        int crypto_sign_detached(byte[] signature, LongByReference signatureLength,
                byte[] message, long messageLength, byte[] secretKey);
        int crypto_sign_verify_detached(byte[] signature, byte[] message, long messageLength, byte[] publicKey);
        int crypto_box_seed_keypair(byte[] publicKey, byte[] secretKey, byte[] seed);
        int crypto_box_seal(byte[] ciphertext, byte[] message, long messageLength, byte[] recipientPublicKey);
        int crypto_box_seal_open(byte[] message, byte[] ciphertext, long ciphertextLength,
                byte[] recipientPublicKey, byte[] recipientSecretKey);
        int crypto_aead_xchacha20poly1305_ietf_encrypt(byte[] ciphertext, LongByReference ciphertextLength,
                byte[] plaintext, long plaintextLength, byte[] aad, long aadLength, Pointer nsec,
                byte[] nonce, byte[] key);
        int crypto_aead_xchacha20poly1305_ietf_decrypt(byte[] plaintext, LongByReference plaintextLength,
                Pointer nsec, byte[] ciphertext, long ciphertextLength, byte[] aad, long aadLength,
                byte[] nonce, byte[] key);
    }

    private static final Sodium INSTANCE = load();

    private AndroidSodium() { }

    static Sodium instance() { return INSTANCE; }

    private static Sodium load() {
        Sodium sodium = Native.load("sodium", Sodium.class);
        if (sodium.sodium_init() < 0) throw new IllegalStateException("libsodium initialization failed.");
        String version = sodium.sodium_version_string().getString(0, "UTF-8");
        if (!"1.0.22".equals(version)) throw new IllegalStateException("Unexpected libsodium version.");
        return sodium;
    }
}
