package app.focusguard.phase0;

import android.content.Context;

import org.json.JSONException;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Base64;
import java.util.HashSet;
import java.util.Set;

/** FG-SODIUM-BOX-1 signed account-key envelope compatible with FocusGuard C#. */
public final class AndroidSignedKeyEnvelopeCodec {
    public static final String SUITE = "FG-SODIUM-BOX-1";
    private static final String PAYLOAD_PROTOCOL = "FG-ACCOUNT-KEY-1";
    private static final int KEY_BYTES = 32;
    private static final int SIGNATURE_BYTES = 64;
    private static final int SEALED_OVERHEAD = 48;
    private static final int MAX_SEALED_BYTES = 6144;
    private static final long MAX_SAFE_INTEGER = 9007199254740991L;

    private AndroidSignedKeyEnvelopeCodec() { }

    public static final class EnvelopeContext {
        private final String accountId;
        private final String generationId;
        private final long keyEpoch;
        private final String recipientId;
        private final String membershipDigest;
        private final String signerDeviceId;

        public EnvelopeContext(String accountId, String generationId, long keyEpoch, String recipientId,
                String membershipDigest, String signerDeviceId) {
            this.accountId = accountId;
            this.generationId = generationId;
            this.keyEpoch = keyEpoch;
            this.recipientId = recipientId;
            this.membershipDigest = membershipDigest;
            this.signerDeviceId = signerDeviceId;
        }

        public String accountId() { return accountId; }
        public String generationId() { return generationId; }
        public long keyEpoch() { return keyEpoch; }
        public String recipientId() { return recipientId; }
        public String membershipDigest() { return membershipDigest; }
        public String signerDeviceId() { return signerDeviceId; }

        void validate() throws GeneralSecurityException {
            requireId(accountId);
            requireId(generationId);
            requireId(recipientId);
            requireId(signerDeviceId);
            if (keyEpoch < 1 || keyEpoch > MAX_SAFE_INTEGER)
                throw new GeneralSecurityException("Key epoch rejected.");
            byte[] digest = decodeExact(membershipDigest, KEY_BYTES);
            Arrays.fill(digest, (byte) 0);
        }
    }

    public static final class SignedEnvelope {
        private final String suite;
        private final String generationId;
        private final long keyEpoch;
        private final String recipientId;
        private final String recipientPublicKey;
        private final String membershipDigest;
        private final String ciphertext;
        private final String signerDeviceId;
        private final String signature;

        public SignedEnvelope(String suite, String generationId, long keyEpoch, String recipientId,
                String recipientPublicKey, String membershipDigest, String ciphertext,
                String signerDeviceId, String signature) {
            this.suite = suite;
            this.generationId = generationId;
            this.keyEpoch = keyEpoch;
            this.recipientId = recipientId;
            this.recipientPublicKey = recipientPublicKey;
            this.membershipDigest = membershipDigest;
            this.ciphertext = ciphertext;
            this.signerDeviceId = signerDeviceId;
            this.signature = signature;
        }

        public String suite() { return suite; }
        public String generationId() { return generationId; }
        public long keyEpoch() { return keyEpoch; }
        public String recipientId() { return recipientId; }
        public String recipientPublicKey() { return recipientPublicKey; }
        public String membershipDigest() { return membershipDigest; }
        public String ciphertext() { return ciphertext; }
        public String signerDeviceId() { return signerDeviceId; }
        public String signature() { return signature; }
    }

    static SignedEnvelope sealForDevice(byte[] accountContentKey, EnvelopeContext context,
            String recipientEncryptionPublicKey, AndroidDeviceIdentity signer) throws GeneralSecurityException {
        context.validate();
        if (accountContentKey == null || accountContentKey.length != KEY_BYTES || signer == null
                || !signer.deviceId().equals(context.signerDeviceId()))
            throw new GeneralSecurityException("Key or signer context rejected.");
        byte[] recipientPublicKey = decodeExact(recipientEncryptionPublicKey, KEY_BYTES);
        byte[] clearPayload = null;
        byte[] ciphertext = new byte[1];
        byte[] header = null;
        try {
            clearPayload = canonicalPayload(accountContentKey, context);
            ciphertext = new byte[clearPayload.length + SEALED_OVERHEAD];
            if (AndroidSodium.instance().crypto_box_seal(ciphertext, clearPayload, clearPayload.length,
                    recipientPublicKey) != 0 || ciphertext.length < SEALED_OVERHEAD || ciphertext.length > MAX_SEALED_BYTES)
                throw new GeneralSecurityException("Account key sealing failed.");
            SignedEnvelope unsigned = new SignedEnvelope(SUITE, context.generationId(), context.keyEpoch(),
                    context.recipientId(), recipientEncryptionPublicKey, context.membershipDigest(),
                    encode(ciphertext), context.signerDeviceId(), "");
            header = canonicalHeader(unsigned);
            String signature = signer.sign(SUITE, header);
            return new SignedEnvelope(unsigned.suite(), unsigned.generationId(), unsigned.keyEpoch(),
                    unsigned.recipientId(), unsigned.recipientPublicKey(), unsigned.membershipDigest(),
                    unsigned.ciphertext(), unsigned.signerDeviceId(), signature);
        } finally {
            Arrays.fill(recipientPublicKey, (byte) 0);
            if (clearPayload != null) Arrays.fill(clearPayload, (byte) 0);
            Arrays.fill(ciphertext, (byte) 0);
            if (header != null) Arrays.fill(header, (byte) 0);
        }
    }

