package app.focusguard.phase0;

import android.content.Context;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Base64;

/** Synthetic identity lifecycle and C#-compatible public vector checks for the Android AVD. */
public final class AndroidIdentityRuntimeProbe {
    private static final String VECTOR_DEVICE_ID = "11111111-1111-4111-8111-111111111111";
    private static final String EXPECTED_SIGNING_PUBLIC = "ebVWLo_mVPlAeLES6KmLp5AfhTrmlb7X4OORC60ElmQ";
    private static final String EXPECTED_ENCRYPTION_PUBLIC = "V3-u8AYN_QDAOScrxv58Qmic4W20e2_CqkHRmBn_qTY";
    private static final String EXPECTED_FINGERPRINT = "hvajpSraxvUY7-P82PqDv0XVW36oY5BX4CtepzLkjH4";
    private static final String EXPECTED_SIGNATURE = "BrWSMsUGdq--2woiUSjgZC6qgbxAVU9bcyjnJAJySW_5UYGu17PA-4fUDnJLe0zeHzVCm-KXocerkzgOHYGtBw";
    private static final String EXPECTED_PAYLOAD = "eyJkZXZpY2VJZCI6IjExMTExMTExLTExMTEtNDExMS04MTExLTExMTExMTExMTExMSIsInB1cnBvc2UiOiJhbmRyb2lkLWNyb3NzLXBsYXRmb3JtIiwidGV4dCI6ImNhZsOpIn0";

    private AndroidIdentityRuntimeProbe() { }

