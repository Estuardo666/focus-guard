package app.focusguard.phase0;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Base64;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** Verifies the signed membership chain before a caller trusts a signer or recipient. */
public final class AndroidMembershipVerifier {
    public static final String PROTOCOL = "FG-MEMBERSHIP-1";
    private static final String SIGNATURE_DOMAIN = PROTOCOL;
    private static final long MAX_SAFE_INTEGER = 9007199254740991L;
    private static final int MAX_MEMBERSHIP_CHAIN_LENGTH = 256;
    private static final int MAX_MEMBERSHIP_CHAIN_JSON_BYTES = 1_000_000;
    private static final Set<String> ALLOWED_ROLES = new HashSet<>(Arrays.asList(
            "Read", "WriteSettings", "StartNormal", "StartStrict", "ManageDevices"));
    private static final List<String> INITIAL_ROLES = Arrays.asList(
            "ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings");

    private AndroidMembershipVerifier() { }

    public static final class Member {
        private final String deviceId;
        private final String signingPublicKey;
        private final String encryptionPublicKey;
        private final String status;
        private final String[] roles;

        public Member(String deviceId, String signingPublicKey, String encryptionPublicKey,
                String status, String[] roles) {
            this.deviceId = deviceId;
            this.signingPublicKey = signingPublicKey;
            this.encryptionPublicKey = encryptionPublicKey;
            this.status = status;
            this.roles = roles == null ? null : roles.clone();
        }

        public String deviceId() { return deviceId; }
        public String signingPublicKey() { return signingPublicKey; }
        public String encryptionPublicKey() { return encryptionPublicKey; }
        public String status() { return status; }
        public String[] roles() { return roles == null ? null : roles.clone(); }
        boolean hasRole(String role) { return roles != null && Arrays.asList(roles).contains(role); }
    }

    public static final class Document {
        private final String protocol;
        private final String accountId;
        private final String generationId;
        private final long baseRevision;
        private final long proposedRevision;
        private final long keyEpoch;
        private final String previousDigest;
        private final Member[] members;
        private final String recoverySigningPublicKey;
        private final String recoveryEncryptionPublicKey;
        private final String signerDeviceId;
        private final String signature;

        public Document(String protocol, String accountId, String generationId, long baseRevision,
                long proposedRevision, long keyEpoch, String previousDigest, Member[] members,
                String recoverySigningPublicKey, String recoveryEncryptionPublicKey,
                String signerDeviceId, String signature) {
            this.protocol = protocol;
            this.accountId = accountId;
            this.generationId = generationId;
            this.baseRevision = baseRevision;
            this.proposedRevision = proposedRevision;
            this.keyEpoch = keyEpoch;
            this.previousDigest = previousDigest;
            this.members = members == null ? null : members.clone();
            this.recoverySigningPublicKey = recoverySigningPublicKey;
            this.recoveryEncryptionPublicKey = recoveryEncryptionPublicKey;
            this.signerDeviceId = signerDeviceId;
            this.signature = signature;
        }

        public String protocol() { return protocol; }
        public String accountId() { return accountId; }
        public String generationId() { return generationId; }
        public long baseRevision() { return baseRevision; }
        public long proposedRevision() { return proposedRevision; }
        public long keyEpoch() { return keyEpoch; }
        public String previousDigest() { return previousDigest; }
        public Member[] members() { return members == null ? null : members.clone(); }
        public String recoverySigningPublicKey() { return recoverySigningPublicKey; }
        public String recoveryEncryptionPublicKey() { return recoveryEncryptionPublicKey; }
        public String signerDeviceId() { return signerDeviceId; }
        public String signature() { return signature; }
    }

    public static final class CSharpMembershipFixture {
        private final Document initial;
        private final Document current;
        private final Document unauthorizedCurrent;
        private final String digest;
        private final AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope;
        private final AndroidContentDocumentCodec.EncryptedDocument contentDocument;

        private CSharpMembershipFixture(Document initial, Document current, Document unauthorizedCurrent,
                String digest, AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope,
                AndroidContentDocumentCodec.EncryptedDocument contentDocument) {
            this.initial = initial;
            this.current = current;
            this.unauthorizedCurrent = unauthorizedCurrent;
            this.digest = digest;
            this.envelope = envelope;
            this.contentDocument = contentDocument;
        }

