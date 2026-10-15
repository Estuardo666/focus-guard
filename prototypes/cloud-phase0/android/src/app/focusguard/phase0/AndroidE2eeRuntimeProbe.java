package app.focusguard.phase0;

import android.content.Context;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Arrays;

/** Public synthetic cross-platform document vectors; never uses account or profile data. */
public final class AndroidE2eeRuntimeProbe {
    private static final String AUTHOR_ID = "11111111-1111-4111-8111-111111111111";
    private static final String DOCUMENT_JSON = "{\"durationMinutes\":25,\"theme\":\"mist\"}";
    private static final String CSHARP_VECTOR_JSON = "{\"protocol\":\"FG-E2EE-1\",\"suite\":\"XCHACHA20-POLY1305-IETF\",\"accountId\":\"22222222-2222-4222-8222-222222222222\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"entityId\":\"44444444-4444-4444-8444-444444444444\",\"kind\":\"profile.settings\",\"baseRevision\":0,\"proposedRevision\":1,\"keyEpoch\":2,\"membershipRevision\":7,\"authorDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"nonce\":\"Xcp0UCqfpUFsUIB0eo2Na7RF6N9OZOxG\",\"ciphertext\":\"qfTVQtR92epSVOZzXiYDtbC_lYOpLGYof89_cnO1Zbg6snHL-qgLIVaBVQ4qwW5-XvnXYlE\",\"signature\":\"yWdogUv0ZmH50Qhl4XpAD0MU2H2Au2hmVgFOvm2eO1AzSp6ECwdh0PuiaJM38gdLa5qEdMDOnFYMepiiJNirAQ\"}";
    private static final AndroidContentDocumentCodec.DocumentContext CONTEXT =
            new AndroidContentDocumentCodec.DocumentContext("22222222-2222-4222-8222-222222222222",
                    "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444",
                    "profile.settings", 0, 1, 2, 7);

    private AndroidE2eeRuntimeProbe() { }