    public static SignedEnvelope sealForDevice(byte[] accountContentKey, EnvelopeContext context,
            String recipientEncryptionPublicKey, AndroidDeviceIdentity signer,
            AndroidMembershipVerifier.VerifiedMembership verifiedMembership) throws GeneralSecurityException {
        if (verifiedMembership == null) throw new GeneralSecurityException("Verified membership is required.");
        verifiedMembership.authorizeKeyEnvelopeForRecipient(context, signer.publicIdentity(), recipientEncryptionPublicKey);
        return sealForDevice(accountContentKey, context, recipientEncryptionPublicKey, signer);
    }

    public static SignedEnvelope sealForRecovery(byte[] accountContentKey, EnvelopeContext context,
            String recoveryEncryptionPublicKey, AndroidDeviceIdentity signer,
            AndroidMembershipVerifier.VerifiedMembership verifiedMembership) throws GeneralSecurityException {
        if (verifiedMembership == null) throw new GeneralSecurityException("Verified membership is required.");
        verifiedMembership.authorizeKeyEnvelopeForRecovery(context, signer.publicIdentity(), recoveryEncryptionPublicKey);
        return sealForDevice(accountContentKey, context, recoveryEncryptionPublicKey, signer);
    }

    /** Verifies all trusted-context bindings and the signer before opening the ciphertext. */
    static byte[] openForDevice(SignedEnvelope envelope, EnvelopeContext context,
            AndroidDeviceIdentity.PublicIdentity signer, AndroidDeviceIdentity recipient)
            throws GeneralSecurityException {
        context.validate();
        if (envelope == null || signer == null || recipient == null || !SUITE.equals(envelope.suite())
                || !envelope.generationId().equals(context.generationId())
                || envelope.keyEpoch() != context.keyEpoch()
                || !envelope.recipientId().equals(context.recipientId())
                || !envelope.membershipDigest().equals(context.membershipDigest())
                || !envelope.signerDeviceId().equals(context.signerDeviceId())
                || !envelope.signerDeviceId().equals(signer.deviceId())
                || !envelope.recipientId().equals(recipient.deviceId())
                || !envelope.recipientPublicKey().equals(recipient.publicIdentity().encryptionPublicKey()))
            throw new GeneralSecurityException("Key envelope context rejected.");

        byte[] signature = null;
        byte[] ciphertext = null;
        byte[] header = null;
        byte[] clearPayload = null;
        try {
            signature = decodeExact(envelope.signature(), SIGNATURE_BYTES);
            ciphertext = decodeVariable(envelope.ciphertext(), SEALED_OVERHEAD, MAX_SEALED_BYTES);
            header = canonicalHeader(envelope);
            if (!AndroidDeviceIdentity.verify(signer, SUITE, header, envelope.signature()))
                throw new GeneralSecurityException("Envelope signer rejected.");
            clearPayload = recipient.openSealed(ciphertext, envelope.recipientPublicKey());
            return parsePayload(clearPayload, context);
        } finally {
            if (signature != null) Arrays.fill(signature, (byte) 0);
            if (ciphertext != null) Arrays.fill(ciphertext, (byte) 0);
            if (header != null) Arrays.fill(header, (byte) 0);
            if (clearPayload != null) Arrays.fill(clearPayload, (byte) 0);
        }
    }

    public static byte[] openForDevice(SignedEnvelope envelope, EnvelopeContext context,
            AndroidDeviceIdentity.PublicIdentity signer, AndroidDeviceIdentity recipient,
            AndroidMembershipVerifier.VerifiedMembership verifiedMembership) throws GeneralSecurityException {
        if (verifiedMembership == null) throw new GeneralSecurityException("Verified membership is required.");
        verifiedMembership.authorizeKeyEnvelope(context, signer, recipient);
        return openForDevice(envelope, context, signer, recipient);
    }