        public Document initial() { return initial; }
        public Document current() { return current; }
        public Document unauthorizedCurrent() { return unauthorizedCurrent; }
        public String digest() { return digest; }
        public AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope() { return envelope; }
        public AndroidContentDocumentCodec.EncryptedDocument contentDocument() { return contentDocument; }
    }

    public static final class VerifiedMembership {
        private final List<Document> chain;
        private final Document current;
        private final String digest;

        private VerifiedMembership(List<Document> chain, Document current, String digest) {
            this.chain = Collections.unmodifiableList(new ArrayList<>(chain));
            this.current = current;
            this.digest = digest;
        }

        public String accountId() { return current.accountId(); }
        public String generationId() { return current.generationId(); }
        public long revision() { return current.proposedRevision(); }
        public long keyEpoch() { return current.keyEpoch(); }
        public String digest() { return digest; }
        public Document currentDocument() { return current; }

        public void authorizeKeyEnvelope(AndroidSignedKeyEnvelopeCodec.EnvelopeContext context,
                AndroidDeviceIdentity.PublicIdentity signer, AndroidDeviceIdentity recipient)
                throws GeneralSecurityException {
            if (!context.accountId().equals(current.accountId())
                    || !context.generationId().equals(current.generationId())
                    || context.keyEpoch() != current.keyEpoch()
                    || !context.membershipDigest().equals(digest))
                throw new GeneralSecurityException("Envelope does not match the verified membership snapshot.");
            Member authorizedSigner = find(current, context.signerDeviceId());
            requireActiveRole(authorizedSigner, "ManageDevices");
            requireSameKeys(authorizedSigner, signer);
            Member recipientMember = find(current, context.recipientId());
            if (recipientMember == null || !("Pending".equals(recipientMember.status()) || "Active".equals(recipientMember.status())))
                throw new GeneralSecurityException("Recipient is not pending or active in the verified snapshot.");
            requireSameKeys(recipientMember, recipient.publicIdentity());
        }

        public void authorizeKeyEnvelopeForRecipient(AndroidSignedKeyEnvelopeCodec.EnvelopeContext context,
                AndroidDeviceIdentity.PublicIdentity signer, String recipientEncryptionPublicKey)
                throws GeneralSecurityException {
            if (!context.accountId().equals(current.accountId())
                    || !context.generationId().equals(current.generationId())
                    || context.keyEpoch() != current.keyEpoch()
                    || !context.membershipDigest().equals(digest))
                throw new GeneralSecurityException("Envelope does not match the verified membership snapshot.");
            Member authorizedSigner = find(current, context.signerDeviceId());
            requireActiveRole(authorizedSigner, "ManageDevices");
            requireSameKeys(authorizedSigner, signer);
            Member recipientMember = find(current, context.recipientId());
            if (recipientMember == null || !("Pending".equals(recipientMember.status()) || "Active".equals(recipientMember.status()))
                    || !recipientMember.encryptionPublicKey().equals(recipientEncryptionPublicKey))
                throw new GeneralSecurityException("Envelope recipient is not pending/active with this key.");
        }

        public void authorizeKeyEnvelopeForRecovery(AndroidSignedKeyEnvelopeCodec.EnvelopeContext context,
                AndroidDeviceIdentity.PublicIdentity signer, String recoveryEncryptionPublicKey)
                throws GeneralSecurityException {
            if (!context.accountId().equals(current.accountId())
                    || !context.generationId().equals(current.generationId())
                    || context.keyEpoch() != current.keyEpoch()
                    || !context.membershipDigest().equals(digest)
                    || !context.recipientId().equals(current.accountId())
                    || !current.recoveryEncryptionPublicKey().equals(recoveryEncryptionPublicKey))
                throw new GeneralSecurityException("Recovery envelope does not match the verified membership snapshot.");
            Member authorizedSigner = find(current, context.signerDeviceId());
            requireActiveRole(authorizedSigner, "ManageDevices");
            requireSameKeys(authorizedSigner, signer);
        }

        public void authorizeDocumentRead(AndroidContentDocumentCodec.DocumentContext context,
                String authorDeviceId, AndroidDeviceIdentity.PublicIdentity author) throws GeneralSecurityException {
            if (!context.accountId().equals(current.accountId())
                    || !context.generationId().equals(current.generationId())
                    || context.membershipRevision() != current.proposedRevision()
                    || context.keyEpoch() != current.keyEpoch())
                throw new GeneralSecurityException("Document does not match the verified membership snapshot.");
            Member activeAuthor = find(current, authorDeviceId);
            requireActiveRole(activeAuthor, "Read");
            requireSameKeys(activeAuthor, author);
        }

