package app.focusguard.phase0;

import android.content.Context;

import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.util.Arrays;
import java.util.List;

/** Verifies a signed C# membership chain before using its key-envelope or document authority. */
public final class AndroidMembershipRuntimeProbe {
    private static final String SIGNER_ID = "11111111-1111-4111-8111-111111111111";
    private static final String RECIPIENT_ID = "55555555-5555-4555-8555-555555555555";
    private static final String ACCOUNT_ID = "22222222-2222-4222-8222-222222222222";
    private static final String GENERATION_ID = "33333333-3333-4333-8333-333333333333";
    private static final String CSHARP_FIXTURE = "{\"status\":\"PASS\",\"initial\":{\"protocol\":\"FG-MEMBERSHIP-1\",\"accountId\":\"22222222-2222-4222-8222-222222222222\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"baseRevision\":0,\"proposedRevision\":1,\"keyEpoch\":1,\"previousDigest\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"members\":[{\"deviceId\":\"11111111-1111-4111-8111-111111111111\",\"signingPublicKey\":\"ebVWLo_mVPlAeLES6KmLp5AfhTrmlb7X4OORC60ElmQ\",\"encryptionPublicKey\":\"V3-u8AYN_QDAOScrxv58Qmic4W20e2_CqkHRmBn_qTY\",\"status\":\"Active\",\"roles\":[\"ManageDevices\",\"Read\",\"StartNormal\",\"StartStrict\",\"WriteSettings\"]}],\"recoverySigningPublicKey\":\"ycrLzM3Oz9DR0tPU1dbX2Nna29zd3t_g4eLj5OXm5-g\",\"recoveryEncryptionPublicKey\":\"6err7O3u7_Dx8vP09fb3-Pn6-_z9_v8AAQIDBAUGBwg\",\"signerDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"signature\":\"PpPsk92q7i3W5rDxYts-r-eutHqZ3AWvwCxrsLj0JyvaqXZWfVXzE6REcG4eZlM6hY_kXQ_fc4o78FqAXWNGAA\"},\"current\":{\"protocol\":\"FG-MEMBERSHIP-1\",\"accountId\":\"22222222-2222-4222-8222-222222222222\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"baseRevision\":1,\"proposedRevision\":2,\"keyEpoch\":2,\"previousDigest\":\"f6blV7A9C7M7I5K5X8lm1TEpLtBXARhKYwqxmFa_Mwk\",\"members\":[{\"deviceId\":\"11111111-1111-4111-8111-111111111111\",\"signingPublicKey\":\"ebVWLo_mVPlAeLES6KmLp5AfhTrmlb7X4OORC60ElmQ\",\"encryptionPublicKey\":\"V3-u8AYN_QDAOScrxv58Qmic4W20e2_CqkHRmBn_qTY\",\"status\":\"Active\",\"roles\":[\"ManageDevices\",\"Read\",\"StartNormal\",\"StartStrict\",\"WriteSettings\"]},{\"deviceId\":\"55555555-5555-4555-8555-555555555555\",\"signingPublicKey\":\"rcFAEfgtHFbZVqpPnXPYhYNhpgYEhSXg0Ixjjcdd2Mc\",\"encryptionPublicKey\":\"kjdZZ00a9DxSLojIyhEb6L0CmMwXB0KKjN49XVtODFo\",\"status\":\"Pending\",\"roles\":[]}],\"recoverySigningPublicKey\":\"ycrLzM3Oz9DR0tPU1dbX2Nna29zd3t_g4eLj5OXm5-g\",\"recoveryEncryptionPublicKey\":\"6err7O3u7_Dx8vP09fb3-Pn6-_z9_v8AAQIDBAUGBwg\",\"signerDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"signature\":\"t3ir_to5c5wptTN0BAbn5iwsKMWuLJMulkwqxxIZ5ZTInS_8hBU2AT3jOArFsKIlvGwYZAkmlpgwYdgFyGkeAA\"},\"digest\":\"z7xGVPxri0l6TSkvS4hPyt5gb6J98wJfz5y3Xbmm0sM\",\"envelope\":{\"suite\":\"FG-SODIUM-BOX-1\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"keyEpoch\":2,\"recipientId\":\"55555555-5555-4555-8555-555555555555\",\"recipientPublicKey\":\"kjdZZ00a9DxSLojIyhEb6L0CmMwXB0KKjN49XVtODFo\",\"membershipDigest\":\"z7xGVPxri0l6TSkvS4hPyt5gb6J98wJfz5y3Xbmm0sM\",\"ciphertext\":\"-CP9jnVIPwwJ1DycJwQDHog-LRy0uBFV0CGfDZFskWtYigR-U-3SvUEwF3oRnwGxZQJLazEaT20GSNUWBB-IKFVED6cVKUHZhEnc8g5C07IovaAwAqGlfK9jnFfF7TPbriMyx-xuWJgCYNuGe7wyub0E3T-SwiNY0SgAKD827uhIdIGNHh2A_byPOBK56yy89BhfRjQiw2LJqVmU51iyFGKaBvtFgMj4K7G_nJ2GgdxZ_RZlLBGwPAjThJo3w3pLy1eARpoBrDgxSqVGZqc-_7GHltBWW56XpAYRqushyuFl8_t-8eI1bNwW6UEaT-uhZnEnpERx1CQrriXyj0Dbw7dlbcirIr5hgu02LT5qxYg70ZYVKM92amTsLnwPgeLJp1xkSWA9nzlfMsO3JsbUf0IE5Wz-ufUtkxPmRtT2w_EHFV2XB6DbAN7HFisswdoyyKaTyZ-R3ggyfQgvRabB-dxdMRFpRYZ7RrLlX2vbfGrBa_7z4qY\",\"signerDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"signature\":\"bve8uX8O2fUm_tTA-Ug348npTOXVHb3uP8GOYd_UdYK16RKe_wys5DSpBz6sJiu7iUqn6y0Tf7U40klIKt2QCw\"},\"unauthorizedCurrent\":{\"protocol\":\"FG-MEMBERSHIP-1\",\"accountId\":\"22222222-2222-4222-8222-222222222222\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"baseRevision\":1,\"proposedRevision\":2,\"keyEpoch\":2,\"previousDigest\":\"f6blV7A9C7M7I5K5X8lm1TEpLtBXARhKYwqxmFa_Mwk\",\"members\":[{\"deviceId\":\"11111111-1111-4111-8111-111111111111\",\"signingPublicKey\":\"ebVWLo_mVPlAeLES6KmLp5AfhTrmlb7X4OORC60ElmQ\",\"encryptionPublicKey\":\"V3-u8AYN_QDAOScrxv58Qmic4W20e2_CqkHRmBn_qTY\",\"status\":\"Revoked\",\"roles\":[]},{\"deviceId\":\"55555555-5555-4555-8555-555555555555\",\"signingPublicKey\":\"rcFAEfgtHFbZVqpPnXPYhYNhpgYEhSXg0Ixjjcdd2Mc\",\"encryptionPublicKey\":\"kjdZZ00a9DxSLojIyhEb6L0CmMwXB0KKjN49XVtODFo\",\"status\":\"Active\",\"roles\":[\"ManageDevices\",\"Read\",\"StartNormal\",\"StartStrict\",\"WriteSettings\"]}],\"recoverySigningPublicKey\":\"ycrLzM3Oz9DR0tPU1dbX2Nna29zd3t_g4eLj5OXm5-g\",\"recoveryEncryptionPublicKey\":\"6err7O3u7_Dx8vP09fb3-Pn6-_z9_v8AAQIDBAUGBwg\",\"signerDeviceId\":\"55555555-5555-4555-8555-555555555555\",\"signature\":\"2KrokA_cLsQ5Ps-wDhc2cSomxJ5GTiRVlpxiFSFV_XZp8C1j2vQ0kBRvdHDO413TstX7tbYbXIjiX-QDgFPxCw\"}}";
    private static final String CSHARP_CONTENT_DOCUMENT = "{\"protocol\":\"FG-E2EE-1\",\"suite\":\"XCHACHA20-POLY1305-IETF\",\"accountId\":\"22222222-2222-4222-8222-222222222222\",\"generationId\":\"33333333-3333-4333-8333-333333333333\",\"entityId\":\"44444444-4444-4444-8444-444444444444\",\"kind\":\"profile.settings\",\"baseRevision\":0,\"proposedRevision\":1,\"keyEpoch\":2,\"membershipRevision\":2,\"authorDeviceId\":\"11111111-1111-4111-8111-111111111111\",\"nonce\":\"CX0of4r1rN1yvMYmm1lFkT_Pac7YklYK\",\"ciphertext\":\"MqECxKywwvectkKDJCbO1nponxfvqLLQlFWkSjsSXjbQcKFOF0pHJc_FVHj-rM-3XWNtT5M\",\"signature\":\"1thsZjj0mnUD44Le5Y3PMFLbYvlDRD3RbL8VI8A4caWHp9ZKRJBJwVwMUbwVYvgAxYy7W2Hf8HljZc8L9hl8Cg\"}";
    private AndroidMembershipRuntimeProbe() { }

