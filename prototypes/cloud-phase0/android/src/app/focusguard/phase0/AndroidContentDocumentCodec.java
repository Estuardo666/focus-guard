package app.focusguard.phase0;

import android.content.Context;

import com.sun.jna.Pointer;
import com.sun.jna.ptr.LongByReference;
import org.json.JSONException;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import java.util.HashSet;
import java.util.Set;

/** FG-E2EE-1 document codec matching the existing FocusGuard C# wire format. */
public final class AndroidContentDocumentCodec {
    public static final String PROTOCOL = "FG-E2EE-1";
    public static final String SUITE = "XCHACHA20-POLY1305-IETF";
    private static final String SIGNATURE_DOMAIN = "FG-DOCUMENT-1";
    private static final int KEY_BYTES = 32;
    private static final int NONCE_BYTES = 24;
    private static final int TAG_BYTES = 16;
    private static final int MAX_DOCUMENT_BYTES = 262144;
    private static final long MAX_SAFE_INTEGER_LONG = 9007199254740991L;
    private static final AndroidSodium.Sodium SODIUM = AndroidSodium.instance();

    private AndroidContentDocumentCodec() { }

    public static final class DocumentContext {
        private final String accountId;
        private final String generationId;
        private final String entityId;
        private final String kind;
        private final long baseRevision;
        private final long proposedRevision;
        private final long keyEpoch;
        private final long membershipRevision;

        public DocumentContext(String accountId, String generationId, String entityId, String kind,
                long baseRevision, long proposedRevision, long keyEpoch, long membershipRevision) {
            this.accountId = accountId;
            this.generationId = generationId;
            this.entityId = entityId;
            this.kind = kind;
            this.baseRevision = baseRevision;
            this.proposedRevision = proposedRevision;
            this.keyEpoch = keyEpoch;
            this.membershipRevision = membershipRevision;
        }

        public String accountId() { return accountId; }
        public String generationId() { return generationId; }
        public String entityId() { return entityId; }
        public String kind() { return kind; }
        public long baseRevision() { return baseRevision; }
        public long proposedRevision() { return proposedRevision; }
        public long keyEpoch() { return keyEpoch; }
        public long membershipRevision() { return membershipRevision; }

        void validate() {
            requireId(accountId);
            requireId(generationId);
            requireId(entityId);
            if (kind == null || !kind.matches("[a-z][a-z0-9.-]{0,63}"))
                throw new IllegalArgumentException("Document kind rejected.");
            if (baseRevision < 0 || baseRevision > MAX_SAFE_INTEGER_LONG - 1
                    || proposedRevision != baseRevision + 1 || proposedRevision > MAX_SAFE_INTEGER_LONG
                    || keyEpoch < 1 || keyEpoch > MAX_SAFE_INTEGER_LONG
                    || membershipRevision < 1 || membershipRevision > MAX_SAFE_INTEGER_LONG)
                throw new IllegalArgumentException("Document revision or epoch rejected.");
        }
    }

    public static final class EncryptedDocument {
        private final String protocol;
        private final String suite;
        private final String accountId;
        private final String generationId;
        private final String entityId;
        private final String kind;
        private final long baseRevision;
        private final long proposedRevision;
        private final long keyEpoch;
        private final long membershipRevision;
        private final String authorDeviceId;
        private final String nonce;
        private final String ciphertext;
        private final String signature;

        public EncryptedDocument(String protocol, String suite, String accountId, String generationId,
                String entityId, String kind, long baseRevision, long proposedRevision, long keyEpoch,
                long membershipRevision, String authorDeviceId, String nonce, String ciphertext, String signature) {
            this.protocol = protocol;
            this.suite = suite;
            this.accountId = accountId;
            this.generationId = generationId;
            this.entityId = entityId;
            this.kind = kind;
            this.baseRevision = baseRevision;
            this.proposedRevision = proposedRevision;
            this.keyEpoch = keyEpoch;
            this.membershipRevision = membershipRevision;
            this.authorDeviceId = authorDeviceId;
            this.nonce = nonce;
            this.ciphertext = ciphertext;
            this.signature = signature;
        }

