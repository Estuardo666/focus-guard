package app.focusguard.phase0;

import android.content.Context;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.SecretKey;

/** Exercises the local Android Keystore vault with synthetic data only. */
public final class AndroidVaultRuntimeProbe {
    private static final byte[] SYNTHETIC_MATERIAL =
            "FOCUSGUARD-ANDROID-VAULT-SYNTHETIC-v1".getBytes(StandardCharsets.UTF_8);

    private AndroidVaultRuntimeProbe() { }

    public static String run(Context context) {
        byte[] accountDigest = null;
        byte[] otherAccountDigest = null;
        byte[] plaintext = SYNTHETIC_MATERIAL.clone();
        byte[] loaded = null;
        byte[] envelope = null;
        byte[] modified = null;
        SecretKey key = null;
        try {
            accountDigest = MessageDigest.getInstance("SHA-256")
                    .digest("android-keystore-vault-probe-account-v1".getBytes(StandardCharsets.UTF_8));
            otherAccountDigest = MessageDigest.getInstance("SHA-256")
                    .digest("android-keystore-vault-other-probe-account-v1".getBytes(StandardCharsets.UTF_8));
            String binding = encode(accountDigest);
            String otherBinding = encode(otherAccountDigest);
            AndroidDeviceVault vault = new AndroidDeviceVault(context);
            boolean reloaded = vault.exists(binding);
            if (!reloaded) vault.save(binding, plaintext);
            loaded = vault.load(binding);
            require(MessageDigest.isEqual(plaintext, loaded), "VAULT_READBACK_FAILED");

            envelope = vault.readEnvelopeForProbe(binding);
            require(!contains(envelope, SYNTHETIC_MATERIAL), "VAULT_PLAINTEXT_FOUND");
            key = vault.keyForProbe(binding);
            require(key.getEncoded() == null, "VAULT_KEY_EXPORTABLE");
            byte[] storedEnvelope = envelope;
            SecretKey vaultKey = key;
            verifyRejected(() -> vault.openEnvelopeForProbe(storedEnvelope, otherBinding, vaultKey), "WRONG_ACCOUNT_ACCEPTED");

            modified = envelope.clone();
            modified[modified.length - 1] ^= 1;
            byte[] tamperedEnvelope = modified;
            verifyRejected(() -> vault.openEnvelopeForProbe(tamperedEnvelope, binding, vaultKey), "VAULT_TAMPER_ACCEPTED");

            File file = vault.fileForProbe(binding).getCanonicalFile();
            File noBackup = context.getNoBackupFilesDir().getCanonicalFile();
            require(file.getParentFile().equals(noBackup), "VAULT_OUTSIDE_NO_BACKUP_DIR");
            boolean hardwareBacked = vault.isHardwareBacked(binding);
            byte[] clearDigest = MessageDigest.getInstance("SHA-256")
                    .digest("android-keystore-vault-clear-probe-v1".getBytes(StandardCharsets.UTF_8));
            try {
                String clearBinding = encode(clearDigest);
                vault.save(clearBinding, plaintext);
                require(vault.exists(clearBinding) && vault.hasKeyForProbe(clearBinding), "VAULT_CLEAR_SETUP_FAILED");
                vault.clear(clearBinding);
                require(!vault.exists(clearBinding) && !vault.hasKeyForProbe(clearBinding), "VAULT_CLEAR_FAILED");
            } finally { Arrays.fill(clearDigest, (byte) 0); }
            return (reloaded ? "VAULT_RELOAD_PASS" : "VAULT_CREATE_PASS")
                    + "\nVAULT_READBACK_PASS · NONEXPORTABLE_KEY_PASS · NO_BACKUP_FILE_PASS"
                    + "\nPLAINTEXT_ABSENT_PASS · WRONG_ACCOUNT_REJECTED_PASS · TAMPER_REJECTED_PASS"
                    + "\nVAULT_CLEAR_PASS · local file and per-binding Keystore key removed"
                    + "\nKey hardware-backed: " + (hardwareBacked ? "yes" : "no; software Keystore");
        } catch (Exception | LinkageError exception) {
            return "ANDROID_VAULT_PROBE_FAILED: " + exception.getClass().getSimpleName();
        } finally {
            if (accountDigest != null) Arrays.fill(accountDigest, (byte) 0);
            if (otherAccountDigest != null) Arrays.fill(otherAccountDigest, (byte) 0);
            Arrays.fill(plaintext, (byte) 0);
            if (loaded != null) Arrays.fill(loaded, (byte) 0);
            if (envelope != null) Arrays.fill(envelope, (byte) 0);
            if (modified != null) Arrays.fill(modified, (byte) 0);
        }
    }

    private static void verifyRejected(CheckedAction action, String failure) throws Exception {
        try {
            byte[] unexpected = action.run();
            Arrays.fill(unexpected, (byte) 0);
            throw new GeneralSecurityException(failure);
        } catch (GeneralSecurityException expected) {
            if (failure.equals(expected.getMessage())) throw expected;
        }
    }

    private static String encode(byte[] bytes) {
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static boolean contains(byte[] content, byte[] needle) {
        if (needle.length == 0 || content.length < needle.length) return false;
        for (int start = 0; start <= content.length - needle.length; start++) {
            int index = 0;
            while (index < needle.length && content[start + index] == needle[index]) index++;
            if (index == needle.length) return true;
        }
        return false;
    }

    private static void require(boolean condition, String failure) throws GeneralSecurityException {
        if (!condition) throw new GeneralSecurityException(failure);
    }

    @FunctionalInterface
    private interface CheckedAction { byte[] run() throws GeneralSecurityException; }
}
