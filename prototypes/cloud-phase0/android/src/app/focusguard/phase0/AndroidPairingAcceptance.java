package app.focusguard.phase0;

import org.json.JSONArray;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.IOException;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/** Verifies an approved pairing chain and opens only this Android installation's signed key envelope. */
public final class AndroidPairingAcceptance {
    private static final int MAX_STATE_JSON_BYTES = 1_000_000;
    private static final int MAX_KEY_ENVELOPES = 11;

    private AndroidPairingAcceptance() { }

    public static AcceptedKey verifyAndOpen(String membershipJson, String membershipChainJson,
            String keyEnvelopesJson, String accountId, String generationId, long expectedMembershipRevision,
            String expectedMembershipDigest, AndroidDeviceIdentity identity) throws GeneralSecurityException {
        if (membershipJson == null || membershipChainJson == null || keyEnvelopesJson == null || identity == null
                || membershipJson.length() > 100_000 || membershipChainJson.length() > MAX_STATE_JSON_BYTES
                || keyEnvelopesJson.length() > 200_000)
            throw new GeneralSecurityException("Approved pairing material size rejected.");
        byte[] membershipCanonical = null;
        byte[] chainCanonical = null;
        byte[] envelopesCanonical = null;
        byte[] contentKey = null;
        try {
            membershipCanonical = new JsonCanonicalizer(membershipJson).getEncodedUTF8();
            chainCanonical = new JsonCanonicalizer(membershipChainJson).getEncodedUTF8();
            envelopesCanonical = new JsonCanonicalizer(keyEnvelopesJson).getEncodedUTF8();
            JSONArray chain = new JSONArray(membershipChainJson);
            if (chain.length() < 1 || chain.length() > 256)
                throw new GeneralSecurityException("Approved membership chain length rejected.");
            byte[] chainCurrentCanonical = new JsonCanonicalizer(
                    chain.getJSONObject(chain.length() - 1).toString()).getEncodedUTF8();
            try {
                if (!MessageDigest.isEqual(membershipCanonical, chainCurrentCanonical))
                    throw new GeneralSecurityException("Approved membership does not match its verified chain.");
            } finally { Arrays.fill(chainCurrentCanonical, (byte) 0); }

            AndroidMembershipVerifier.VerifiedMembership verified = AndroidMembershipVerifier.verifyChainJson(
                    membershipChainJson, accountId, generationId);
            AndroidMembershipVerifier.Document current = verified.currentDocument();
            if (!accountId.equals(current.accountId()) || !generationId.equals(current.generationId())
                    || current.proposedRevision() != expectedMembershipRevision
                    || !expectedMembershipDigest.equals(verified.digest()))
                throw new GeneralSecurityException("Approved membership context rejected.");

            AndroidDeviceIdentity.PublicIdentity local = identity.publicIdentity();
            AndroidMembershipVerifier.Member localMember = find(current, local.deviceId());
            if (localMember == null || !"Active".equals(localMember.status())
                    || !local.deviceId().equals(localMember.deviceId())
                    || !local.signingPublicKey().equals(localMember.signingPublicKey())
                    || !local.encryptionPublicKey().equals(localMember.encryptionPublicKey())
                    || hasRole(localMember, "StartStrict"))
                throw new GeneralSecurityException("Approved Android member does not match this installation.");

            String signerId = current.signerDeviceId();
            AndroidMembershipVerifier.Member signerMember = find(current, signerId);
            if (signerMember == null || !verified.hasActiveRole(signerId, "ManageDevices"))
                throw new GeneralSecurityException("Approved pairing signer is not authorized.");
            AndroidDeviceIdentity.PublicIdentity signer = AndroidDeviceIdentity.importPublicIdentity(
                    signerMember.deviceId(), signerMember.signingPublicKey(), signerMember.encryptionPublicKey());

            JSONArray envelopes = new JSONArray(keyEnvelopesJson);
            Map<String, String> expectedRecipients = new HashMap<>();
            for (AndroidMembershipVerifier.Member member : current.members())
                if ("Active".equals(member.status()))
                    expectedRecipients.put(member.deviceId(), member.encryptionPublicKey());
            if (expectedRecipients.put(accountId, current.recoveryEncryptionPublicKey()) != null
                    || envelopes.length() != expectedRecipients.size()
                    || envelopes.length() > MAX_KEY_ENVELOPES)
                throw new GeneralSecurityException("Approved key envelope set rejected.");

            Set<String> recipients = new HashSet<>();
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope selected = null;
            for (int index = 0; index < envelopes.length(); index++) {
                JSONObject envelopeJson = envelopes.getJSONObject(index);
                AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope =
                        AndroidSignedKeyEnvelopeCodec.fromJson(envelopeJson.toString());
                if (!recipients.add(envelope.recipientId()))
                    throw new GeneralSecurityException("Duplicate approved key recipient.");
                String expectedRecipientKey = expectedRecipients.get(envelope.recipientId());
                if (expectedRecipientKey == null || !expectedRecipientKey.equals(envelope.recipientPublicKey())
                        || !generationId.equals(envelope.generationId())
                        || envelope.keyEpoch() != verified.keyEpoch()
                        || !verified.digest().equals(envelope.membershipDigest())
                        || !signerId.equals(envelope.signerDeviceId()))
                    throw new GeneralSecurityException("Approved key envelope recipient or context rejected.");
                AndroidSignedKeyEnvelopeCodec.verifySignedHeader(envelope, signer);
                if (local.deviceId().equals(envelope.recipientId())) selected = envelope;
            }
            if (selected == null || !recipients.equals(expectedRecipients.keySet()))
                throw new GeneralSecurityException("Approved key envelope recipients are incomplete.");

            AndroidSignedKeyEnvelopeCodec.EnvelopeContext context = new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(
                    accountId, generationId, verified.keyEpoch(), local.deviceId(), verified.digest(), signerId);
            contentKey = AndroidSignedKeyEnvelopeCodec.openForDevice(selected, context, signer, identity, verified);
            if (contentKey.length != 32) throw new GeneralSecurityException("Approved account key length rejected.");
            byte[] transferredKey = contentKey;
            contentKey = null;
            return new AcceptedKey(transferredKey, verified.revision(), verified.keyEpoch(), verified.digest());
        } catch (org.json.JSONException | IOException exception) {
            throw new GeneralSecurityException("Approved pairing JSON rejected.", exception);
        } finally {
            if (membershipCanonical != null) Arrays.fill(membershipCanonical, (byte) 0);
            if (chainCanonical != null) Arrays.fill(chainCanonical, (byte) 0);
            if (envelopesCanonical != null) Arrays.fill(envelopesCanonical, (byte) 0);
            if (contentKey != null) Arrays.fill(contentKey, (byte) 0);
        }
    }