        public String protocol() { return protocol; }
        public String suite() { return suite; }
        public String accountId() { return accountId; }
        public String generationId() { return generationId; }
        public String entityId() { return entityId; }
        public String kind() { return kind; }
        public long baseRevision() { return baseRevision; }
        public long proposedRevision() { return proposedRevision; }
        public long keyEpoch() { return keyEpoch; }
        public long membershipRevision() { return membershipRevision; }
        public String authorDeviceId() { return authorDeviceId; }
        public String nonce() { return nonce; }
        public String ciphertext() { return ciphertext; }
        public String signature() { return signature; }
    }

    static EncryptedDocument encrypt(byte[] canonicalJson, byte[] contentKey,
            DocumentContext context, AndroidDeviceIdentity author) throws GeneralSecurityException {
        if (author == null) throw new IllegalArgumentException("Document author required.");
        context.validate();
        if (contentKey == null || contentKey.length != KEY_BYTES)
            throw new GeneralSecurityException("Content key size rejected.");
        validateCanonicalJson(canonicalJson);

        byte[] nonce = new byte[NONCE_BYTES];
        byte[] aad = null;
        byte[] ciphertext = new byte[canonicalJson.length + TAG_BYTES];
        byte[] header = null;
        new SecureRandom().nextBytes(nonce);
        try {
            aad = associatedData(context);
            LongByReference ciphertextLength = new LongByReference();
            if (SODIUM.crypto_aead_xchacha20poly1305_ietf_encrypt(ciphertext, ciphertextLength,
                    canonicalJson, canonicalJson.length, aad, aad.length, Pointer.NULL, nonce, contentKey) != 0
                    || ciphertextLength.getValue() != ciphertext.length)
                throw new GeneralSecurityException("Document encryption failed.");

            EncryptedDocument unsigned = new EncryptedDocument(PROTOCOL, SUITE, context.accountId(),
                    context.generationId(), context.entityId(), context.kind(), context.baseRevision(),
                    context.proposedRevision(), context.keyEpoch(), context.membershipRevision(),
                    author.deviceId(), encode(nonce), encode(ciphertext), encode(new byte[64]));
            header = canonicalHeader(unsigned);
            String signature = author.sign(SIGNATURE_DOMAIN, header);
            return new EncryptedDocument(unsigned.protocol(), unsigned.suite(), unsigned.accountId(),
                    unsigned.generationId(), unsigned.entityId(), unsigned.kind(), unsigned.baseRevision(),
                    unsigned.proposedRevision(), unsigned.keyEpoch(), unsigned.membershipRevision(),
                    unsigned.authorDeviceId(), unsigned.nonce(), unsigned.ciphertext(), signature);
        } finally {
            Arrays.fill(nonce, (byte) 0);
            Arrays.fill(ciphertext, (byte) 0);
            if (aad != null) Arrays.fill(aad, (byte) 0);
            if (header != null) Arrays.fill(header, (byte) 0);
        }
    }

    public static EncryptedDocument encrypt(byte[] canonicalJson, byte[] contentKey,
            DocumentContext context, AndroidDeviceIdentity author,
            AndroidMembershipVerifier.VerifiedMembership membership) throws GeneralSecurityException {
        if (membership == null) throw new GeneralSecurityException("Verified membership is required.");
        membership.authorizeDocumentWrite(context, author.deviceId(), author.publicIdentity());
        return encrypt(canonicalJson, contentKey, context, author);
    }