        public void authorizeDocumentWrite(AndroidContentDocumentCodec.DocumentContext context,
                String authorDeviceId, AndroidDeviceIdentity.PublicIdentity author) throws GeneralSecurityException {
            if (!context.accountId().equals(current.accountId())
                    || !context.generationId().equals(current.generationId())
                    || context.membershipRevision() != current.proposedRevision()
                    || context.keyEpoch() != current.keyEpoch())
                throw new GeneralSecurityException("Document does not match the verified membership snapshot.");
            Member activeAuthor = find(current, authorDeviceId);
            requireActiveRole(activeAuthor, "WriteSettings");
            requireSameKeys(activeAuthor, author);
        }

        public boolean hasActiveRole(String deviceId, String role) {
            Member member = find(current, deviceId);
            return member != null && "Active".equals(member.status()) && member.hasRole(role);
        }

        public int chainLength() { return chain.size(); }
    }

    public static CSharpMembershipFixture parseCSharpFixture(String json, String contentDocumentJson)
            throws GeneralSecurityException {
        if (json == null || json.length() > 50000 || contentDocumentJson == null || contentDocumentJson.length() > 400000)
            throw new GeneralSecurityException("Membership fixture size rejected.");
        byte[] canonical = null;
        try {
            JSONObject root = new JSONObject(json);
            canonical = new JsonCanonicalizer(json).getEncodedUTF8();
            Set<String> fields = keys(root);
            if (!fields.equals(new HashSet<>(Arrays.asList("status", "initial", "current", "digest", "envelope",
                    "unauthorizedCurrent")))
                    || !"PASS".equals(root.getString("status")))
                throw new GeneralSecurityException("Membership fixture fields rejected.");
            return new CSharpMembershipFixture(parseDocument(root.getJSONObject("initial")),
                    parseDocument(root.getJSONObject("current")), parseDocument(root.getJSONObject("unauthorizedCurrent")),
                    root.getString("digest"), AndroidSignedKeyEnvelopeCodec.fromJson(root.getJSONObject("envelope").toString()),
                    AndroidContentDocumentCodec.fromJson(contentDocumentJson));
        } catch (JSONException | IOException exception) {
            throw new GeneralSecurityException("Membership fixture JSON rejected.", exception);
        } finally { if (canonical != null) Arrays.fill(canonical, (byte) 0); }
    }

    /** expectedAccountId/generationId must come from the authenticated account session, not from this chain. */
    public static VerifiedMembership verifyChain(List<Document> documents, String expectedAccountId,
            String expectedGenerationId) throws GeneralSecurityException {
        if (documents == null || documents.isEmpty() || documents.size() > MAX_MEMBERSHIP_CHAIN_LENGTH)
            throw new GeneralSecurityException("Membership chain length rejected.");
        requireId(expectedAccountId);
        requireId(expectedGenerationId);
        Document first = documents.get(0);
        if (first == null || !expectedAccountId.equals(first.accountId())
                || !expectedGenerationId.equals(first.generationId()))
            throw new GeneralSecurityException("Membership chain does not match the authenticated account.");
        List<Document> verified = new ArrayList<>(documents.size());
        Document previous = null;
        String previousDigest = null;
        for (Document document : documents) {
            validateDocument(document, true);
            if (previous == null) validateGenesis(document);
            else validateLink(previous, previousDigest, document);
            byte[] canonical = canonicalMembership(document);
            byte[] domain = domainBytes(canonical);
            byte[] digest = null;
            try {
                digest = MessageDigest.getInstance("SHA-256").digest(domain);
                previousDigest = encode(digest);
            } catch (java.security.GeneralSecurityException exception) {
                throw new GeneralSecurityException("Membership digest could not be calculated.", exception);
            } finally {
                Arrays.fill(canonical, (byte) 0);
                Arrays.fill(domain, (byte) 0);
                if (digest != null) Arrays.fill(digest, (byte) 0);
            }
            verified.add(document);
            previous = document;
        }
        return new VerifiedMembership(verified, previous, previousDigest);
    }