    public static String run(Context context) {
        byte[] bindingDigest = null;
        byte[] otherBindingDigest = null;
        byte[] payload = null;
        byte[] changedPayload = null;
        byte[] clearIdentity = null;
        byte[] envelope = null;
        AndroidDeviceIdentity identity = null;
        AndroidDeviceIdentity vector = null;
        try {
            bindingDigest = MessageDigest.getInstance("SHA-256")
                    .digest("android-identity-probe-account-v1".getBytes(StandardCharsets.UTF_8));
            otherBindingDigest = MessageDigest.getInstance("SHA-256")
                    .digest("android-identity-probe-other-account-v1".getBytes(StandardCharsets.UTF_8));
            String binding = AndroidDeviceIdentity.encode(bindingDigest);
            String otherBinding = AndroidDeviceIdentity.encode(otherBindingDigest);
            AndroidDeviceVault vault = new AndroidDeviceVault(context);
            boolean reloaded = vault.exists(binding);
            identity = AndroidDeviceIdentity.loadOrCreate(vault, binding);
            payload = canonicalPayload(identity.deviceId());
            String signature = identity.sign("FG-REQUEST-1", payload);
            require(AndroidDeviceIdentity.verify(identity.publicIdentity(), "FG-REQUEST-1", payload, signature), "IDENTITY_SIGNATURE_FAILED");
            changedPayload = payload.clone();
            changedPayload[changedPayload.length - 1] ^= 1;
            require(!AndroidDeviceIdentity.verify(identity.publicIdentity(), "FG-REQUEST-1", changedPayload, signature), "IDENTITY_TAMPER_ACCEPTED");
            require(!AndroidDeviceIdentity.verify(identity.publicIdentity(), "FG-MEMBERSHIP-1", payload, signature), "IDENTITY_DOMAIN_REUSE_ACCEPTED");

            clearIdentity = identity.serializeForVault();
            envelope = vault.readEnvelopeForProbe(binding);
            require(!contains(envelope, clearIdentity), "IDENTITY_PLAINTEXT_FOUND");
            javax.crypto.SecretKey vaultKey = vault.keyForProbe(binding);
            boolean wrongBindingRejected = false;
            try {
                byte[] wrongBindingClear = vault.openEnvelopeForProbe(envelope, otherBinding, vaultKey);
                Arrays.fill(wrongBindingClear, (byte) 0);
            } catch (GeneralSecurityException expected) { wrongBindingRejected = true; }
            require(wrongBindingRejected, "IDENTITY_WRONG_BINDING_ACCEPTED");

            byte[] signingSeed = sequence(1, 32);
            byte[] encryptionSeed = sequence(33, 32);
            try {
                vector = AndroidDeviceIdentity.fromSeedsForProbe(VECTOR_DEVICE_ID, signingSeed, encryptionSeed);
            } finally {
                Arrays.fill(signingSeed, (byte) 0);
                Arrays.fill(encryptionSeed, (byte) 0);
            }
            byte[] vectorPayload = canonicalPayload(VECTOR_DEVICE_ID);
            String vectorSignature;
            String vectorPayloadText;
            try {
                vectorSignature = vector.sign("FG-REQUEST-1", vectorPayload);
                vectorPayloadText = AndroidDeviceIdentity.encode(vectorPayload);
            }
            finally { Arrays.fill(vectorPayload, (byte) 0); }
            AndroidDeviceIdentity.PublicIdentity publicIdentity = vector.publicIdentity();
            require(EXPECTED_SIGNING_PUBLIC.equals(publicIdentity.signingPublicKey()), "CROSS_PLATFORM_SIGNING_KEY_MISMATCH");
            require(EXPECTED_ENCRYPTION_PUBLIC.equals(publicIdentity.encryptionPublicKey()), "CROSS_PLATFORM_ENCRYPTION_KEY_MISMATCH");
            require(EXPECTED_FINGERPRINT.equals(publicIdentity.fingerprint()), "CROSS_PLATFORM_FINGERPRINT_MISMATCH");
            require(EXPECTED_SIGNATURE.equals(vectorSignature), "CROSS_PLATFORM_SIGNATURE_MISMATCH");
            require(EXPECTED_PAYLOAD.equals(vectorPayloadText), "CROSS_PLATFORM_JCS_PAYLOAD_MISMATCH");
            return (reloaded ? "IDENTITY_RELOAD_PASS" : "IDENTITY_CREATE_PASS")
                    + " · UUID y semillas dentro del vault cifrado"
                    + "\nIDENTITY_SIGNATURE_PASS · DOMAIN_BINDING_PASS · TAMPER_REJECTED_PASS"
                    + "\nCROSS_PLATFORM_CSHARP_VECTOR_PASS · Ed25519/X25519/JCS/fingerprint"
                    + "\nHuella pública: " + identity.publicIdentity().fingerprint().substring(0, 12) + "…"
                    + "\nNo se muestran ni exportan semillas; sin cuenta, red ni bloqueo.";
        } catch (Exception | LinkageError exception) {
            return "ANDROID_IDENTITY_PROBE_FAILED: " + exception.getClass().getSimpleName()
                    + (exception.getMessage() == null ? "" : " · " + exception.getMessage());
        } finally {
            if (bindingDigest != null) Arrays.fill(bindingDigest, (byte) 0);
            if (otherBindingDigest != null) Arrays.fill(otherBindingDigest, (byte) 0);
            if (payload != null) Arrays.fill(payload, (byte) 0);
            if (changedPayload != null) Arrays.fill(changedPayload, (byte) 0);
            if (clearIdentity != null) Arrays.fill(clearIdentity, (byte) 0);
            if (envelope != null) Arrays.fill(envelope, (byte) 0);
            if (identity != null) identity.close();
            if (vector != null) vector.close();
        }
    }

    private static byte[] canonicalPayload(String deviceId) throws IOException {
        String json = "{\"deviceId\":\"" + deviceId + "\",\"purpose\":\"android-cross-platform\",\"text\":\"café\"}";
        return new org.webpki.jcs.JsonCanonicalizer(json).getEncodedUTF8();
    }

    private static byte[] sequence(int first, int length) {
        byte[] result = new byte[length];
        for (int index = 0; index < length; index++) result[index] = (byte) (first + index);
        return result;
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
}