    public static String run(Context unused) {
        byte[] signerSeed = sequence(1, 32);
        byte[] signerBoxSeed = sequence(33, 32);
        byte[] recipientSeed = sequence(65, 32);
        byte[] recipientBoxSeed = sequence(97, 32);
        byte[] contentKey = sequence(129, 32);
        AndroidDeviceIdentity signer = null;
        AndroidDeviceIdentity recipient = null;
        byte[] opened = null;
        byte[] openedDocument = null;
        try {
            signer = AndroidDeviceIdentity.fromSeedsForProbe(SIGNER_ID, signerSeed, signerBoxSeed);
            recipient = AndroidDeviceIdentity.fromSeedsForProbe(RECIPIENT_ID, recipientSeed, recipientBoxSeed);
            final AndroidDeviceIdentity signerIdentity = signer;
            final AndroidDeviceIdentity recipientIdentity = recipient;
            AndroidMembershipVerifier.CSharpMembershipFixture fixture =
                    AndroidMembershipVerifier.parseCSharpFixture(CSHARP_FIXTURE, CSHARP_CONTENT_DOCUMENT);
            AndroidMembershipVerifier.VerifiedMembership verified = AndroidMembershipVerifier.verifyChain(
                    Arrays.asList(fixture.initial(), fixture.current()), ACCOUNT_ID, GENERATION_ID);
            require(fixture.digest().equals(verified.digest()), "MEMBERSHIP_CSHARP_DIGEST_MISMATCH");
            require(verified.chainLength() == 2 && verified.revision() == 2 && verified.keyEpoch() == 2,
                    "MEMBERSHIP_CHAIN_METADATA_MISMATCH");
            require(verified.hasActiveRole(SIGNER_ID, "ManageDevices"), "MEMBERSHIP_MANAGER_ROLE_MISSING");
            require(!verified.hasActiveRole(RECIPIENT_ID, "Read"), "PENDING_RECIPIENT_READ_AUTHORIZED");

            AndroidSignedKeyEnvelopeCodec.EnvelopeContext envelopeContext =
                    new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(fixture.current().accountId(),
                            fixture.current().generationId(), fixture.current().keyEpoch(), RECIPIENT_ID,
                            fixture.digest(), SIGNER_ID);
            opened = AndroidSignedKeyEnvelopeCodec.openForDevice(fixture.envelope(), envelopeContext,
                    signerIdentity.publicIdentity(), recipientIdentity, verified);
            require(MessageDigestEqual(opened, contentKey), "MEMBERSHIP_GATED_ENVELOPE_OPEN_FAILED");
            Arrays.fill(opened, (byte) 0);
            opened = null;

            AndroidContentDocumentCodec.DocumentContext documentContext =
                    new AndroidContentDocumentCodec.DocumentContext(fixture.current().accountId(),
                            fixture.current().generationId(), "44444444-4444-4444-8444-444444444444",
                            "profile.settings", 0, 1, fixture.current().keyEpoch(), fixture.current().proposedRevision());
            openedDocument = AndroidContentDocumentCodec.decrypt(fixture.contentDocument(), documentContext,
                    contentKey, signerIdentity.publicIdentity(), verified);
            byte[] expectedDocument = "{\"durationMinutes\":25,\"theme\":\"mist\"}".getBytes(StandardCharsets.UTF_8);
            try { require(MessageDigestEqual(openedDocument, expectedDocument), "MEMBERSHIP_GATED_DOCUMENT_OPEN_FAILED"); }
            finally { Arrays.fill(expectedDocument, (byte) 0); }
            Arrays.fill(openedDocument, (byte) 0);
            openedDocument = null;
            verified.authorizeDocumentRead(documentContext, SIGNER_ID, signerIdentity.publicIdentity());
            verified.authorizeDocumentWrite(documentContext, SIGNER_ID, signerIdentity.publicIdentity());
            expectRejected(() -> verified.authorizeDocumentRead(documentContext, RECIPIENT_ID,
                    recipientIdentity.publicIdentity()), "PENDING_DEVICE_DOCUMENT_READ_ACCEPTED");
            expectRejected(() -> verified.authorizeKeyEnvelope(new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(
                    envelopeContext.accountId(), envelopeContext.generationId(), envelopeContext.keyEpoch(),
                    envelopeContext.recipientId(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", envelopeContext.signerDeviceId()),
                    signerIdentity.publicIdentity(), recipientIdentity), "WRONG_MEMBERSHIP_DIGEST_ACCEPTED");
            expectRejected(() -> verified.authorizeDocumentRead(new AndroidContentDocumentCodec.DocumentContext(
                    documentContext.accountId(), documentContext.generationId(), documentContext.entityId(),
                    documentContext.kind(), 0, 1, 2, 3), SIGNER_ID, signerIdentity.publicIdentity()),
                    "WRONG_DOCUMENT_MEMBERSHIP_REVISION_ACCEPTED");
            expectRejected(() -> AndroidMembershipVerifier.verifyChain(
                    Arrays.asList(fixture.initial(), fixture.unauthorizedCurrent()), ACCOUNT_ID, GENERATION_ID),
                    "PENDING_MANAGER_PROMOTION_ACCEPTED");
            expectRejected(() -> AndroidMembershipVerifier.verifyChain(Arrays.asList(fixture.current()), ACCOUNT_ID, GENERATION_ID),
                    "DETACHED_MEMBERSHIP_SNAPSHOT_ACCEPTED");
            expectRejected(() -> AndroidMembershipVerifier.verifyChain(Arrays.asList(fixture.initial(), fixture.current()),
                    "66666666-6666-4666-8666-666666666666", GENERATION_ID), "CROSS_ACCOUNT_MEMBERSHIP_ACCEPTED");
            expectRejected(() -> AndroidMembershipVerifier.parseCSharpFixture(CSHARP_FIXTURE
                    .substring(0, CSHARP_FIXTURE.length() - 1) + ",\"unknown\":true}", CSHARP_CONTENT_DOCUMENT),
                    "UNKNOWN_MEMBERSHIP_FIXTURE_FIELD_ACCEPTED");

            return "MEMBERSHIP_CSHARP_CHAIN_PASS · anchor/signatures/digest/contiguity"
                    + "\nMEMBERSHIP_ACTIVE_MANAGER_PASS · Pending recipient has no Read access"
                    + "\nMEMBERSHIP_GATED_KEY_ENVELOPE_PASS · signer ManageDevices, recipient Pending"
                    + "\nMEMBERSHIP_GATED_DOCUMENT_PASS · author Active/Read/WriteSettings at signed revision"
                    + "\nMEMBERSHIP_PENDING_READ/DIGEST/REVISION/DETACHED/UNAUTHORIZED_SIGNER_REJECTED_PASS";
        } catch (Exception | LinkageError exception) {
            return "ANDROID_MEMBERSHIP_PROBE_FAILED: " + exception.getClass().getSimpleName()
                    + (exception.getMessage() == null ? "" : " · " + exception.getMessage());
        } finally {
            Arrays.fill(signerSeed, (byte) 0);
            Arrays.fill(signerBoxSeed, (byte) 0);
            Arrays.fill(recipientSeed, (byte) 0);
            Arrays.fill(recipientBoxSeed, (byte) 0);
            Arrays.fill(contentKey, (byte) 0);
            if (opened != null) Arrays.fill(opened, (byte) 0);
            if (openedDocument != null) Arrays.fill(openedDocument, (byte) 0);
            if (signer != null) signer.close();
            if (recipient != null) recipient.close();
        }
    }

    private static byte[] sequence(int first, int length) {
        byte[] result = new byte[length];
        for (int index = 0; index < length; index++) result[index] = (byte) (first + index);
        return result;
    }

    private static boolean MessageDigestEqual(byte[] left, byte[] right) {
        return java.security.MessageDigest.isEqual(left, right);
    }

    private static void expectRejected(CheckedAction action, String failure) throws Exception {
        try {
            action.run();
            throw new GeneralSecurityException(failure);
        } catch (GeneralSecurityException expected) {
            if (failure.equals(expected.getMessage())) throw expected;
        }
    }

    private static void require(boolean condition, String failure) throws GeneralSecurityException {
        if (!condition) throw new GeneralSecurityException(failure);
    }

    @FunctionalInterface
    private interface CheckedAction { void run() throws GeneralSecurityException; }
}
