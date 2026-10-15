package app.focusguard.phase0;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyInfo;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.Key;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;

/** App-private, no-backup envelope for local device material. Not an identity implementation. */
public final class AndroidDeviceVault {
    private static final int MAGIC = 0x46474156; // FGAV
    private static final int VERSION = 1;
    private static final int NONCE_BYTES = 12;
    private static final int TAG_BITS = 128;
    private static final int TAG_BYTES = TAG_BITS / 8;
    private static final int MAX_CLEAR_BYTES = 64 * 1024;
    private static final int HEADER_BYTES = Integer.BYTES + 1 + 1 + Integer.BYTES;
    private static final byte[] AAD_DOMAIN = "FocusGuard.AndroidDeviceVault.v1\0".getBytes(StandardCharsets.UTF_8);

    private final Context context;

    public AndroidDeviceVault(Context context) {
        this.context = context.getApplicationContext();
    }

    public boolean exists(String accountBinding) throws GeneralSecurityException {
        return vaultFile(accountBinding).exists();
    }

    public void save(String accountBinding, byte[] cleartext) throws GeneralSecurityException, IOException {
        if (cleartext == null || cleartext.length == 0 || cleartext.length > MAX_CLEAR_BYTES) {
            throw new IllegalArgumentException("Vault payload size rejected.");
        }
        SecretKey key = keyFor(accountBinding, true);
        byte[] envelope = encrypt(cleartext, accountBinding, key);
        AtomicFile file = new AtomicFile(vaultFile(accountBinding));
        FileOutputStream output = null;
        try {
            output = file.startWrite();
            output.write(envelope);
            output.getFD().sync();
            file.finishWrite(output);
            output = null;
        } catch (IOException | RuntimeException exception) {
            if (output != null) file.failWrite(output);
            throw exception;
        } finally {
            Arrays.fill(envelope, (byte) 0);
        }
    }

    public byte[] load(String accountBinding) throws GeneralSecurityException, IOException {
        File base = vaultFile(accountBinding);
        if (!base.isFile()) throw new IOException("Vault file is missing.");
        byte[] envelope = readBounded(new AtomicFile(base));
        try {
            return decrypt(envelope, accountBinding, keyFor(accountBinding, false));
        } finally {
            Arrays.fill(envelope, (byte) 0);
        }
    }

    /** Deletes one local account vault and its Android Keystore key. */
    public void clear(String accountBinding) throws GeneralSecurityException, IOException {
        File file = vaultFile(accountBinding);
        String alias = keyAlias(accountBinding);
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        try {
            store.load(null);
            if (store.containsAlias(alias)) store.deleteEntry(alias);
        } catch (IOException exception) {
            throw new GeneralSecurityException("Android Keystore unavailable.", exception);
        }
        new AtomicFile(file).delete();
    }

    @SuppressWarnings("deprecation")
    public boolean isHardwareBacked(String accountBinding) throws GeneralSecurityException {
        SecretKey key = keyFor(accountBinding, false);
        SecretKeyFactory factory = SecretKeyFactory.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        KeyInfo info = (KeyInfo) factory.getKeySpec(key, KeyInfo.class);
        return info.isInsideSecureHardware();
    }

    File fileForProbe(String accountBinding) throws GeneralSecurityException {
        return vaultFile(accountBinding);
    }

    byte[] readEnvelopeForProbe(String accountBinding) throws IOException, GeneralSecurityException {
        return readBounded(new AtomicFile(vaultFile(accountBinding)));
    }

    SecretKey keyForProbe(String accountBinding) throws GeneralSecurityException {
        return keyFor(accountBinding, false);
    }

    boolean hasKeyForProbe(String accountBinding) throws GeneralSecurityException {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        try {
            store.load(null);
            return store.containsAlias(keyAlias(accountBinding));
        } catch (IOException exception) {
            throw new GeneralSecurityException("Android Keystore unavailable.", exception);
        }
    }

    byte[] openEnvelopeForProbe(byte[] envelope, String accountBinding, SecretKey key)
            throws GeneralSecurityException {
        return decrypt(envelope, accountBinding, key);
    }

    private File vaultFile(String accountBinding) throws GeneralSecurityException {
        byte[] binding = bindingBytes(accountBinding);
        byte[] digest;
        try {
            digest = MessageDigest.getInstance("SHA-256").digest(binding);
        } finally {
            Arrays.fill(binding, (byte) 0);
        }
        try {
            return new File(context.getNoBackupFilesDir(), "device-vault-v1-" + hex(digest) + ".bin");
        } finally {
            Arrays.fill(digest, (byte) 0);
        }
    }

    private SecretKey keyFor(String accountBinding, boolean create) throws GeneralSecurityException {
        String alias = keyAlias(accountBinding);

        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        try {
            store.load(null);
            Key stored = store.getKey(alias, null);
            if (stored != null) {
                if (!(stored instanceof SecretKey)) throw new GeneralSecurityException("Vault key type rejected.");
                return (SecretKey) stored;
            }
        } catch (IOException exception) {
            throw new GeneralSecurityException("Android Keystore unavailable.", exception);
        }
        if (!create) throw new GeneralSecurityException("Vault key is unavailable.");

        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(alias,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setKeySize(256)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .build());
        return generator.generateKey();
    }

