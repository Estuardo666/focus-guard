package app.focusguard.phase0;

import android.content.Context;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Arrays;

/** Synthetic C#↔Android sealed-box and signed-header interoperability probe. */
public final class AndroidKeyEnvelopeRuntimeProbe {
    private static final String SIGNER_ID = "11111111-1111-4111-8111-111111111111";
    private static final String RECIPIENT_ID = "55555555-5555-4555-8555-555555555555";
    private static final String MEMBERSHIP_DIGEST = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static final String CSHARP_VECTOR_JSON = "{\"suite\":\"FG-SODIUM-BOX-1\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"keyEpoch\":2,\"recipientId\":\"55555555-5555-4555-8555-555555555555\",\"recipientPublicKey\":\"kjdZZ00a9DxSLojIyhEb6L0CmMwXB0KKjN49XVtODFo\",\"membershipDigest\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"ciphertext\":\"Ta0K8e_szOAorc03l013KXZggnwz0kUNsyH-EdAifS84UVH8wm2cO5t33mDsdMc1iSAb3LDVlKL9ONEQSlW3tHwwCrSL6Dnee2TZntT40zUZXQP4WHu6enW-KtryKTmP0zfXOSxxPq5dc9QS7H3iAl3j5fDX-cDXC9zHH5gxbhOmboIWIqsrLaxR6ztbDt3g0l5OGojIZ8qXN_SDsGQxyPQZ0l5H8YkpwbchuAZd6Xq2letio7YNhYTsfsJbxmZ_2-M9LUTE6yHuZ894-o5Ggos1nmHoNX9qPp6ew_usPgNmTiwENs1uDCa9xGciRmFVUI0F450PiwwoD9WcnOSnYaMmZ_WsqPUI49h_ytwGfvwu28sYsq50dIb_eL2aMSnCoV7fe5Wg4sqbEwckENlJiz5GjEA4YyMzx93pQAn6d0gxQoEV_eoR_P1zRtZrgflycwlynsb1bRYBBOArwAJiWdderuTxF6ns28MBzq_hPO4GywI-_zQ\",\"signerDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"signature\":\"-9WoiaQwbRojJ6a9X3V_GYkblDSVCqh5J8a6oipZyx1vjA6Y4NzIBKWIjzvwVO00sjhRWOr2uLc7574olsXGBw\"}";
    private static final AndroidSignedKeyEnvelopeCodec.EnvelopeContext CONTEXT =
            new AndroidSignedKeyEnvelopeCodec.EnvelopeContext("22222222-2222-4222-8222-222222222222",
                    "33333333-3333-4333-8333-333333333333", 2, RECIPIENT_ID, MEMBERSHIP_DIGEST, SIGNER_ID);

    private AndroidKeyEnvelopeRuntimeProbe() { }