    static byte[] decrypt(EncryptedDocument document, DocumentContext expectedContext,
            byte[] contentKey, AndroidDeviceIdentity.PublicIdentity verifiedAuthor) throws GeneralSecurityException {
        if (document == null || expectedContext == null || verifiedAuthor == null)
            throw new GeneralSecurityException("Document input missing.");
        expectedContext.validate();
        if (contentKey == null || contentKey.length != KEY_BYTES || !PROTOCOL.equals(document.protocol())
                || !SUITE.equals(document.suite()) || !expectedContext.accountId().equals(document.accountId())
                || !expectedContext.generationId().equals(document.generationId())
                || !expectedContext.entityId().equals(document.entityId()) || !expectedContext.kind().equals(document.kind())
                || expectedContext.baseRevision() != document.baseRevision()
                || expectedContext.proposedRevision() != document.proposedRevision()
                || expectedContext.keyEpoch() != document.keyEpoch()
                || expectedContext.membershipRevision() != document.membershipRevision()
                || !verifiedAuthor.deviceId().equals(document.authorDeviceId()))
            throw new GeneralSecurityException("Document context rejected.");

        byte[] nonce = null;
        byte[] ciphertext = null;
        byte[] signature = null;
        byte[] header = null;
        byte[] aad = null;
        byte[] plaintext = null;
        try {
            nonce = decode(document.nonce(), NONCE_BYTES);
            ciphertext = decodeVariable(document.ciphertext(), TAG_BYTES + 1, MAX_DOCUMENT_BYTES + TAG_BYTES);
            signature = decode(document.signature(), 64);
            header = canonicalHeader(unsigned(document));
            if (!AndroidDeviceIdentity.verify(verifiedAuthor, SIGNATURE_DOMAIN, header, document.signature()))
                throw new GeneralSecurityException("Document author signature rejected.");
            aad = associatedData(expectedContext);
            plaintext = new byte[ciphertext.length - TAG_BYTES];
            LongByReference plaintextLength = new LongByReference();
            if (SODIUM.crypto_aead_xchacha20poly1305_ietf_decrypt(plaintext, plaintextLength, Pointer.NULL,
                    ciphertext, ciphertext.length, aad, aad.length, nonce, contentKey) != 0
                    || plaintextLength.getValue() != plaintext.length)
                throw new GeneralSecurityException("Document authentication failed.");
            validateCanonicalJson(plaintext);
            byte[] clear = plaintext;
            plaintext = null;
            return clear;
        } finally {
            if (nonce != null) Arrays.fill(nonce, (byte) 0);
            if (ciphertext != null) Arrays.fill(ciphertext, (byte) 0);
            if (signature != null) Arrays.fill(signature, (byte) 0);
            if (header != null) Arrays.fill(header, (byte) 0);
            if (aad != null) Arrays.fill(aad, (byte) 0);
            if (plaintext != null) Arrays.fill(plaintext, (byte) 0);
        }
    }

    public static byte[] decrypt(EncryptedDocument document, DocumentContext expectedContext,
            byte[] contentKey, AndroidDeviceIdentity.PublicIdentity verifiedAuthor,
            AndroidMembershipVerifier.VerifiedMembership membership) throws GeneralSecurityException {
        if (membership == null) throw new GeneralSecurityException("Verified membership is required.");
        membership.authorizeDocumentRead(expectedContext, verifiedAuthor.deviceId(), verifiedAuthor);
        return decrypt(document, expectedContext, contentKey, verifiedAuthor);
    }

    public static EncryptedDocument fromJson(String json) throws GeneralSecurityException {
        if (json == null || json.length() > 400000) throw new GeneralSecurityException("Envelope JSON size rejected.");
        byte[] source = json.getBytes(StandardCharsets.UTF_8);
        byte[] canonical = null;
        try {
            JSONObject parsed = new JSONObject(json);
            canonical = new JsonCanonicalizer(json).getEncodedUTF8();
            Set<String> keys = new HashSet<>();
            parsed.keys().forEachRemaining(keys::add);
            Set<String> expectedKeys = new HashSet<>(Arrays.asList("protocol", "suite", "accountId", "generationId", "entityId", "kind",
                    "baseRevision", "proposedRevision", "keyEpoch", "membershipRevision", "authorDeviceId",
                    "nonce", "ciphertext", "signature"));
            if (!keys.equals(expectedKeys))
                throw new GeneralSecurityException("Envelope fields rejected.");
            return new EncryptedDocument(parsed.getString("protocol"), parsed.getString("suite"),
                    parsed.getString("accountId"), parsed.getString("generationId"), parsed.getString("entityId"),
                    parsed.getString("kind"), getLong(parsed, "baseRevision"), getLong(parsed, "proposedRevision"),
                    getLong(parsed, "keyEpoch"), getLong(parsed, "membershipRevision"), parsed.getString("authorDeviceId"),
                    parsed.getString("nonce"), parsed.getString("ciphertext"), parsed.getString("signature"));
        } catch (JSONException | IOException exception) {
            throw new GeneralSecurityException("Envelope JSON rejected.", exception);
        } finally {
            Arrays.fill(source, (byte) 0);
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
        }
    }