    /** Verifies an envelope signature without opening another device's ciphertext. */
    public static void verifySignedHeader(SignedEnvelope envelope,
            AndroidDeviceIdentity.PublicIdentity signer) throws GeneralSecurityException {
        if (envelope == null || signer == null || !SUITE.equals(envelope.suite())
                || !envelope.signerDeviceId().equals(signer.deviceId()))
            throw new GeneralSecurityException("Envelope signer context rejected.");
        byte[] signature = decodeExact(envelope.signature(), SIGNATURE_BYTES);
        byte[] header = null;
        try {
            header = canonicalHeader(envelope);
            if (!AndroidDeviceIdentity.verify(signer, SUITE, header, envelope.signature()))
                throw new GeneralSecurityException("Envelope signer rejected.");
        } finally {
            Arrays.fill(signature, (byte) 0);
            if (header != null) Arrays.fill(header, (byte) 0);
        }
    }

    public static SignedEnvelope fromJson(String json) throws GeneralSecurityException {
        if (json == null || json.length() > 16000) throw new GeneralSecurityException("Envelope JSON size rejected.");
        byte[] utf8 = json.getBytes(StandardCharsets.UTF_8);
        byte[] canonical = null;
        try {
            JSONObject parsed = new JSONObject(json);
            canonical = new JsonCanonicalizer(json).getEncodedUTF8(); // Rejects duplicate property names.
            Set<String> keys = new HashSet<>();
            parsed.keys().forEachRemaining(keys::add);
            Set<String> expected = new HashSet<>(Arrays.asList("suite", "generationId", "keyEpoch", "recipientId",
                    "recipientPublicKey", "membershipDigest", "ciphertext", "signerDeviceId", "signature"));
            if (!keys.equals(expected)) throw new GeneralSecurityException("Envelope fields rejected.");
            Object rawEpoch = parsed.get("keyEpoch");
            if (!(rawEpoch instanceof Number) || !rawEpoch.toString().matches("-?(0|[1-9][0-9]*)"))
                throw new GeneralSecurityException("Envelope epoch rejected.");
            return new SignedEnvelope(parsed.getString("suite"), parsed.getString("generationId"),
                    Long.parseLong(rawEpoch.toString()), parsed.getString("recipientId"),
                    parsed.getString("recipientPublicKey"), parsed.getString("membershipDigest"),
                    parsed.getString("ciphertext"), parsed.getString("signerDeviceId"), parsed.getString("signature"));
        } catch (JSONException | IOException | NumberFormatException exception) {
            throw new GeneralSecurityException("Envelope JSON rejected.", exception);
        } finally {
            Arrays.fill(utf8, (byte) 0);
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
        }
    }