    public static Document parseDocumentJson(String json) throws GeneralSecurityException {
        if (json == null || json.isEmpty() || json.length() > MAX_MEMBERSHIP_CHAIN_JSON_BYTES)
            throw new GeneralSecurityException("Membership document size rejected.");
        byte[] canonical = null;
        try {
            canonical = new JsonCanonicalizer(json).getEncodedUTF8();
            return parseDocument(new JSONObject(json));
        } catch (JSONException | IOException exception) {
            throw new GeneralSecurityException("Membership document JSON rejected.", exception);
        } finally { if (canonical != null) Arrays.fill(canonical, (byte) 0); }
    }

    public static VerifiedMembership verifyChainJson(String json, String expectedAccountId,
            String expectedGenerationId) throws GeneralSecurityException {
        if (json == null || json.isEmpty() || json.length() > MAX_MEMBERSHIP_CHAIN_JSON_BYTES)
            throw new GeneralSecurityException("Membership chain JSON size rejected.");
        byte[] canonical = null;
        try {
            canonical = new JsonCanonicalizer(json).getEncodedUTF8();
            JSONArray array = new JSONArray(json);
            if (array.length() < 1 || array.length() > MAX_MEMBERSHIP_CHAIN_LENGTH)
                throw new GeneralSecurityException("Membership chain length rejected.");
            List<Document> documents = new ArrayList<>(array.length());
            for (int index = 0; index < array.length(); index++)
                documents.add(parseDocument(array.getJSONObject(index)));
            return verifyChain(documents, expectedAccountId, expectedGenerationId);
        } catch (JSONException | IOException exception) {
            throw new GeneralSecurityException("Membership chain JSON rejected.", exception);
        } finally { if (canonical != null) Arrays.fill(canonical, (byte) 0); }
    }

    private static void validateDocument(Document document, boolean requireSignature) throws GeneralSecurityException {
        if (document == null || !PROTOCOL.equals(document.protocol()) || document.baseRevision() < 0
                || document.proposedRevision() != document.baseRevision() + 1
                || document.proposedRevision() > MAX_SAFE_INTEGER || document.keyEpoch() < 1
                || document.keyEpoch() > MAX_SAFE_INTEGER)
            throw new GeneralSecurityException("Membership revision or protocol rejected.");
        requireId(document.accountId());
        requireId(document.generationId());
        requireId(document.signerDeviceId());
        byte[] previousDigestBytes = decodeExact(document.previousDigest(), 32);
        Arrays.fill(previousDigestBytes, (byte) 0);
        byte[] recoverySign = decodeExact(document.recoverySigningPublicKey(), 32);
        byte[] recoveryEncrypt = decodeExact(document.recoveryEncryptionPublicKey(), 32);
        Arrays.fill(recoverySign, (byte) 0);
        Arrays.fill(recoveryEncrypt, (byte) 0);
        Member[] members = document.members();
        if (members == null || members.length < 1 || members.length > 10)
            throw new GeneralSecurityException("Membership member count rejected.");
        Set<String> ids = new HashSet<>();
        Member signer = null;
        for (Member member : members) {
            if (member == null || !ids.add(member.deviceId()))
                throw new GeneralSecurityException("Duplicate or missing membership member.");
            requireId(member.deviceId());
            if (!("Active".equals(member.status()) || "Pending".equals(member.status())
                    || "Paused".equals(member.status()) || "Revoked".equals(member.status())))
                throw new GeneralSecurityException("Membership status rejected.");
            String[] roles = member.roles();
            if (roles == null || roles.length > 5) throw new GeneralSecurityException("Membership roles rejected.");
            Set<String> seenRoles = new HashSet<>();
            for (String role : roles)
                if (!ALLOWED_ROLES.contains(role) || !seenRoles.add(role))
                    throw new GeneralSecurityException("Unknown or duplicate membership role.");
            byte[] signing = decodeExact(member.signingPublicKey(), 32);
            byte[] encryption = decodeExact(member.encryptionPublicKey(), 32);
            Arrays.fill(signing, (byte) 0);
            Arrays.fill(encryption, (byte) 0);
            if (member.deviceId().equals(document.signerDeviceId())) signer = member;
        }
        if (signer == null) throw new GeneralSecurityException("Membership signer is absent.");
        if (requireSignature) {
            byte[] signature = decodeExact(document.signature(), 64);
            try {
                AndroidDeviceIdentity.PublicIdentity signerIdentity = AndroidDeviceIdentity.importPublicIdentity(
                        signer.deviceId(), signer.signingPublicKey(), signer.encryptionPublicKey());
                byte[] canonical = canonicalMembership(document);
                try {
                    if (!AndroidDeviceIdentity.verify(signerIdentity, SIGNATURE_DOMAIN, canonical, document.signature()))
                        throw new GeneralSecurityException("Membership signature rejected.");
                } finally { Arrays.fill(canonical, (byte) 0); }
            } finally { Arrays.fill(signature, (byte) 0); }
        }
    }

