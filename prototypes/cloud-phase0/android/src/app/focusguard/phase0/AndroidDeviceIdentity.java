package app.focusguard.phase0;

import com.sun.jna.ptr.LongByReference;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import java.util.Locale;
import java.util.UUID;

/** Installation-local Ed25519/X25519 identity. Private seeds are persisted only inside AndroidDeviceVault. */
public final class AndroidDeviceIdentity implements AutoCloseable {
    private static final int VAULT_MAGIC = 0x46474449; // FGDI
    private static final byte VAULT_VERSION = 1;
    private static final int KEY_BYTES = 32;
    private static final int DEVICE_ID_BYTES = 36;
    private static final int VAULT_RECORD_BYTES = Integer.BYTES + 1 + DEVICE_ID_BYTES + KEY_BYTES * 2;
    private static final int SIGNATURE_BYTES = 64;
    private static final int MAX_SIGNED_PAYLOAD_BYTES = 262144;
    private static final AndroidSodium.Sodium SODIUM = AndroidSodium.instance();

    private final Object lock = new Object();
    private final String deviceId;
    private final byte[] signingSeed;
    private final byte[] encryptionSeed;
    private final PublicIdentity publicIdentity;
    private boolean closed;

    private AndroidDeviceIdentity(String deviceId, byte[] signingSeed, byte[] encryptionSeed) {
        requireDeviceId(deviceId);
        if (signingSeed == null || signingSeed.length != KEY_BYTES
                || encryptionSeed == null || encryptionSeed.length != KEY_BYTES)
            throw new IllegalArgumentException("Device identity seeds must be 32 bytes.");
        this.deviceId = deviceId;
        this.signingSeed = signingSeed.clone();
        this.encryptionSeed = encryptionSeed.clone();
        this.publicIdentity = derivePublicIdentity();
    }

    public static AndroidDeviceIdentity create() {
        byte[] signingSeed = new byte[KEY_BYTES];
        byte[] encryptionSeed = new byte[KEY_BYTES];
        SecureRandom random = new SecureRandom();
        random.nextBytes(signingSeed);
        random.nextBytes(encryptionSeed);
        try {
            return new AndroidDeviceIdentity(UUID.randomUUID().toString().toLowerCase(Locale.ROOT),
                    signingSeed, encryptionSeed);
        } finally {
            Arrays.fill(signingSeed, (byte) 0);
            Arrays.fill(encryptionSeed, (byte) 0);
        }
    }

    static AndroidDeviceIdentity fromSeedsForProbe(String deviceId, byte[] signingSeed, byte[] encryptionSeed) {
        return new AndroidDeviceIdentity(deviceId, signingSeed, encryptionSeed);
    }

    static PublicIdentity importPublicIdentity(String deviceId, String signingPublicKey,
            String encryptionPublicKey) throws GeneralSecurityException {
        PublicIdentity identity;
        try {
            identity = new PublicIdentity(deviceId, signingPublicKey, encryptionPublicKey,
                    fingerprint(deviceId, signingPublicKey, encryptionPublicKey));
        } catch (IllegalArgumentException | IllegalStateException exception) {
            throw new GeneralSecurityException("Public device identity rejected.", exception);
        }
        identity.validate();
        return identity;
    }

    /** Reuses the account-bound vault; a missing identity is created once and then encrypted at rest. */
    public static AndroidDeviceIdentity loadOrCreate(AndroidDeviceVault vault, String accountBinding)
            throws GeneralSecurityException, IOException {
        if (vault.exists(accountBinding)) {
            byte[] clear = vault.load(accountBinding);
            try { return deserialize(clear); }
            finally { Arrays.fill(clear, (byte) 0); }
        }

        AndroidDeviceIdentity identity = create();
        byte[] clear = null;
        try {
            clear = identity.serializeForVault();
            vault.save(accountBinding, clear);
            return identity;
        } catch (GeneralSecurityException | IOException | RuntimeException exception) {
            identity.close();
            throw exception;
        } finally {
            if (clear != null) Arrays.fill(clear, (byte) 0);
        }
    }

    public String deviceId() { return deviceId; }
    public PublicIdentity publicIdentity() { return publicIdentity; }

    public String sign(String domain, byte[] canonicalPayload) {
        byte[] message = domainBytes(domain, canonicalPayload);
        byte[] secretKey = new byte[64];
        byte[] publicKey = new byte[KEY_BYTES];
        byte[] signature = new byte[SIGNATURE_BYTES];
        try {
            synchronized (lock) {
                requireOpen();
                requireNativeSuccess(SODIUM.crypto_sign_seed_keypair(publicKey, secretKey, signingSeed), "signing key derivation");
                LongByReference signatureLength = new LongByReference();
                requireNativeSuccess(SODIUM.crypto_sign_detached(signature, signatureLength,
                        message, message.length, secretKey), "signature generation");
                if (signatureLength.getValue() != SIGNATURE_BYTES)
                    throw new IllegalStateException("Unexpected Ed25519 signature length.");
            }
            return encode(signature);
        } finally {
            Arrays.fill(message, (byte) 0);
            Arrays.fill(secretKey, (byte) 0);
            Arrays.fill(publicKey, (byte) 0);
            Arrays.fill(signature, (byte) 0);
        }
    }