    public static byte[] canonicalEnvelopeJson(SignedEnvelope envelope) throws GeneralSecurityException {
        String json = "{\"suite\":\"" + envelope.suite() + "\",\"generationId\":\"" + envelope.generationId()
                + "\",\"keyEpoch\":" + envelope.keyEpoch() + ",\"recipientId\":\"" + envelope.recipientId()
                + "\",\"recipientPublicKey\":\"" + envelope.recipientPublicKey() + "\",\"membershipDigest\":\""
                + envelope.membershipDigest() + "\",\"ciphertext\":\"" + envelope.ciphertext()
                + "\",\"signerDeviceId\":\"" + envelope.signerDeviceId() + "\",\"signature\":\""
                + envelope.signature() + "\"}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Envelope serialization failed.", exception); }
    }

    public static void writeEnvelopeForProbe(Context appContext, SignedEnvelope envelope)
            throws IOException, GeneralSecurityException {
        byte[] json = canonicalEnvelopeJson(envelope);
        try (FileOutputStream output = appContext.openFileOutput("android-key-envelope-interop.json", Context.MODE_PRIVATE)) {
            output.write(json);
            output.getFD().sync();
        } finally { Arrays.fill(json, (byte) 0); }
    }

    private static byte[] canonicalHeader(SignedEnvelope envelope) throws GeneralSecurityException {
        String json = "{\"suite\":\"" + envelope.suite() + "\",\"generationId\":\"" + envelope.generationId()
                + "\",\"keyEpoch\":" + envelope.keyEpoch() + ",\"recipientId\":\"" + envelope.recipientId()
                + "\",\"recipientPublicKey\":\"" + envelope.recipientPublicKey() + "\",\"membershipDigest\":\""
                + envelope.membershipDigest() + "\",\"ciphertext\":\"" + envelope.ciphertext()
                + "\",\"signerDeviceId\":\"" + envelope.signerDeviceId() + "\"}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Envelope header serialization failed.", exception); }
    }

    private static byte[] canonicalPayload(byte[] contentKey, EnvelopeContext context) throws GeneralSecurityException {
        String json = "{\"protocol\":\"" + PAYLOAD_PROTOCOL + "\",\"accountId\":\"" + context.accountId()
                + "\",\"generationId\":\"" + context.generationId() + "\",\"keyEpoch\":" + context.keyEpoch()
                + ",\"recipientId\":\"" + context.recipientId() + "\",\"membershipDigest\":\""
                + context.membershipDigest() + "\",\"contentKey\":\"" + encode(contentKey) + "\"}";
        try { return new JsonCanonicalizer(json).getEncodedUTF8(); }
        catch (IOException exception) { throw new GeneralSecurityException("Key payload serialization failed.", exception); }
    }

    private static byte[] parsePayload(byte[] clear, EnvelopeContext context) throws GeneralSecurityException {
        if (clear == null || clear.length == 0 || clear.length > 6000)
            throw new GeneralSecurityException("Key payload size rejected.");
        byte[] canonical = null;
        byte[] key = null;
        try {
            String text = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(clear)).toString();
            requireDepthAtMostFour(text);
            JSONObject payload = new JSONObject(text);
            canonical = new JsonCanonicalizer(text).getEncodedUTF8();
            Set<String> keys = new HashSet<>();
            payload.keys().forEachRemaining(keys::add);
            Set<String> expected = new HashSet<>(Arrays.asList("protocol", "accountId", "generationId", "keyEpoch",
                    "recipientId", "membershipDigest", "contentKey"));
            if (!keys.equals(expected) || !MessageDigest.isEqual(canonical, clear)
                    || !PAYLOAD_PROTOCOL.equals(payload.getString("protocol"))
                    || !context.accountId().equals(payload.getString("accountId"))
                    || !context.generationId().equals(payload.getString("generationId"))
                    || !context.recipientId().equals(payload.getString("recipientId"))
                    || !context.membershipDigest().equals(payload.getString("membershipDigest")))
                throw new GeneralSecurityException("Key payload binding or canonical form rejected.");
            Object rawEpoch = payload.get("keyEpoch");
            if (!(rawEpoch instanceof Number) || !rawEpoch.toString().matches("-?(0|[1-9][0-9]*)")
                    || Long.parseLong(rawEpoch.toString()) != context.keyEpoch())
                throw new GeneralSecurityException("Key payload epoch rejected.");
            key = decodeExact(payload.getString("contentKey"), KEY_BYTES);
            byte[] result = key;
            key = null;
            return result;
        } catch (IOException | JSONException | NumberFormatException exception) {
            throw new GeneralSecurityException("Key payload rejected.", exception);
        } finally {
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
            if (key != null) Arrays.fill(key, (byte) 0);
        }
    }

    private static void requireDepthAtMostFour(String json) throws GeneralSecurityException {
        int depth = 0;
        boolean inString = false;
        boolean escaped = false;
        for (int index = 0; index < json.length(); index++) {
            char value = json.charAt(index);
            if (inString) {
                if (escaped) escaped = false;
                else if (value == '\\') escaped = true;
                else if (value == '"') inString = false;
            } else if (value == '"') inString = true;
            else if (value == '{' || value == '[') {
                depth++;
                if (depth > 4) throw new GeneralSecurityException("Key payload nesting rejected.");
            } else if (value == '}' || value == ']') depth--;
        }
    }

    private static void requireId(String id) throws GeneralSecurityException {
        if (id == null || !id.matches("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
                ) || "00000000-0000-0000-0000-000000000000".equals(id))
            throw new GeneralSecurityException("Canonical lowercase UUID required.");
    }

    private static byte[] decodeExact(String value, int length) throws GeneralSecurityException {
        byte[] result = decodeVariable(value, length, length);
        return result;
    }

    private static byte[] decodeVariable(String value, int minLength, int maxLength) throws GeneralSecurityException {
        int maxEncodedLength = (maxLength * 8 + 5) / 6;
        if (value == null || value.length() == 0 || value.length() > maxEncodedLength
                || !value.matches("[A-Za-z0-9_-]+")) throw new GeneralSecurityException("Canonical base64url rejected.");
        byte[] decoded;
        try { decoded = Base64.getUrlDecoder().decode(value); }
        catch (IllegalArgumentException exception) { throw new GeneralSecurityException("Base64url rejected.", exception); }
        if (decoded.length < minLength || decoded.length > maxLength || !encode(decoded).equals(value)) {
            Arrays.fill(decoded, (byte) 0);
            throw new GeneralSecurityException("Envelope binary size rejected.");
        }
        return decoded;
    }

    private static String encode(byte[] bytes) { return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes); }
}