    private static void validateGenesis(Document document) throws GeneralSecurityException {
        byte[] previous = decodeExact(document.previousDigest(), 32);
        try {
            if (document.baseRevision() != 0 || document.proposedRevision() != 1 || document.keyEpoch() != 1
                    || !MessageDigest.isEqual(previous, new byte[32]) || document.members().length != 1
                    || !document.members()[0].deviceId().equals(document.signerDeviceId())
                    || !"Active".equals(document.members()[0].status())
                    || !Arrays.asList(document.members()[0].roles()).containsAll(INITIAL_ROLES)
                    || document.members()[0].roles().length != INITIAL_ROLES.size())
                throw new GeneralSecurityException("Initial membership anchor rejected.");
        } finally { Arrays.fill(previous, (byte) 0); }
    }

    private static void validateLink(Document previous, String previousDigest, Document current)
            throws GeneralSecurityException {
        if (!previous.accountId().equals(current.accountId()) || !previous.generationId().equals(current.generationId())
                || current.baseRevision() != previous.proposedRevision()
                || !current.previousDigest().equals(previousDigest) || current.keyEpoch() < previous.keyEpoch())
            throw new GeneralSecurityException("Membership chain continuity rejected.");
        Member priorSigner = find(previous, current.signerDeviceId());
        requireActiveRole(priorSigner, "ManageDevices");
        Member currentSigner = find(current, current.signerDeviceId());
        requireActiveRole(currentSigner, "ManageDevices");
        requireSameKeys(priorSigner, currentSigner);
        for (Member before : previous.members()) {
            Member after = find(current, before.deviceId());
            if (after != null) requireSameKeys(before, after);
        }
    }