    public static boolean verify(PublicIdentity identity, String domain, byte[] canonicalPayload, String signatureText) {
        if (identity == null || canonicalPayload == null || signatureText == null) return false;
        byte[] message = null;
        byte[] signature = null;
        byte[] publicKey = null;
        try {
            identity.validate();
            message = domainBytes(domain, canonicalPayload);
            signature = decode(signatureText, SIGNATURE_BYTES);
            publicKey = decode(identity.signingPublicKey(), KEY_BYTES);
            return SODIUM.crypto_sign_verify_detached(signature, message, message.length, publicKey) == 0;
        } catch (IllegalArgumentException | GeneralSecurityException exception) {
            return false;
        } finally {
            if (message != null) Arrays.fill(message, (byte) 0);
            if (signature != null) Arrays.fill(signature, (byte) 0);
            if (publicKey != null) Arrays.fill(publicKey, (byte) 0);
        }
    }

    byte[] openSealed(byte[] ciphertext, String expectedEncryptionPublicKey) throws GeneralSecurityException {
        if (ciphertext == null || ciphertext.length < 48 || ciphertext.length > 6144)
            throw new GeneralSecurityException("Sealed-box size rejected.");
        byte[] publicKey = new byte[KEY_BYTES];
        byte[] secretKey = new byte[KEY_BYTES];
        byte[] clear = new byte[ciphertext.length - 48];
        try {
            synchronized (lock) {
                requireOpen();
                requireNativeSuccess(SODIUM.crypto_box_seed_keypair(publicKey, secretKey, encryptionSeed),
                        "encryption key derivation");
                if (!encode(publicKey).equals(expectedEncryptionPublicKey))
                    throw new GeneralSecurityException("Local recipient public key mismatch.");
                if (SODIUM.crypto_box_seal_open(clear, ciphertext, ciphertext.length, publicKey, secretKey) != 0)
                    throw new GeneralSecurityException("Sealed-box authentication failed.");
                return clear;
            }
        } catch (RuntimeException | GeneralSecurityException exception) {
            Arrays.fill(clear, (byte) 0);
            throw exception;
        } finally {
            Arrays.fill(publicKey, (byte) 0);
            Arrays.fill(secretKey, (byte) 0);
        }
    }

    byte[] serializeForVault() {
        synchronized (lock) {
            requireOpen();
            byte[] id = deviceId.getBytes(StandardCharsets.US_ASCII);
            try {
                if (id.length != DEVICE_ID_BYTES) throw new IllegalStateException("Device ID serialization failed.");
                ByteBuffer record = ByteBuffer.allocate(VAULT_RECORD_BYTES);
                record.putInt(VAULT_MAGIC).put(VAULT_VERSION).put(id).put(signingSeed).put(encryptionSeed);
                return record.array();
            } finally { Arrays.fill(id, (byte) 0); }
        }
    }

    private static AndroidDeviceIdentity deserialize(byte[] clear) throws GeneralSecurityException {
        if (clear == null || clear.length != VAULT_RECORD_BYTES)
            throw new GeneralSecurityException("Stored device identity size rejected.");
        ByteBuffer record = ByteBuffer.wrap(clear);
        if (record.getInt() != VAULT_MAGIC || record.get() != VAULT_VERSION)
            throw new GeneralSecurityException("Stored device identity version rejected.");
        byte[] idBytes = new byte[DEVICE_ID_BYTES];
        byte[] signingSeed = new byte[KEY_BYTES];
        byte[] encryptionSeed = new byte[KEY_BYTES];
        try {
            record.get(idBytes).get(signingSeed).get(encryptionSeed);
            String id = new String(idBytes, StandardCharsets.US_ASCII);
            try { return new AndroidDeviceIdentity(id, signingSeed, encryptionSeed); }
            catch (IllegalArgumentException exception) {
                throw new GeneralSecurityException("Stored device identity material rejected.", exception);
            }
        } finally {
            Arrays.fill(idBytes, (byte) 0);
            Arrays.fill(signingSeed, (byte) 0);
            Arrays.fill(encryptionSeed, (byte) 0);
        }
    }

    private PublicIdentity derivePublicIdentity() {
        byte[] signingPublic = new byte[KEY_BYTES];
        byte[] signingSecret = new byte[64];
        byte[] encryptionPublic = new byte[KEY_BYTES];
        byte[] encryptionSecret = new byte[KEY_BYTES];
        try {
            requireNativeSuccess(SODIUM.crypto_sign_seed_keypair(signingPublic, signingSecret, signingSeed), "signing public-key derivation");
            requireNativeSuccess(SODIUM.crypto_box_seed_keypair(encryptionPublic, encryptionSecret, encryptionSeed), "encryption public-key derivation");
            String signingText = encode(signingPublic);
            String encryptionText = encode(encryptionPublic);
            String fingerprint = fingerprint(deviceId, signingText, encryptionText);
            return new PublicIdentity(deviceId, signingText, encryptionText, fingerprint);
        } finally {
            Arrays.fill(signingPublic, (byte) 0);
            Arrays.fill(signingSecret, (byte) 0);
            Arrays.fill(encryptionPublic, (byte) 0);
            Arrays.fill(encryptionSecret, (byte) 0);
        }
    }