    public static String run(Context context) {
        byte[] signingSeed = sequence(1, 32);
        byte[] encryptionSeed = sequence(33, 32);
        byte[] contentKey = sequence(129, 32);
        byte[] wrongKey = sequence(161, 32);
        byte[] clear = DOCUMENT_JSON.getBytes(StandardCharsets.UTF_8);
        AndroidDeviceIdentity author = null;
        byte[] opened = null;
        try {
            author = AndroidDeviceIdentity.fromSeedsForProbe(AUTHOR_ID, signingSeed, encryptionSeed);
            final AndroidDeviceIdentity signer = author;
            AndroidContentDocumentCodec.EncryptedDocument csharpDocument =
                    AndroidContentDocumentCodec.fromJson(CSHARP_VECTOR_JSON);
            expectRejected(() -> AndroidContentDocumentCodec.fromJson(
                    CSHARP_VECTOR_JSON.replace("\"baseRevision\":0", "\"baseRevision\":0.5")),
                    "E2EE_FRACTIONAL_REVISION_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.fromJson(
                    CSHARP_VECTOR_JSON.replace("{\"protocol\":", "{\"protocol\":\"FG-E2EE-1\",\"protocol\":")),
                    "E2EE_DUPLICATE_ENVELOPE_FIELD_ACCEPTED");
            opened = AndroidContentDocumentCodec.decrypt(csharpDocument, CONTEXT, contentKey,
                    signer.publicIdentity());
            require(Arrays.equals(clear, opened), "CSHARP_DOCUMENT_CONTENT_MISMATCH");
            Arrays.fill(opened, (byte) 0);
            opened = null;

            expectRejected(() -> AndroidContentDocumentCodec.decrypt(csharpDocument,
                    new AndroidContentDocumentCodec.DocumentContext(CONTEXT.accountId(), CONTEXT.generationId(),
                            CONTEXT.entityId(), CONTEXT.kind(), 0, 1, 2, 8), contentKey, signer.publicIdentity()),
                    "E2EE_WRONG_MEMBERSHIP_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.decrypt(csharpDocument, CONTEXT, wrongKey,
                    signer.publicIdentity()), "E2EE_WRONG_KEY_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.decrypt(new AndroidContentDocumentCodec.EncryptedDocument(
                    csharpDocument.protocol(), csharpDocument.suite(), csharpDocument.accountId(),
                    csharpDocument.generationId(), csharpDocument.entityId(), csharpDocument.kind(),
                    csharpDocument.baseRevision(), csharpDocument.proposedRevision(), csharpDocument.keyEpoch(),
                    csharpDocument.membershipRevision(), csharpDocument.authorDeviceId(), csharpDocument.nonce(),
                    mutateLast(csharpDocument.ciphertext()), csharpDocument.signature()), CONTEXT, contentKey,
                    signer.publicIdentity()), "E2EE_TAMPERED_CIPHERTEXT_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.decrypt(new AndroidContentDocumentCodec.EncryptedDocument(
                    csharpDocument.protocol(), csharpDocument.suite(), csharpDocument.accountId(),
                    csharpDocument.generationId(), csharpDocument.entityId(), csharpDocument.kind(),
                    csharpDocument.baseRevision(), csharpDocument.proposedRevision(), csharpDocument.keyEpoch(),
                    csharpDocument.membershipRevision(), csharpDocument.authorDeviceId(), csharpDocument.nonce(),
                    csharpDocument.ciphertext(), mutateLast(csharpDocument.signature())), CONTEXT, contentKey,
                    signer.publicIdentity()), "E2EE_TAMPERED_SIGNATURE_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.decrypt(new AndroidContentDocumentCodec.EncryptedDocument(
                    csharpDocument.protocol(), "UNKNOWN-SUITE", csharpDocument.accountId(),
                    csharpDocument.generationId(), csharpDocument.entityId(), csharpDocument.kind(),
                    csharpDocument.baseRevision(), csharpDocument.proposedRevision(), csharpDocument.keyEpoch(),
                    csharpDocument.membershipRevision(), csharpDocument.authorDeviceId(), csharpDocument.nonce(),
                    csharpDocument.ciphertext(), csharpDocument.signature()), CONTEXT, contentKey,
                    signer.publicIdentity()), "E2EE_UNKNOWN_SUITE_ACCEPTED");

            AndroidContentDocumentCodec.EncryptedDocument androidDocument = AndroidContentDocumentCodec.encrypt(
                    clear, contentKey, CONTEXT, signer);
            opened = AndroidContentDocumentCodec.decrypt(androidDocument, CONTEXT, contentKey,
                    signer.publicIdentity());
            require(Arrays.equals(clear, opened), "ANDROID_DOCUMENT_ROUNDTRIP_FAILED");
            AndroidContentDocumentCodec.writeEncryptedEnvelopeForProbe(context, androidDocument);
            expectRejected(() -> AndroidContentDocumentCodec.encrypt(
                    "{\"theme\":\"mist\",\"theme\":\"dark\"}".getBytes(StandardCharsets.UTF_8),
                    contentKey, CONTEXT, signer), "E2EE_DUPLICATE_JSON_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.encrypt(
                    "{\"theme\": \"mist\",\"durationMinutes\":25}".getBytes(StandardCharsets.UTF_8),
                    contentKey, CONTEXT, signer), "E2EE_NONCANONICAL_JSON_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.encrypt("[]".getBytes(StandardCharsets.UTF_8),
                    contentKey, CONTEXT, signer), "E2EE_NONOBJECT_JSON_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.encrypt(new byte[] {(byte) 0xc3, 0x28},
                    contentKey, CONTEXT, signer), "E2EE_INVALID_UTF8_ACCEPTED");
            StringBuilder deep = new StringBuilder();
            for (int index = 0; index < 33; index++) deep.append("{\"x\":");
            deep.append('0');
            for (int index = 0; index < 33; index++) deep.append('}');
            expectRejected(() -> AndroidContentDocumentCodec.encrypt(
                    deep.toString().getBytes(StandardCharsets.UTF_8), contentKey, CONTEXT, signer),
                    "E2EE_EXCESSIVE_DEPTH_ACCEPTED");
            expectRejected(() -> AndroidContentDocumentCodec.encrypt(new byte[262145],
                    contentKey, CONTEXT, signer), "E2EE_OVERSIZE_JSON_ACCEPTED");

            return "E2EE_CSHARP_TO_ANDROID_PASS · XChaCha20-Poly1305/JCS/Ed25519"
                    + "\nE2EE_ANDROID_ROUNDTRIP_PASS · signed encrypted envelope saved for local C# check"
                    + "\nE2EE_WRONG_CONTEXT/KEY/SIGNATURE/CIPHERTEXT/SUITE_REJECTED_PASS"
                    + "\nE2EE_DUPLICATE/DECIMAL/NONCANONICAL/UTF8/ROOT/DEPTH/SIZE_REJECTED_PASS"
                    + "\nNo network, account or personal settings.";
        } catch (Exception | LinkageError exception) {
            return "ANDROID_E2EE_PROBE_FAILED: " + exception.getClass().getSimpleName()
                    + (exception.getMessage() == null ? "" : " · " + exception.getMessage());
        } finally {
            Arrays.fill(signingSeed, (byte) 0);
            Arrays.fill(encryptionSeed, (byte) 0);
            Arrays.fill(contentKey, (byte) 0);
            Arrays.fill(wrongKey, (byte) 0);
            Arrays.fill(clear, (byte) 0);
            if (opened != null) Arrays.fill(opened, (byte) 0);
            if (author != null) author.close();
        }
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