    public static String run(Context appContext) {
        byte[] signerSeed = sequence(1, 32);
        byte[] signerEncryptionSeed = sequence(33, 32);
        byte[] recipientSeed = sequence(65, 32);
        byte[] recipientEncryptionSeed = sequence(97, 32);
        byte[] wrongRecipientSeed = sequence(129, 32);
        byte[] contentKey = sequence(129, 32);
        AndroidDeviceIdentity signer = null;
        AndroidDeviceIdentity recipient = null;
        AndroidDeviceIdentity wrongRecipient = null;
        AndroidDeviceIdentity wrongSigner = null;
        byte[] opened = null;
        try {
            signer = AndroidDeviceIdentity.fromSeedsForProbe(SIGNER_ID, signerSeed, signerEncryptionSeed);
            recipient = AndroidDeviceIdentity.fromSeedsForProbe(RECIPIENT_ID, recipientSeed, recipientEncryptionSeed);
            wrongRecipient = AndroidDeviceIdentity.fromSeedsForProbe(RECIPIENT_ID, recipientSeed, wrongRecipientSeed);
            wrongSigner = AndroidDeviceIdentity.fromSeedsForProbe(SIGNER_ID, wrongRecipientSeed, signerEncryptionSeed);
            final AndroidDeviceIdentity author = signer;
            final AndroidDeviceIdentity localRecipient = recipient;
            final AndroidDeviceIdentity alternateRecipient = wrongRecipient;
            final AndroidDeviceIdentity alternateSigner = wrongSigner;

            AndroidSignedKeyEnvelopeCodec.SignedEnvelope csharpEnvelope =
                    AndroidSignedKeyEnvelopeCodec.fromJson(CSHARP_VECTOR_JSON);
            opened = AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope, CONTEXT,
                    author.publicIdentity(), localRecipient);
            require(messageDigestEqual(opened, contentKey), "CSHARP_CONTENT_KEY_MISMATCH");
            Arrays.fill(opened, (byte) 0);
            opened = null;

            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope,
                    new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(CONTEXT.accountId(), CONTEXT.generationId(),
                            3, CONTEXT.recipientId(), CONTEXT.membershipDigest(), CONTEXT.signerDeviceId()),
                    author.publicIdentity(), localRecipient), "KEY_ENVELOPE_WRONG_EPOCH_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope,
                    new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(CONTEXT.accountId(),
                            "44444444-4444-4444-8444-444444444444", CONTEXT.keyEpoch(), CONTEXT.recipientId(),
                            CONTEXT.membershipDigest(), CONTEXT.signerDeviceId()),
                    author.publicIdentity(), localRecipient), "KEY_ENVELOPE_WRONG_GENERATION_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope,
                    new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(CONTEXT.accountId(), CONTEXT.generationId(),
                            CONTEXT.keyEpoch(), CONTEXT.recipientId(), repeat('B', 43), CONTEXT.signerDeviceId()),
                    author.publicIdentity(), localRecipient), "KEY_ENVELOPE_WRONG_MEMBERSHIP_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope, CONTEXT,
                    alternateSigner.publicIdentity(), localRecipient), "KEY_ENVELOPE_WRONG_SIGNER_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(csharpEnvelope, CONTEXT,
                    author.publicIdentity(), alternateRecipient), "KEY_ENVELOPE_WRONG_RECIPIENT_KEY_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(withSignature(csharpEnvelope,
                    mutateLast(csharpEnvelope.signature())), CONTEXT, author.publicIdentity(), localRecipient),
                    "KEY_ENVELOPE_TAMPERED_SIGNATURE_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(withCiphertext(csharpEnvelope,
                    mutateLast(csharpEnvelope.ciphertext())), CONTEXT, author.publicIdentity(), localRecipient),
                    "KEY_ENVELOPE_TAMPERED_CIPHERTEXT_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.openForDevice(withSuite(csharpEnvelope,
                    "UNKNOWN-SUITE"), CONTEXT, author.publicIdentity(), localRecipient),
                    "KEY_ENVELOPE_UNKNOWN_SUITE_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.fromJson(
                    CSHARP_VECTOR_JSON.replace("{\"suite\":", "{\"suite\":\"FG-SODIUM-BOX-1\",\"suite\":")),
                    "KEY_ENVELOPE_DUPLICATE_FIELD_ACCEPTED");
            expectRejected(() -> AndroidSignedKeyEnvelopeCodec.fromJson(
                    CSHARP_VECTOR_JSON.substring(0, CSHARP_VECTOR_JSON.length() - 1) + ",\"unknown\":true}"),
                    "KEY_ENVELOPE_UNKNOWN_FIELD_ACCEPTED");

            AndroidSignedKeyEnvelopeCodec.SignedEnvelope androidEnvelope =
                    AndroidSignedKeyEnvelopeCodec.sealForDevice(contentKey, CONTEXT,
                            localRecipient.publicIdentity().encryptionPublicKey(), author);
            opened = AndroidSignedKeyEnvelopeCodec.openForDevice(androidEnvelope, CONTEXT,
                    author.publicIdentity(), localRecipient);
            require(messageDigestEqual(opened, contentKey), "ANDROID_KEY_ENVELOPE_ROUNDTRIP_FAILED");
            AndroidSignedKeyEnvelopeCodec.writeEnvelopeForProbe(appContext, androidEnvelope);
            return "KEY_ENVELOPE_CSHARP_TO_ANDROID_PASS · signed sealed-box opens only for the recipient"
                    + "\nKEY_ENVELOPE_ANDROID_ROUNDTRIP_PASS · Android signature and sealed-box"
                    + "\nKEY_ENVELOPE_WRONG_CONTEXT/MEMBERSHIP/SIGNER/RECIPIENT/SIGNATURE/CIPHERTEXT/SUITE_REJECTED_PASS"
                    + "\nKEY_ENVELOPE_DUPLICATE/UNKNOWN_FIELDS_REJECTED_PASS · temporary encrypted fixture for C#";
        } catch (Exception | LinkageError exception) {
            return "ANDROID_KEY_ENVELOPE_PROBE_FAILED: " + exception.getClass().getSimpleName()
                    + (exception.getMessage() == null ? "" : " · " + exception.getMessage());
        } finally {
            Arrays.fill(signerSeed, (byte) 0);
            Arrays.fill(signerEncryptionSeed, (byte) 0);
            Arrays.fill(recipientSeed, (byte) 0);
            Arrays.fill(recipientEncryptionSeed, (byte) 0);
            Arrays.fill(wrongRecipientSeed, (byte) 0);
            Arrays.fill(contentKey, (byte) 0);
            if (opened != null) Arrays.fill(opened, (byte) 0);
            if (signer != null) signer.close();
            if (recipient != null) recipient.close();
            if (wrongRecipient != null) wrongRecipient.close();
            if (wrongSigner != null) wrongSigner.close();
        }
    }

    private static AndroidSignedKeyEnvelopeCodec.SignedEnvelope withSignature(
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope, String signature) {
        return new AndroidSignedKeyEnvelopeCodec.SignedEnvelope(envelope.suite(), envelope.generationId(),
                envelope.keyEpoch(), envelope.recipientId(), envelope.recipientPublicKey(),
                envelope.membershipDigest(), envelope.ciphertext(), envelope.signerDeviceId(), signature);
    }

    private static AndroidSignedKeyEnvelopeCodec.SignedEnvelope withCiphertext(
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope, String ciphertext) {
        return new AndroidSignedKeyEnvelopeCodec.SignedEnvelope(envelope.suite(), envelope.generationId(),
                envelope.keyEpoch(), envelope.recipientId(), envelope.recipientPublicKey(),
                envelope.membershipDigest(), ciphertext, envelope.signerDeviceId(), envelope.signature());
    }

    private static AndroidSignedKeyEnvelopeCodec.SignedEnvelope withSuite(
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope, String suite) {
        return new AndroidSignedKeyEnvelopeCodec.SignedEnvelope(suite, envelope.generationId(),
                envelope.keyEpoch(), envelope.recipientId(), envelope.recipientPublicKey(),
                envelope.membershipDigest(), envelope.ciphertext(), envelope.signerDeviceId(), envelope.signature());
    }

    private static String mutateLast(String value) {
        char last = value.charAt(value.length() - 1);
        return value.substring(0, value.length() - 1) + (last == 'A' ? 'B' : 'A');
    }

    private static byte[] sequence(int first, int length) {
        byte[] result = new byte[length];
        for (int index = 0; index < length; index++) result[index] = (byte) (first + index);
        return result;
    }

    private static String repeat(char value, int length) {
        char[] chars = new char[length];
        Arrays.fill(chars, value);
        return new String(chars);
    }

    private static boolean messageDigestEqual(byte[] left, byte[] right) {
        return java.security.MessageDigest.isEqual(left, right);
    }

    private static void expectRejected(CheckedAction action, String failure) throws Exception {
        try {
            Object unexpected = action.run();
            if (unexpected instanceof byte[] bytes) Arrays.fill(bytes, (byte) 0);
            throw new GeneralSecurityException(failure);
        } catch (GeneralSecurityException expected) {
            if (failure.equals(expected.getMessage())) throw expected;
        }
    }

    private static void require(boolean condition, String failure) throws GeneralSecurityException {
        if (!condition) throw new GeneralSecurityException(failure);
    }

    @FunctionalInterface
    private interface CheckedAction { Object run() throws GeneralSecurityException; }
}