    private static byte[] canonicalMembership(Document document) throws GeneralSecurityException {
        StringBuilder json = new StringBuilder("{\"protocol\":\"").append(document.protocol())
                .append("\",\"accountId\":\"").append(document.accountId())
                .append("\",\"generationId\":\"").append(document.generationId())
                .append("\",\"baseRevision\":").append(document.baseRevision())
                .append(",\"proposedRevision\":").append(document.proposedRevision())
                .append(",\"keyEpoch\":").append(document.keyEpoch())
                .append(",\"previousDigest\":\"").append(document.previousDigest()).append("\",\"members\":[");
        Member[] members = document.members();
        for (int index = 0; index < members.length; index++) {
            Member member = members[index];
            if (index > 0) json.append(',');
            json.append("{\"deviceId\":\"").append(member.deviceId())
                    .append("\",\"signingPublicKey\":\"").append(member.signingPublicKey())
                    .append("\",\"encryptionPublicKey\":\"").append(member.encryptionPublicKey())
                    .append("\",\"status\":\"").append(member.status()).append("\",\"roles\":[");
            String[] roles = member.roles();
            for (int role = 0; role < roles.length; role++) {
                if (role > 0) json.append(',');
                json.append('"').append(roles[role]).append('"');
            }
            json.append("]}");
        }
        json.append("],\"recoverySigningPublicKey\":\"").append(document.recoverySigningPublicKey())
                .append("\",\"recoveryEncryptionPublicKey\":\"").append(document.recoveryEncryptionPublicKey())
                .append("\",\"signerDeviceId\":\"").append(document.signerDeviceId()).append("\"}");
        try { return new JsonCanonicalizer(json.toString()).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Membership canonicalization failed.", exception); }
    }

    private static Document parseDocument(JSONObject json) throws JSONException, GeneralSecurityException {
        Set<String> expected = new HashSet<>(Arrays.asList("protocol", "accountId", "generationId", "baseRevision",
                "proposedRevision", "keyEpoch", "previousDigest", "members", "recoverySigningPublicKey",
                "recoveryEncryptionPublicKey", "signerDeviceId", "signature"));
        if (!keys(json).equals(expected)) throw new GeneralSecurityException("Membership document fields rejected.");
        JSONArray array = json.getJSONArray("members");
        if (array.length() > 10) throw new GeneralSecurityException("Membership member count rejected.");
        Member[] members = new Member[array.length()];
        for (int index = 0; index < array.length(); index++) {
            JSONObject m = array.getJSONObject(index);
            Set<String> memberFields = new HashSet<>(Arrays.asList("deviceId", "signingPublicKey",
                    "encryptionPublicKey", "status", "roles"));
            if (!keys(m).equals(memberFields)) throw new GeneralSecurityException("Membership member fields rejected.");
            JSONArray roleArray = m.getJSONArray("roles");
            String[] roles = new String[roleArray.length()];
            for (int role = 0; role < roles.length; role++) roles[role] = roleArray.getString(role);
            members[index] = new Member(m.getString("deviceId"), m.getString("signingPublicKey"),
                    m.getString("encryptionPublicKey"), m.getString("status"), roles);
        }
        return new Document(json.getString("protocol"), json.getString("accountId"), json.getString("generationId"),
                getLong(json, "baseRevision"), getLong(json, "proposedRevision"), getLong(json, "keyEpoch"),
                json.getString("previousDigest"), members, json.getString("recoverySigningPublicKey"),
                json.getString("recoveryEncryptionPublicKey"), json.getString("signerDeviceId"), json.getString("signature"));
    }

    private static Member find(Document document, String id) {
        for (Member member : document.members()) if (member.deviceId().equals(id)) return member;
        return null;
    }

    private static void requireActiveRole(Member member, String role) throws GeneralSecurityException {
        if (member == null || !"Active".equals(member.status()) || !member.hasRole(role))
            throw new GeneralSecurityException("Membership role authorization rejected.");
    }

    private static void requireSameKeys(Member member, AndroidDeviceIdentity.PublicIdentity identity)
            throws GeneralSecurityException {
        if (identity == null || !member.deviceId().equals(identity.deviceId())
                || !member.signingPublicKey().equals(identity.signingPublicKey())
                || !member.encryptionPublicKey().equals(identity.encryptionPublicKey()))
            throw new GeneralSecurityException("Membership device keys do not match.");
    }

    private static void requireSameKeys(Member before, Member after) throws GeneralSecurityException {
        if (!before.deviceId().equals(after.deviceId()) || !before.signingPublicKey().equals(after.signingPublicKey())
                || !before.encryptionPublicKey().equals(after.encryptionPublicKey()))
            throw new GeneralSecurityException("Membership device identity changed.");
    }

    private static byte[] domainBytes(byte[] canonical) {
        byte[] prefix = (PROTOCOL + "\0").getBytes(StandardCharsets.US_ASCII);
        byte[] result = Arrays.copyOf(prefix, prefix.length + canonical.length);
        System.arraycopy(canonical, 0, result, prefix.length, canonical.length);
        Arrays.fill(prefix, (byte) 0);
        return result;
    }

    private static Set<String> keys(JSONObject json) {
        Set<String> result = new HashSet<>();
        json.keys().forEachRemaining(result::add);
        return result;
    }

    private static long getLong(JSONObject json, String field) throws JSONException, GeneralSecurityException {
        Object value = json.get(field);
        if (!(value instanceof Number) || !value.toString().matches("-?(0|[1-9][0-9]*)"))
            throw new GeneralSecurityException("Membership integer rejected.");
        try { return Long.parseLong(value.toString()); }
        catch (NumberFormatException exception) { throw new GeneralSecurityException("Membership integer rejected.", exception); }
    }

    private static void requireId(String id) throws GeneralSecurityException {
        if (id == null || !id.matches("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
                ) || "00000000-0000-0000-0000-000000000000".equals(id))
            throw new GeneralSecurityException("Canonical UUID rejected.");
    }

    private static byte[] decodeExact(String value, int expected) throws GeneralSecurityException {
        int encodedLength = (expected * 8 + 5) / 6;
        if (value == null || value.length() != encodedLength || !value.matches("[A-Za-z0-9_-]+"))
            throw new GeneralSecurityException("Canonical base64url rejected.");
        byte[] decoded;
        try { decoded = Base64.getUrlDecoder().decode(value); }
        catch (IllegalArgumentException exception) { throw new GeneralSecurityException("Base64url rejected.", exception); }
        if (decoded.length != expected || !encode(decoded).equals(value)) {
            Arrays.fill(decoded, (byte) 0);
            throw new GeneralSecurityException("Base64url size rejected.");
        }
        return decoded;
    }

    private static String encode(byte[] value) { return Base64.getUrlEncoder().withoutPadding().encodeToString(value); }
}