    private String keyAlias(String accountBinding) throws GeneralSecurityException {
        byte[] binding = bindingBytes(accountBinding);
        byte[] digest = null;
        try {
            digest = MessageDigest.getInstance("SHA-256").digest(binding);
            return "focusguard.device.vault.v1." + hex(digest);
        } finally {
            Arrays.fill(binding, (byte) 0);
            if (digest != null) Arrays.fill(digest, (byte) 0);
        }
    }

    private static byte[] encrypt(byte[] cleartext, String accountBinding, SecretKey key)
            throws GeneralSecurityException {
        byte[] aad = associatedData(accountBinding);
        byte[] nonce = null;
        byte[] ciphertext = null;
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, key); // Android Keystore supplies the randomized IV.
            nonce = cipher.getIV();
            if (nonce == null || nonce.length != NONCE_BYTES) throw new GeneralSecurityException("Vault nonce rejected.");
            cipher.updateAAD(aad);
            ciphertext = cipher.doFinal(cleartext);
            if (ciphertext.length < TAG_BYTES || ciphertext.length > MAX_CLEAR_BYTES + TAG_BYTES)
                throw new GeneralSecurityException("Vault ciphertext size rejected.");
            ByteBuffer envelope = ByteBuffer.allocate(HEADER_BYTES + nonce.length + ciphertext.length);
            envelope.putInt(MAGIC).put((byte) VERSION).put((byte) nonce.length)
                    .putInt(ciphertext.length).put(nonce).put(ciphertext);
            return envelope.array();
        } finally {
            Arrays.fill(aad, (byte) 0);
            if (nonce != null) Arrays.fill(nonce, (byte) 0);
            if (ciphertext != null) Arrays.fill(ciphertext, (byte) 0);
        }
    }

    private static byte[] decrypt(byte[] envelope, String accountBinding, SecretKey key)
            throws GeneralSecurityException {
        if (envelope == null || envelope.length < HEADER_BYTES + NONCE_BYTES + TAG_BYTES
                || envelope.length > HEADER_BYTES + NONCE_BYTES + MAX_CLEAR_BYTES + TAG_BYTES)
            throw new GeneralSecurityException("Vault envelope size rejected.");
        ByteBuffer input = ByteBuffer.wrap(envelope);
        if (input.getInt() != MAGIC || Byte.toUnsignedInt(input.get()) != VERSION)
            throw new GeneralSecurityException("Vault envelope version rejected.");
        int nonceLength = Byte.toUnsignedInt(input.get());
        int ciphertextLength = input.getInt();
        if (nonceLength != NONCE_BYTES || ciphertextLength < TAG_BYTES
                || ciphertextLength > MAX_CLEAR_BYTES + TAG_BYTES
                || input.remaining() != nonceLength + ciphertextLength)
            throw new GeneralSecurityException("Vault envelope structure rejected.");
        byte[] nonce = new byte[nonceLength];
        byte[] ciphertext = new byte[ciphertextLength];
        byte[] aad = associatedData(accountBinding);
        try {
            input.get(nonce).get(ciphertext);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key, new GCMParameterSpec(TAG_BITS, nonce));
            cipher.updateAAD(aad);
            return cipher.doFinal(ciphertext);
        } finally {
            Arrays.fill(nonce, (byte) 0);
            Arrays.fill(ciphertext, (byte) 0);
            Arrays.fill(aad, (byte) 0);
        }
    }

    private static byte[] readBounded(AtomicFile file) throws IOException {
        try (FileInputStream input = file.openRead(); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] block = new byte[4096];
            int total = 0;
            try {
                int count;
                while ((count = input.read(block)) != -1) {
                    total += count;
                    if (total > HEADER_BYTES + NONCE_BYTES + MAX_CLEAR_BYTES + TAG_BYTES)
                        throw new IOException("Vault file exceeds the size limit.");
                    output.write(block, 0, count);
                }
                return output.toByteArray();
            } finally {
                Arrays.fill(block, (byte) 0);
            }
        }
    }

    private static byte[] associatedData(String accountBinding) throws GeneralSecurityException {
        byte[] binding = bindingBytes(accountBinding);
        try {
            ByteBuffer aad = ByteBuffer.allocate(AAD_DOMAIN.length + binding.length);
            aad.put(AAD_DOMAIN).put(binding);
            return aad.array();
        } finally {
            Arrays.fill(binding, (byte) 0);
        }
    }

    private static byte[] bindingBytes(String accountBinding) throws GeneralSecurityException {
        if (accountBinding == null || accountBinding.length() > 64
                || !accountBinding.matches("[A-Za-z0-9_-]{43}"))
            throw new GeneralSecurityException("Account binding format rejected.");
        try {
            byte[] decoded = Base64.getUrlDecoder().decode(accountBinding);
            if (decoded.length != 32) {
                Arrays.fill(decoded, (byte) 0);
                throw new GeneralSecurityException("Account binding length rejected.");
            }
            return decoded;
        } catch (IllegalArgumentException exception) {
            throw new GeneralSecurityException("Account binding format rejected.", exception);
        }
    }

    private static String hex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) result.append(String.format(java.util.Locale.ROOT, "%02x", value & 0xff));
        return result.toString();
    }
}