    private static AndroidMembershipVerifier.Member find(AndroidMembershipVerifier.Document document, String id) {
        for (AndroidMembershipVerifier.Member member : document.members())
            if (id.equals(member.deviceId())) return member;
        return null;
    }

    private static boolean hasRole(AndroidMembershipVerifier.Member member, String role) {
        for (String item : member.roles()) if (role.equals(item)) return true;
        return false;
    }

    public static final class AcceptedKey implements AutoCloseable {
        private byte[] contentKey;
        private final long membershipRevision;
        private final long keyEpoch;
        private final String membershipDigest;

        private AcceptedKey(byte[] contentKey, long membershipRevision, long keyEpoch, String membershipDigest) {
            this.contentKey = contentKey;
            this.membershipRevision = membershipRevision;
            this.keyEpoch = keyEpoch;
            this.membershipDigest = membershipDigest;
        }

        public byte[] copyContentKey() {
            if (contentKey == null) throw new IllegalStateException("Accepted key was cleared.");
            return contentKey.clone();
        }

        public long membershipRevision() { return membershipRevision; }
        public long keyEpoch() { return keyEpoch; }
        public String membershipDigest() { return membershipDigest; }

        @Override public void close() {
            if (contentKey != null) Arrays.fill(contentKey, (byte) 0);
            contentKey = null;
        }
    }
}