    public static byte[] canonicalDocumentJson(EncryptedDocument document) throws GeneralSecurityException {
        String json = "{\"protocol\":\"" + document.protocol() + "\",\"suite\":\"" + document.suite()
                + "\",\"accountId\":\"" + document.accountId() + "\",\"generationId\":\"" + document.generationId()
                + "\",\"entityId\":\"" + document.entityId() + "\",\"kind\":\"" + document.kind()
                + "\",\"baseRevision\":" + document.baseRevision() + ",\"proposedRevision\":" + document.proposedRevision()
                + ",\"keyEpoch\":" + document.keyEpoch() + ",\"membershipRevision\":" + document.membershipRevision()
                + ",\"authorDeviceId\":\"" + document.authorDeviceId() + "\",\"nonce\":\"" + document.nonce()
                + "\",\"ciphertext\":\"" + document.ciphertext() + "\",\"signature\":\"" + document.signature() + "\"}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Document envelope serialization failed.", exception); }
    }

    public static void writeEncryptedEnvelopeForProbe(Context context, EncryptedDocument document)
            throws GeneralSecurityException, IOException {
        byte[] bytes = canonicalDocumentJson(document);
        try (FileOutputStream output = context.openFileOutput("android-document-interop.json", Context.MODE_PRIVATE)) {
            output.write(bytes);
            output.getFD().sync();
        } finally { Arrays.fill(bytes, (byte) 0); }
    }

    private static EncryptedDocument unsigned(EncryptedDocument document) {
        return new EncryptedDocument(document.protocol(), document.suite(), document.accountId(),
                document.generationId(), document.entityId(), document.kind(), document.baseRevision(),
                document.proposedRevision(), document.keyEpoch(), document.membershipRevision(),
                document.authorDeviceId(), document.nonce(), document.ciphertext(), encode(new byte[64]));
    }

    private static byte[] canonicalHeader(EncryptedDocument document) throws GeneralSecurityException {
        String json = "{\"protocol\":\"" + document.protocol() + "\",\"suite\":\"" + document.suite()
                + "\",\"accountId\":\"" + document.accountId() + "\",\"generationId\":\"" + document.generationId()
                + "\",\"entityId\":\"" + document.entityId() + "\",\"kind\":\"" + document.kind()
                + "\",\"baseRevision\":" + document.baseRevision() + ",\"proposedRevision\":" + document.proposedRevision()
                + ",\"keyEpoch\":" + document.keyEpoch() + ",\"authorDeviceId\":\"" + document.authorDeviceId()
                + "\",\"membershipRevision\":" + document.membershipRevision() + ",\"nonce\":\"" + document.nonce()
                + "\",\"ciphertext\":\"" + document.ciphertext() + "\"}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Document header serialization failed.", exception); }
    }

    private static byte[] associatedData(DocumentContext context) throws GeneralSecurityException {
        context.validate();
        String json = "{\"protocol\":\"" + PROTOCOL + "\",\"accountId\":\"" + context.accountId()
                + "\",\"generationId\":\"" + context.generationId() + "\",\"entityId\":\"" + context.entityId()
                + "\",\"kind\":\"" + context.kind() + "\",\"baseRevision\":" + context.baseRevision()
                + ",\"proposedRevision\":" + context.proposedRevision() + ",\"keyEpoch\":" + context.keyEpoch()
                + ",\"membershipRevision\":" + context.membershipRevision() + "}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Document AAD serialization failed.", exception); }
    }

    private static void validateCanonicalJson(byte[] json) throws GeneralSecurityException {
        if (json == null || json.length == 0 || json.length > MAX_DOCUMENT_BYTES)
            throw new GeneralSecurityException("Document JSON size rejected.");
        byte[] canonical = null;
        try {
            String text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(json)).toString();
            if (!text.trim().startsWith("{")) throw new GeneralSecurityException("Document must be a JSON object.");
            requireDepthAtMost32(text);
            new JSONObject(text); // Requires an object root; JCS below rejects duplicate names and malformed JSON.
            canonical = new JsonCanonicalizer(text).getEncodedUTF8();
            if (!fixedTimeEqual(json, canonical)) throw new GeneralSecurityException("Document must be JCS canonical.");
        } catch (JSONException | IOException exception) {
            throw new GeneralSecurityException("Document JSON rejected.", exception);
        } finally {
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
        }
    }

    private static void requireDepthAtMost32(String json) throws GeneralSecurityException {
        int depth = 0;
        int maximum = 0;
        boolean inString = false;
        boolean escaped = false;
        for (int index = 0; index < json.length(); index++) {
            char value = json.charAt(index);
            if (inString) {
                if (escaped) escaped = false;
                else if (value == '\\') escaped = true;
                else if (value == '"') inString = false;
                continue;
            }
            if (value == '"') inString = true;
            else if (value == '{' || value == '[') {
                depth++;
                maximum = Math.max(maximum, depth);
                if (maximum > 32) throw new GeneralSecurityException("Document nesting depth rejected.");
            } else if (value == '}' || value == ']') depth--;
        }
    }

    private static void requireId(String id) {
        if (id == null || !id.matches("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
                ) || "00000000-0000-0000-0000-000000000000".equals(id))
            throw new IllegalArgumentException("Canonical lowercase UUID required.");
    }

    private static long getLong(JSONObject json, String field) throws JSONException, GeneralSecurityException {
        Object value = json.get(field);
        if (!(value instanceof Number)) throw new GeneralSecurityException("Envelope integer field rejected.");
        String text = value.toString();
        if (!text.matches("-?(0|[1-9][0-9]*)"))
            throw new GeneralSecurityException("Envelope integer field rejected.");
        try { return Long.parseLong(text); }
        catch (NumberFormatException exception) {
            throw new GeneralSecurityException("Envelope integer field rejected.", exception);
        }
    }

    private static byte[] decode(String value, int expectedBytes) throws GeneralSecurityException {
        byte[] result = decodeVariable(value, expectedBytes, expectedBytes);
        return result;
    }

    private static byte[] decodeVariable(String value, int minBytes, int maxBytes) throws GeneralSecurityException {
        int maxEncodedLength = (maxBytes * 8 + 5) / 6;
        if (value == null || value.length() == 0 || value.length() > maxEncodedLength
                || !value.matches("[A-Za-z0-9_-]+")) throw new GeneralSecurityException("Canonical base64url rejected.");
        byte[] decoded;
        try { decoded = Base64.getUrlDecoder().decode(value); }
        catch (IllegalArgumentException exception) { throw new GeneralSecurityException("Canonical base64url rejected.", exception); }
        if (decoded.length < minBytes || decoded.length > maxBytes || !encode(decoded).equals(value)) {
            Arrays.fill(decoded, (byte) 0);
            throw new GeneralSecurityException("Encoded field size rejected.");
        }
        return decoded;
    }

    private static String encode(byte[] bytes) { return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes); }

    private static boolean fixedTimeEqual(byte[] left, byte[] right) {
        return java.security.MessageDigest.isEqual(left, right);
    }
}