    private static String fingerprint(String id, String signingKey, String encryptionKey) {
        String json = "{\"deviceId\":\"" + id + "\",\"signingPublicKey\":\"" + signingKey
                + "\",\"encryptionPublicKey\":\"" + encryptionKey + "\"}";
        byte[] canonical = null;
        byte[] domain = null;
        byte[] digest = null;
        try {
            canonical = new JsonCanonicalizer(json).getEncodedUTF8();
            domain = domainBytes("FG-DEVICE-1", canonical);
            digest = MessageDigest.getInstance("SHA-256").digest(domain);
            return encode(digest);
        } catch (IOException | GeneralSecurityException exception) {
            throw new IllegalStateException("Device fingerprint calculation failed.", exception);
        } finally {
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
            if (domain != null) Arrays.fill(domain, (byte) 0);
            if (digest != null) Arrays.fill(digest, (byte) 0);
        }
    }

    private static byte[] domainBytes(String domain, byte[] payload) {
        if (domain == null || domain.length() < 3 || domain.length() > 64
                || !domain.matches("[A-Z0-9-]+")) throw new IllegalArgumentException("Protocol domain rejected.");
        if (payload == null || payload.length > MAX_SIGNED_PAYLOAD_BYTES)
            throw new IllegalArgumentException("Signed payload size rejected.");
        byte[] prefix = (domain + "\0").getBytes(StandardCharsets.US_ASCII);
        byte[] result = Arrays.copyOf(prefix, prefix.length + payload.length);
        System.arraycopy(payload, 0, result, prefix.length, payload.length);
        Arrays.fill(prefix, (byte) 0);
        return result;
    }

    private static void requireDeviceId(String id) {
        if (id == null || !id.matches("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
                ) || "00000000-0000-0000-0000-000000000000".equals(id))
            throw new IllegalArgumentException("Canonical lowercase UUID required.");
    }

    private static void requireNativeSuccess(int result, String operation) {
        if (result != 0) throw new IllegalStateException("libsodium " + operation + " failed.");
    }

    private void requireOpen() {
        if (closed) throw new IllegalStateException("Device identity is closed.");
    }

    static String encode(byte[] bytes) {
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static byte[] decode(String encoded, int length) throws GeneralSecurityException {
        int encodedLength = (length * 8 + 5) / 6;
        if (encoded == null || !encoded.matches("[A-Za-z0-9_-]{" + encodedLength + "}"))
            throw new GeneralSecurityException("Canonical base64url rejected.");
        try {
            byte[] bytes = Base64.getUrlDecoder().decode(encoded);
            if (bytes.length != length || !encode(bytes).equals(encoded)) {
                Arrays.fill(bytes, (byte) 0);
                throw new GeneralSecurityException("Encoded key length rejected.");
            }
            return bytes;
        } catch (IllegalArgumentException exception) {
            throw new GeneralSecurityException("Canonical base64url rejected.", exception);
        }
    }

    @Override public void close() {
        synchronized (lock) {
            if (closed) return;
            closed = true;
            Arrays.fill(signingSeed, (byte) 0);
            Arrays.fill(encryptionSeed, (byte) 0);
        }
    }

    public static final class PublicIdentity {
        private final String deviceId;
        private final String signingPublicKey;
        private final String encryptionPublicKey;
        private final String fingerprint;

        PublicIdentity(String deviceId, String signingPublicKey, String encryptionPublicKey, String fingerprint) {
            this.deviceId = deviceId;
            this.signingPublicKey = signingPublicKey;
            this.encryptionPublicKey = encryptionPublicKey;
            this.fingerprint = fingerprint;
        }

        public String deviceId() { return deviceId; }
        public String signingPublicKey() { return signingPublicKey; }
        public String encryptionPublicKey() { return encryptionPublicKey; }
        public String fingerprint() { return fingerprint; }

        public void validate() throws GeneralSecurityException {
            try { requireDeviceId(deviceId); }
            catch (IllegalArgumentException exception) {
                throw new GeneralSecurityException("Device identity identifier rejected.", exception);
            }
            byte[] signing = null;
            byte[] encryption = null;
            try {
                signing = decode(signingPublicKey, KEY_BYTES);
                encryption = decode(encryptionPublicKey, KEY_BYTES);
                if (!AndroidDeviceIdentity.fingerprint(deviceId, signingPublicKey, encryptionPublicKey).equals(fingerprint))
                    throw new GeneralSecurityException("Device identity fingerprint rejected.");
            } finally {
                if (signing != null) Arrays.fill(signing, (byte) 0);
                if (encryption != null) Arrays.fill(encryption, (byte) 0);
            }
        }
    }
}
