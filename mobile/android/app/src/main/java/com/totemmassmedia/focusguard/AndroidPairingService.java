package com.totemmassmedia.focusguard;

import android.content.Context;
import android.net.Uri;

import app.focusguard.phase0.AndroidDeviceIdentity;
import app.focusguard.phase0.AndroidDeviceVault;
import app.focusguard.phase0.AndroidMembershipVerifier;
import app.focusguard.phase0.AndroidPairingAcceptance;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.webpki.jcs.JsonCanonicalizer;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.time.Instant;
import java.time.format.DateTimeFormatter;
import java.util.Arrays;
import java.util.Base64;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;

/** Creates and recovers a signed Android pairing request. It never reports approval as a completed link. */
final class AndroidPairingService {
    private static final Uri API_ORIGIN = Uri.parse(BuildConfig.FOCUSGUARD_API_ORIGIN);
    private static final String API_HOST = "api.focusguard.totemmassmedia.com";
    private static final String PAIRING_DOMAIN = "FG-PAIRING-1";
    private static final String PENDING_REQUEST_DOMAIN = "FG-PENDING-REQUEST-1";
    private static final String REQUEST_DOMAIN = "FG-REQUEST-1";
    private static final String CONFIRM_DOMAIN = "FG-PAIRING-CONFIRM-1";
    private static final String RENEW_DOMAIN = "FG-DEVICE-SESSION-RENEW-1";
    private static final int SESSION_MAGIC = 0x46474153; // FGAS
    private static final byte SESSION_VERSION = 1;
    private static final int SESSION_RECORD_BYTES = Integer.BYTES + 1 + 36 * 4 + Long.BYTES * 2 + 32 * 3;
    private static final int MAX_RESPONSE_BYTES = 1_000_000;
    private static final int MAX_METADATA_BYTES = 1024;
    private static final DateTimeFormatter API_TIMESTAMP =
            DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT)
                    .withZone(java.time.ZoneOffset.UTC);

    private final AndroidDeviceVault vault;

    AndroidPairingService(Context context) {
        vault = new AndroidDeviceVault(context.getApplicationContext());
    }

    Result requestOrRecover(String accountBinding, String accountId, String generationId, String accessToken)
            throws Exception {
        requireTrustedApi();
        requireId(accountId);
        requireId(generationId);
        requireToken(accessToken);
        String savedPairingId = loadPendingPairingId(accountBinding);
        SessionRecord savedSession = loadDeviceSession(accountBinding);
        try {
            if (savedPairingId == null && savedSession != null) {
                if (!accountId.equals(savedSession.accountId) || !generationId.equals(savedSession.generationId))
                    throw new IOException("Saved device session belongs to a different account context.");
                return refreshLinkedSession(accountBinding, savedSession, accessToken);
            }
            if (savedPairingId != null && !vault.exists(accountBinding))
                throw new IOException("The saved pairing identity is unavailable on this phone.");
            try (AndroidDeviceIdentity identity = AndroidDeviceIdentity.loadOrCreate(vault, accountBinding)) {
                if (savedPairingId != null) {
                    if (savedSession != null && (!accountId.equals(savedSession.accountId)
                            || !generationId.equals(savedSession.generationId)
                            || !identity.deviceId().equals(savedSession.deviceId)
                            || !savedPairingId.equals(savedSession.pairingId)))
                        throw new IOException("Saved pairing session context rejected.");
                    Result current = readOwnPairing(accountId, generationId, identity, accessToken, savedPairingId);
                    switch (current.state) {
                        case "pending": return current;
                        case "approved": return completeApprovedPairing(accountBinding, accountId, generationId,
                                identity, accessToken, current, savedSession);
                        case "consumed":
                            if (savedSession == null) throw new IOException("Confirmed pairing session is unavailable.");
                            clearPendingPairing(accountBinding);
                            return refreshLinkedSession(accountBinding, savedSession, accessToken);
                        case "expired", "not_found":
                            clearPendingPairing(accountBinding);
                            if (savedSession != null) clearDeviceSession(accountBinding);
                            savedPairingId = null;
                            break;
                        default: throw new IOException("Pairing state rejected.");
                    }
                }

                AndroidDeviceIdentity.PublicIdentity publicIdentity = identity.publicIdentity();
                JSONObject challengeRequest = new JSONObject();
                challengeRequest.put("accountId", accountId);
                challengeRequest.put("generationId", generationId);
                challengeRequest.put("deviceId", publicIdentity.deviceId());
                challengeRequest.put("signingPublicKey", publicIdentity.signingPublicKey());
                challengeRequest.put("encryptionPublicKey", publicIdentity.encryptionPublicKey());
                challengeRequest.put("recoverySigningPublicKey", JSONObject.NULL);
                challengeRequest.put("recoveryEncryptionPublicKey", JSONObject.NULL);
                HttpResult challengeResult = send("POST", "/v1/bootstrap/challenges", accessToken,
                        challengeRequest.toString(), null);
                if (challengeResult.status != 201) {
                    Arrays.fill(challengeResult.body, (byte) 0);
                    throw new IOException("Device challenge unavailable.");
                }
                JSONObject challenge;
                try { challenge = parseStrictJson(challengeResult.body, fields(
                        "challengeId", "accountId", "generationId", "deviceId", "nonce", "expiresAt", "purpose")); }
                finally { Arrays.fill(challengeResult.body, (byte) 0); }
                String challengeId = canonicalId(challenge.getString("challengeId"));
                String nonce = challenge.getString("nonce");
                byte[] nonceBytes = decodeCanonical(nonce, 32);
                Arrays.fill(nonceBytes, (byte) 0);
                Instant expires = Instant.parse(challenge.getString("expiresAt"));
                if (!accountId.equals(challenge.getString("accountId"))
                        || !generationId.equals(challenge.getString("generationId"))
                        || !publicIdentity.deviceId().equals(challenge.getString("deviceId"))
                        || !"pairing".equals(challenge.getString("purpose"))
                        || !expires.isAfter(Instant.now()))
                    throw new IOException("Device challenge rejected.");

                // Persist the challenge id before the pairing POST so a lost response can be recovered safely.
                savePendingPairingId(accountBinding, challengeId);
                JSONObject capabilities = capabilities();
                JSONObject proof = new JSONObject();
                proof.put("pairingId", challengeId);
                JSONObject challengeProof = new JSONObject();
                challengeProof.put("challengeId", challengeId);
                challengeProof.put("accountId", accountId);
                challengeProof.put("generationId", generationId);
                challengeProof.put("deviceId", publicIdentity.deviceId());
                challengeProof.put("nonce", nonce);
                proof.put("challenge", challengeProof);
                proof.put("capabilities", capabilities);

                byte[] canonicalProof = canonicalPairingProof(challengeId, accountId, generationId,
                        publicIdentity.deviceId(), nonce, capabilities);
                String signature;
                try { signature = identity.sign(PAIRING_DOMAIN, canonicalProof); }
                finally { Arrays.fill(canonicalProof, (byte) 0); }

                JSONObject request = new JSONObject();
                request.put("proof", proof);
                request.put("proofSignature", signature);
                HttpResult pairingResult = send("POST", "/v1/bootstrap/pairings", accessToken,
                        request.toString(), null);
                if (pairingResult.status != 201) {
                    Arrays.fill(pairingResult.body, (byte) 0);
                    throw new IOException("Pairing request was not accepted.");
                }
                JSONObject response;
                try { response = parseStrictJson(pairingResult.body,
                        fields("pairingId", "deviceId", "expiresAtUtc", "state")); }
                finally { Arrays.fill(pairingResult.body, (byte) 0); }
                if (!challengeId.equals(canonicalId(response.getString("pairingId")))
                        || !publicIdentity.deviceId().equals(canonicalId(response.getString("deviceId")))
                        || !"Pending".equals(response.getString("state"))
                        || !Instant.parse(response.getString("expiresAtUtc")).isAfter(Instant.now()))
                    throw new IOException("Pairing response rejected.");
                return new Result("pending", publicIdentity.fingerprint(), challengeId);
            }
        } finally {
            if (savedSession != null) savedSession.close();
        }
    }

    Result refresh(String accountBinding, String accountId, String generationId, String accessToken)
            throws Exception {
        requireTrustedApi();
        requireId(accountId);
        requireId(generationId);
        requireToken(accessToken);
        String pairingId = loadPendingPairingId(accountBinding);
        SessionRecord savedSession = loadDeviceSession(accountBinding);
        if (pairingId == null) {
            if (savedSession == null) return new Result("not_found", null, null);
            if (!accountId.equals(savedSession.accountId) || !generationId.equals(savedSession.generationId)) {
                savedSession.close();
                throw new IOException("Saved device session belongs to a different account context.");
            }
            try { return refreshLinkedSession(accountBinding, savedSession, accessToken); }
            finally { savedSession.close(); }
        }
        if (!vault.exists(accountBinding)) {
            if (savedSession != null) savedSession.close();
            throw new IOException("The saved pairing identity is unavailable on this phone.");
        }
        try (AndroidDeviceIdentity identity = AndroidDeviceIdentity.loadOrCreate(vault, accountBinding)) {
            if (savedSession != null && (!accountId.equals(savedSession.accountId)
                    || !generationId.equals(savedSession.generationId)
                    || !identity.deviceId().equals(savedSession.deviceId)
                    || !pairingId.equals(savedSession.pairingId)))
                throw new IOException("Saved pairing session context rejected.");
            Result result = readOwnPairing(accountId, generationId, identity, accessToken, pairingId);
            return switch (result.state) {
                case "approved" -> completeApprovedPairing(accountBinding, accountId, generationId,
                        identity, accessToken, result, savedSession);
                case "consumed" -> {
                    if (savedSession == null) throw new IOException("Confirmed pairing session is unavailable.");
                    clearPendingPairing(accountBinding);
                    yield refreshLinkedSession(accountBinding, savedSession, accessToken);
                }
                case "expired", "not_found" -> {
                    clearPendingPairing(accountBinding);
                    if (savedSession != null) clearDeviceSession(accountBinding);
                    yield result;
                }
                default -> result;
            };
        }
        finally { if (savedSession != null) savedSession.close(); }
    }

    Result refreshIfAvailable(String accountBinding, String accountId, String generationId, String accessToken)
            throws Exception {
        if (loadPendingPairingId(accountBinding) == null && !vault.exists(sessionBinding(accountBinding))) return null;
        return refresh(accountBinding, accountId, generationId, accessToken);
    }

    private Result readOwnPairing(String accountId, String generationId,
            AndroidDeviceIdentity identity, String accessToken, String pairingId) throws Exception {
        String target = "/v1/bootstrap/pairings/" + canonicalId(pairingId);
        String requestId = UUID.randomUUID().toString().toLowerCase(Locale.ROOT);
        long timestamp = System.currentTimeMillis();
        byte[] tokenDigest = MessageDigest.getInstance("SHA-256").digest(accessToken.getBytes(StandardCharsets.UTF_8));
        byte[] emptyDigest = MessageDigest.getInstance("SHA-256").digest(new byte[0]);
        JSONObject proof = new JSONObject();
        proof.put("protocol", "FG-REQUEST-1");
        proof.put("method", "GET");
        proof.put("requestTarget", target);
        proof.put("accountId", accountId);
        proof.put("generationId", generationId);
        proof.put("deviceId", identity.deviceId());
        proof.put("requestId", requestId);
        proof.put("timestampUnixMilliseconds", timestamp);
        proof.put("accessTokenDigest", encode(tokenDigest));
        proof.put("bodyDigest", encode(emptyDigest));
        Arrays.fill(tokenDigest, (byte) 0);
        Arrays.fill(emptyDigest, (byte) 0);
        byte[] canonicalProof = canonical(proof.toString());
        String signature;
        try { signature = identity.sign(PENDING_REQUEST_DOMAIN, canonicalProof); }
        finally { Arrays.fill(canonicalProof, (byte) 0); }
        JSONObject headers = new JSONObject();
        headers.put("X-FG-Request-Id", requestId);
        headers.put("X-FG-Timestamp", API_TIMESTAMP.format(Instant.ofEpochMilli(timestamp)));
        headers.put("X-FG-Pending-Proof", signature);
        HttpResult response = send("GET", target, accessToken, null, headers);
        try {
            if (response.status == 404) return new Result("not_found", identity.publicIdentity().fingerprint(), pairingId);
            if (response.status != 200) throw new IOException("Pairing status unavailable.");
            JSONObject state = parseStrictJson(response.body, fields("pairingId", "deviceId", "state",
                    "expiresAtUtc", "fingerprint", "membershipDigest", "membership", "keyEnvelopes", "membershipChain",
                    "currentMembership", "currentMembershipDigest", "currentKeyEnvelopes", "currentMembershipChain"));
            String currentId = canonicalId(state.getString("pairingId"));
            String deviceId = canonicalId(state.getString("deviceId"));
            String fingerprint = state.getString("fingerprint");
            String status = state.getString("state");
            Instant expires = Instant.parse(state.getString("expiresAtUtc"));
            if (!pairingId.equals(currentId) || !identity.deviceId().equals(deviceId)
                    || !identity.publicIdentity().fingerprint().equals(fingerprint)
                    || !("Pending".equals(status) || "Approved".equals(status) || "Consumed".equals(status)
                        || "Expired".equals(status)))
                throw new IOException("Pairing state rejected.");
            String mapped = pairingStateAtTime(status, expires, Instant.now());
            String membershipJson = state.isNull("membership") ? null : state.getJSONObject("membership").toString();
            String chainJson = state.getJSONArray("membershipChain").toString();
            String envelopesJson = state.isNull("keyEnvelopes") ? null : state.getJSONArray("keyEnvelopes").toString();
            String currentMembershipJson = state.isNull("currentMembership") ? null : state.getJSONObject("currentMembership").toString();
            String currentMembershipDigest = state.getString("currentMembershipDigest");
            String currentEnvelopesJson = state.getJSONArray("currentKeyEnvelopes").toString();
            String currentChainJson = state.getJSONArray("currentMembershipChain").toString();
            return new Result(mapped, fingerprint, pairingId, state.optString("membershipDigest", ""),
                    membershipJson, chainJson, envelopesJson, currentMembershipJson, currentMembershipDigest,
                    currentEnvelopesJson, currentChainJson);
        } finally { Arrays.fill(response.body, (byte) 0); }
    }

    static String pairingStateAtTime(String status, Instant deadline, Instant now) {
        // Deadline limits approval of Pending requests. An already committed
        // membership can finish key receipt after a lost or incompatible response.
        if (!deadline.isAfter(now) && "Pending".equals(status)) status = "Expired";
        return switch (status) {
            case "Pending" -> "pending";
            case "Approved" -> "approved";
            case "Consumed" -> "consumed";
            default -> "expired";
        };
    }

    private Result completeApprovedPairing(String accountBinding, String accountId, String generationId,
            AndroidDeviceIdentity identity, String accessToken, Result approved, SessionRecord savedSession)
            throws Exception {
        if (!"approved".equals(approved.state) || approved.membershipJson == null
                || approved.membershipChainJson == null || approved.keyEnvelopesJson == null
                || approved.membershipDigest == null || approved.membershipDigest.isEmpty())
            throw new IOException("Approved pairing material is incomplete.");

        byte[] contentKey = null;
        byte[] sessionBytes = null;
        try (AndroidPairingAcceptance.AcceptedKey accepted = AndroidPairingAcceptance.verifyAndOpen(
                approved.membershipJson, approved.membershipChainJson, approved.keyEnvelopesJson,
                accountId, generationId, membershipRevision(approved.membershipJson),
                approved.membershipDigest, identity)) {
            if (!approved.membershipDigest.equals(accepted.membershipDigest()))
                throw new IOException("Approved pairing digest rejected.");
            contentKey = accepted.copyContentKey();
            if (savedSession == null) {
                sessionBytes = new byte[32];
                new SecureRandom().nextBytes(sessionBytes);
            } else {
                if (!accountId.equals(savedSession.accountId) || !generationId.equals(savedSession.generationId)
                        || !identity.deviceId().equals(savedSession.deviceId)
                        || !approved.pairingId.equals(savedSession.pairingId)
                        || savedSession.membershipRevision != accepted.membershipRevision()
                        || savedSession.keyEpoch != accepted.keyEpoch()
                        || !approved.membershipDigest.equals(savedSession.membershipDigest))
                    throw new IOException("Prepared pairing session context rejected.");
                byte[] previousKey = savedSession.copyContentKey();
                try {
                    if (!MessageDigest.isEqual(contentKey, previousKey))
                        throw new IOException("Prepared pairing key changed.");
                } finally { Arrays.fill(previousKey, (byte) 0); }
                sessionBytes = savedSession.copySessionBytes();
            }

            try (SessionRecord prepared = new SessionRecord(accountId, generationId, identity.deviceId(),
                    approved.pairingId, accepted.membershipRevision(), accepted.keyEpoch(),
                    accepted.membershipDigest(), contentKey, sessionBytes)) {
                // Persist before the POST. A lost response is retried with the identical session secret.
                saveDeviceSession(accountBinding, prepared);
                ConfirmResult confirmed = confirmPairing(accountId, generationId, identity, accessToken,
                        approved.pairingId, accepted.membershipDigest(), prepared.deviceSession());
                if (!approved.pairingId.equals(confirmed.pairingId) || !"Consumed".equals(confirmed.state)
                        || !confirmed.expiresAt.isAfter(Instant.now())
                        || confirmed.expiresAt.isAfter(Instant.now().plusSeconds(8 * 60 * 60 + 120)))
                    throw new IOException("Pairing confirmation response rejected.");
                clearPendingPairing(accountBinding);
                return new Result("linked", approved.fingerprint, approved.pairingId);
            }
        } finally {
            if (contentKey != null) Arrays.fill(contentKey, (byte) 0);
            if (sessionBytes != null) Arrays.fill(sessionBytes, (byte) 0);
        }
    }

    private Result refreshLinkedSession(String accountBinding, SessionRecord savedSession, String accessToken)
            throws Exception {
        requireTrustedApi();
        if (!vault.exists(accountBinding)) throw new IOException("The linked device identity is unavailable.");
        try (AndroidDeviceIdentity identity = AndroidDeviceIdentity.loadOrCreate(vault, accountBinding)) {
            if (!identity.deviceId().equals(savedSession.deviceId))
                throw new IOException("Linked device identity does not match its encrypted session.");

            Result ownPairing = readOwnPairing(savedSession.accountId, savedSession.generationId, identity,
                    accessToken, savedSession.pairingId);
            if (!"consumed".equals(ownPairing.state))
                throw new IOException("The device membership is not confirmed.");

            HttpResult membershipReply = getCurrentMembership(savedSession, identity, accessToken);
            if (membershipReply.status == 200) {
                MembershipSnapshot snapshot;
                try { snapshot = parseMembershipSnapshot(membershipReply.body); }
                finally { Arrays.fill(membershipReply.body, (byte) 0); }
                try (AndroidPairingAcceptance.AcceptedKey accepted = verifySnapshot(
                        snapshot, savedSession.accountId, savedSession.generationId, identity)) {
                    if (accepted.membershipRevision() != savedSession.membershipRevision
                            || accepted.keyEpoch() != savedSession.keyEpoch
                            || !accepted.membershipDigest().equals(savedSession.membershipDigest))
                        throw new IOException("Linked device session is stale.");
                    byte[] storedKey = savedSession.copyContentKey();
                    byte[] currentKey = accepted.copyContentKey();
                    try {
                        if (!MessageDigest.isEqual(storedKey, currentKey))
                            throw new IOException("Linked device key changed unexpectedly.");
                    } finally {
                        Arrays.fill(storedKey, (byte) 0);
                        Arrays.fill(currentKey, (byte) 0);
                    }
                    return new Result("linked", identity.publicIdentity().fingerprint(), savedSession.pairingId);
                }
            }
            Arrays.fill(membershipReply.body, (byte) 0);
            if (membershipReply.status != 403 && membershipReply.status != 404 && membershipReply.status != 409)
                throw new IOException("Linked device membership unavailable.");

            MembershipSnapshot current = snapshotFromPairing(ownPairing);
            try (AndroidPairingAcceptance.AcceptedKey accepted = verifySnapshot(
                    current, savedSession.accountId, savedSession.generationId, identity)) {
                byte[] contentKey = accepted.copyContentKey();
                byte[] sessionBytes = new byte[32];
                new SecureRandom().nextBytes(sessionBytes);
                try (SessionRecord prepared = new SessionRecord(savedSession.accountId, savedSession.generationId,
                        identity.deviceId(), savedSession.pairingId, accepted.membershipRevision(),
                        accepted.keyEpoch(), accepted.membershipDigest(), contentKey, sessionBytes)) {
                    Arrays.fill(contentKey, (byte) 0);
                    Arrays.fill(sessionBytes, (byte) 0);
                    // Save before renewal so retries after a lost response use the same session hash.
                    saveDeviceSession(accountBinding, prepared);
                    RenewResult renewed = renewDeviceSession(prepared, identity, accessToken);
                    if (!renewed.expiresAt.isAfter(Instant.now())
                            || renewed.expiresAt.isAfter(Instant.now().plusSeconds(8 * 60 * 60 + 120)))
                        throw new IOException("Renewed device session expiry rejected.");
                    try (AndroidPairingAcceptance.AcceptedKey renewedKey = verifySnapshot(
                            renewed.snapshot, savedSession.accountId, savedSession.generationId, identity)) {
                        if (renewedKey.membershipRevision() != prepared.membershipRevision
                                || renewedKey.keyEpoch() != prepared.keyEpoch
                                || !renewedKey.membershipDigest().equals(prepared.membershipDigest))
                            throw new IOException("Renewed device session snapshot changed.");
                    }
                    return new Result("linked", identity.publicIdentity().fingerprint(), savedSession.pairingId);
                }
            }
        }
    }

    private HttpResult getCurrentMembership(SessionRecord session, AndroidDeviceIdentity identity,
            String accessToken) throws Exception {
        String target = "/v1/bootstrap/membership";
        JSONObject headers = signedHeaders(identity, session.accountId, session.generationId, accessToken,
                "GET", target, new byte[0], REQUEST_DOMAIN, "X-FG-Proof");
        headers.put("X-FG-Device-Id", identity.deviceId());
        headers.put("X-FG-Device-Session", session.deviceSession());
        return send("GET", target, accessToken, null, headers);
    }

    private ConfirmResult confirmPairing(String accountId, String generationId, AndroidDeviceIdentity identity,
            String accessToken, String pairingId, String membershipDigest, String deviceSession) throws Exception {
        String target = "/v1/bootstrap/pairings/" + canonicalId(pairingId) + "/confirm";
        JSONObject body = confirmationBody(pairingId, membershipDigest, deviceSession);
        byte[] canonicalBody = canonical(body.toString());
        JSONObject headers;
        try {
            headers = signedHeaders(identity, accountId, generationId, accessToken, "POST", target,
                    canonicalBody, CONFIRM_DOMAIN, "X-FG-Pending-Proof");
        } finally { Arrays.fill(canonicalBody, (byte) 0); }
        HttpResult response = send("POST", target, accessToken, body.toString(), headers);
        try {
            if (response.status != 200) throw new IOException("Pairing confirmation was not accepted.");
            JSONObject result = parseStrictJson(response.body, fields("pairingId", "state", "expiresAtUtc"));
            return new ConfirmResult(canonicalId(result.getString("pairingId")), result.getString("state"),
                    Instant.parse(result.getString("expiresAtUtc")));
        } finally { Arrays.fill(response.body, (byte) 0); }
    }

    private RenewResult renewDeviceSession(SessionRecord session, AndroidDeviceIdentity identity,
            String accessToken) throws Exception {
        String target = "/v1/bootstrap/sessions/renew";
        JSONObject body = renewalBody(session.accountId, session.generationId, session.deviceId,
                session.membershipDigest, session.deviceSession());
        byte[] canonicalBody = canonical(body.toString());
        JSONObject headers;
        try {
            headers = signedHeaders(identity, session.accountId, session.generationId, accessToken,
                    "POST", target, canonicalBody, RENEW_DOMAIN, "X-FG-Proof");
        } finally { Arrays.fill(canonicalBody, (byte) 0); }
        HttpResult response = send("POST", target, accessToken, body.toString(), headers);
        try {
            if (response.status != 200) throw new IOException("Device session renewal was not accepted.");
            JSONObject result = parseStrictJson(response.body, fields("expiresAtUtc", "snapshot"));
            MembershipSnapshot snapshot = parseMembershipSnapshot(result.getJSONObject("snapshot"));
            return new RenewResult(Instant.parse(result.getString("expiresAtUtc")), snapshot);
        } finally { Arrays.fill(response.body, (byte) 0); }
    }

    static JSONObject signedHeaders(AndroidDeviceIdentity identity, String accountId, String generationId,
            String accessToken, String method, String target, byte[] body, String domain, String proofHeader)
            throws Exception {
        String requestId = UUID.randomUUID().toString().toLowerCase(Locale.ROOT);
        long timestamp = System.currentTimeMillis();
        byte[] tokenBytes = accessToken.getBytes(StandardCharsets.UTF_8);
        byte[] tokenDigest;
        try { tokenDigest = MessageDigest.getInstance("SHA-256").digest(tokenBytes); }
        finally { Arrays.fill(tokenBytes, (byte) 0); }
        byte[] bodyDigest = MessageDigest.getInstance("SHA-256").digest(body);
        JSONObject proof = new JSONObject();
        try {
            proof.put("protocol", "FG-REQUEST-1");
            proof.put("method", method);
            proof.put("requestTarget", target);
            proof.put("accountId", accountId);
            proof.put("generationId", generationId);
            proof.put("deviceId", identity.deviceId());
            proof.put("requestId", requestId);
            proof.put("timestampUnixMilliseconds", timestamp);
            proof.put("accessTokenDigest", encode(tokenDigest));
            proof.put("bodyDigest", encode(bodyDigest));
        } finally {
            Arrays.fill(tokenDigest, (byte) 0);
            Arrays.fill(bodyDigest, (byte) 0);
        }
        byte[] canonicalProof = canonical(proof.toString());
        String signature;
        try { signature = identity.sign(domain, canonicalProof); }
        finally { Arrays.fill(canonicalProof, (byte) 0); }
        JSONObject headers = new JSONObject();
        headers.put("X-FG-Request-Id", requestId);
        headers.put("X-FG-Timestamp", API_TIMESTAMP.format(Instant.ofEpochMilli(timestamp)));
        headers.put(proofHeader, signature);
        return headers;
    }

    static JSONObject confirmationBody(String pairingId, String membershipDigest, String deviceSession)
            throws Exception {
        String canonicalPairingId = canonicalId(pairingId);
        byte[] digest = null;
        byte[] session = null;
        try {
            digest = decodeCanonical(membershipDigest, 32);
            session = decodeCanonical(deviceSession, 32);
        } finally {
            if (digest != null) Arrays.fill(digest, (byte) 0);
            if (session != null) Arrays.fill(session, (byte) 0);
        }
        JSONObject body = new JSONObject();
        body.put("pairingId", canonicalPairingId);
        body.put("membershipDigest", membershipDigest);
        body.put("deviceSession", deviceSession);
        return body;
    }

    static JSONObject renewalBody(String accountId, String generationId, String deviceId,
            String membershipDigest, String deviceSession) throws Exception {
        JSONObject body = new JSONObject();
        body.put("accountId", canonicalId(accountId));
        body.put("generationId", canonicalId(generationId));
        body.put("deviceId", canonicalId(deviceId));
        byte[] digest = null;
        byte[] session = null;
        try {
            digest = decodeCanonical(membershipDigest, 32);
            session = decodeCanonical(deviceSession, 32);
        } finally {
            if (digest != null) Arrays.fill(digest, (byte) 0);
            if (session != null) Arrays.fill(session, (byte) 0);
        }
        body.put("membershipDigest", membershipDigest);
        body.put("deviceSession", deviceSession);
        return body;
    }

    private static MembershipSnapshot snapshotFromPairing(Result pairing) throws Exception {
        if (pairing.currentMembershipJson == null || pairing.currentMembershipJson.isEmpty()
                || pairing.currentMembershipDigest == null || pairing.currentMembershipDigest.isEmpty()
                || pairing.currentKeyEnvelopesJson == null || pairing.currentMembershipChainJson == null)
            throw new IOException("Current device membership is unavailable.");
        return new MembershipSnapshot(pairing.currentMembershipJson, pairing.currentMembershipDigest,
                pairing.currentKeyEnvelopesJson, pairing.currentMembershipChainJson);
    }

    private static MembershipSnapshot parseMembershipSnapshot(byte[] bytes) throws Exception {
        JSONObject json = parseStrictJson(bytes, fields("membership", "membershipDigest", "keyEnvelopes", "membershipChain"));
        return parseMembershipSnapshot(json);
    }

    private static MembershipSnapshot parseMembershipSnapshot(JSONObject json) throws Exception {
        byte[] canonical = canonical(json.toString());
        Arrays.fill(canonical, (byte) 0);
        return new MembershipSnapshot(json.getJSONObject("membership").toString(),
                json.getString("membershipDigest"), json.getJSONArray("keyEnvelopes").toString(),
                json.getJSONArray("membershipChain").toString());
    }

    private static AndroidPairingAcceptance.AcceptedKey verifySnapshot(MembershipSnapshot snapshot,
            String accountId, String generationId, AndroidDeviceIdentity identity) throws Exception {
        byte[] digest = decodeCanonical(snapshot.membershipDigest, 32);
        Arrays.fill(digest, (byte) 0);
        return AndroidPairingAcceptance.verifyAndOpen(snapshot.membershipJson, snapshot.membershipChainJson,
                snapshot.keyEnvelopesJson, accountId, generationId,
                membershipRevision(snapshot.membershipJson), snapshot.membershipDigest, identity);
    }

    private static long membershipRevision(String membershipJson) throws Exception {
        JSONObject membership = parseStrictJson(membershipJson,
                fields("protocol", "accountId", "generationId", "baseRevision", "proposedRevision", "keyEpoch",
                        "previousDigest", "members", "recoverySigningPublicKey", "recoveryEncryptionPublicKey",
                        "signerDeviceId", "signature"), 100_000);
        Object revision = membership.get("proposedRevision");
        if (!(revision instanceof Number) || !revision.toString().matches("[1-9][0-9]{0,15}"))
            throw new IOException("Membership revision rejected.");
        return Long.parseLong(revision.toString());
    }

    private HttpResult send(String method, String target, String accessToken, String body, JSONObject headers)
            throws IOException, JSONException {
        Uri uri = API_ORIGIN.buildUpon().encodedPath(target).build();
        if (!"https".equals(uri.getScheme()) || !API_HOST.equals(uri.getHost()) || uri.getPort() != -1
                || uri.getUserInfo() != null || uri.getQuery() != null || uri.getFragment() != null
                || !target.startsWith("/v1/") || target.contains(".."))
            throw new IOException("Native pairing destination rejected.");
        HttpURLConnection connection = (HttpURLConnection) new URL(uri.toString()).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestMethod(method);
        connection.setRequestProperty("Authorization", "Bearer " + accessToken);
        connection.setRequestProperty("Accept", "application/json");
        if (headers != null) {
            Iterator<String> names = headers.keys();
            while (names.hasNext()) {
                String name = names.next();
                connection.setRequestProperty(name, headers.getString(name));
            }
        }
        if (body != null) {
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
            try (OutputStream output = connection.getOutputStream()) { output.write(bytes); }
            finally { Arrays.fill(bytes, (byte) 0); }
        }
        try {
            int status = connection.getResponseCode();
            InputStream stream = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
            byte[] response = stream == null ? new byte[0] : readBounded(stream);
            return new HttpResult(status, response);
        } finally { connection.disconnect(); }
    }

    static JSONObject capabilities() throws JSONException {
        JSONObject capabilities = new JSONObject();
        capabilities.put("platform", "Android");
        capabilities.put("clientVersion", BuildConfig.VERSION_NAME);
        capabilities.put("normal", "Unsupported");
        capabilities.put("strict", "Unsupported");
        capabilities.put("channels", new JSONArray());
        capabilities.put("strictPersistenceVerified", false);
        capabilities.put("recoveryReady", false);
        return capabilities;
    }

    static byte[] canonicalPairingProof(String pairingId, String accountId, String generationId,
            String deviceId, String nonce, JSONObject capabilities) throws Exception {
        JSONObject proof = new JSONObject();
        proof.put("protocol", "FG-PAIRING-1");
        proof.put("pairingId", pairingId);
        proof.put("challengeId", pairingId);
        proof.put("accountId", accountId);
        proof.put("generationId", generationId);
        proof.put("deviceId", deviceId);
        proof.put("nonce", nonce);
        proof.put("capabilities", capabilities);
        return canonical(proof.toString());
    }

    private String loadPendingPairingId(String accountBinding) throws Exception {
        String binding = stateBinding(accountBinding);
        if (!vault.exists(binding)) return null;
        byte[] clear = vault.load(binding);
        try {
            if (clear.length > MAX_METADATA_BYTES) throw new IOException("Pairing metadata rejected.");
            JSONObject metadata = parseStrictJson(new String(clear, StandardCharsets.UTF_8), fields("version", "pairingId"));
            if (metadata.getInt("version") != 1) throw new IOException("Pairing metadata version rejected.");
            return canonicalId(metadata.getString("pairingId"));
        } finally { Arrays.fill(clear, (byte) 0); }
    }

    private void savePendingPairingId(String accountBinding, String pairingId) throws Exception {
        JSONObject metadata = new JSONObject();
        metadata.put("version", 1);
        metadata.put("pairingId", canonicalId(pairingId));
        byte[] clear = metadata.toString().getBytes(StandardCharsets.UTF_8);
        try { vault.save(stateBinding(accountBinding), clear); }
        finally { Arrays.fill(clear, (byte) 0); }
    }

    private void clearPendingPairing(String accountBinding) throws Exception {
        vault.clear(stateBinding(accountBinding));
    }

    private void saveDeviceSession(String accountBinding, SessionRecord record) throws Exception {
        byte[] digest = decodeCanonical(record.membershipDigest, 32);
        byte[] account = asciiId(record.accountId);
        byte[] generation = asciiId(record.generationId);
        byte[] device = asciiId(record.deviceId);
        byte[] pairing = asciiId(record.pairingId);
        byte[] contentKey = record.copyContentKey();
        byte[] session = record.copySessionBytes();
        ByteBuffer output = ByteBuffer.allocate(SESSION_RECORD_BYTES);
        try {
            output.putInt(SESSION_MAGIC).put(SESSION_VERSION).put(account).put(generation).put(device).put(pairing)
                    .putLong(record.membershipRevision).putLong(record.keyEpoch).put(digest).put(contentKey).put(session);
            vault.save(sessionBinding(accountBinding), output.array());
        } finally {
            Arrays.fill(output.array(), (byte) 0);
            Arrays.fill(digest, (byte) 0);
            Arrays.fill(account, (byte) 0);
            Arrays.fill(generation, (byte) 0);
            Arrays.fill(device, (byte) 0);
            Arrays.fill(pairing, (byte) 0);
            Arrays.fill(contentKey, (byte) 0);
            Arrays.fill(session, (byte) 0);
        }
    }

    private SessionRecord loadDeviceSession(String accountBinding) throws Exception {
        String binding = sessionBinding(accountBinding);
        if (!vault.exists(binding)) return null;
        byte[] clear = vault.load(binding);
        byte[] account = new byte[36];
        byte[] generation = new byte[36];
        byte[] device = new byte[36];
        byte[] pairing = new byte[36];
        byte[] digest = new byte[32];
        byte[] contentKey = new byte[32];
        byte[] session = new byte[32];
        try {
            if (clear.length != SESSION_RECORD_BYTES) throw new IOException("Device session vault size rejected.");
            ByteBuffer input = ByteBuffer.wrap(clear);
            if (input.getInt() != SESSION_MAGIC || input.get() != SESSION_VERSION)
                throw new IOException("Device session vault version rejected.");
            input.get(account).get(generation).get(device).get(pairing);
            long revision = input.getLong();
            long keyEpoch = input.getLong();
            input.get(digest).get(contentKey).get(session);
            if (input.hasRemaining() || revision < 1 || keyEpoch < 1 || allZero(contentKey) || allZero(session))
                throw new IOException("Device session vault record rejected.");
            return new SessionRecord(decodeId(account), decodeId(generation), decodeId(device), decodeId(pairing),
                    revision, keyEpoch, encode(digest), contentKey, session);
        } finally {
            Arrays.fill(clear, (byte) 0);
            Arrays.fill(account, (byte) 0);
            Arrays.fill(generation, (byte) 0);
            Arrays.fill(device, (byte) 0);
            Arrays.fill(pairing, (byte) 0);
            Arrays.fill(digest, (byte) 0);
            Arrays.fill(contentKey, (byte) 0);
            Arrays.fill(session, (byte) 0);
        }
    }

    private void clearDeviceSession(String accountBinding) throws Exception {
        vault.clear(sessionBinding(accountBinding));
    }

    private static String stateBinding(String accountBinding) throws GeneralSecurityException {
        if (accountBinding == null || !accountBinding.matches("[A-Za-z0-9_-]{43}"))
            throw new GeneralSecurityException("Account binding rejected.");
        byte[] input = ("FocusGuard.AndroidPendingPairing.v1\0" + accountBinding).getBytes(StandardCharsets.UTF_8);
        try { return encode(MessageDigest.getInstance("SHA-256").digest(input)); }
        finally { Arrays.fill(input, (byte) 0); }
    }

    private static String sessionBinding(String accountBinding) throws GeneralSecurityException {
        if (accountBinding == null || !accountBinding.matches("[A-Za-z0-9_-]{43}"))
            throw new GeneralSecurityException("Account binding rejected.");
        byte[] input = ("FocusGuard.AndroidDeviceSession.v1\0" + accountBinding).getBytes(StandardCharsets.UTF_8);
        try { return encode(MessageDigest.getInstance("SHA-256").digest(input)); }
        finally { Arrays.fill(input, (byte) 0); }
    }

    private static byte[] asciiId(String id) throws IOException {
        canonicalId(id);
        byte[] bytes = id.getBytes(StandardCharsets.US_ASCII);
        if (bytes.length != 36) throw new IOException("Device session identifier size rejected.");
        return bytes;
    }

    private static String decodeId(byte[] bytes) throws IOException {
        return canonicalId(new String(bytes, StandardCharsets.US_ASCII));
    }

    private static boolean allZero(byte[] bytes) {
        int aggregate = 0;
        for (byte value : bytes) aggregate |= value;
        return aggregate == 0;
    }

    private static JSONObject parseStrictJson(byte[] bytes, Set<String> expectedFields) throws Exception {
        if (bytes == null || bytes.length == 0 || bytes.length > MAX_RESPONSE_BYTES)
            throw new IOException("Pairing response size rejected.");
        return parseStrictJson(new String(bytes, StandardCharsets.UTF_8), expectedFields, MAX_RESPONSE_BYTES);
    }

    private static Set<String> fields(String... names) {
        Set<String> result = new HashSet<>();
        result.addAll(Arrays.asList(names));
        return result;
    }

    private static JSONObject parseStrictJson(String json, Set<String> expectedFields) throws Exception {
        return parseStrictJson(json, expectedFields, MAX_METADATA_BYTES);
    }

    private static JSONObject parseStrictJson(String json, Set<String> expectedFields, int maxLength) throws Exception {
        if (json == null || json.isEmpty() || json.length() > maxLength)
            throw new IOException("Pairing metadata size rejected.");
        JSONObject parsed = new JSONObject(json);
        byte[] canonical = canonical(json);
        Arrays.fill(canonical, (byte) 0);
        Set<String> keys = new HashSet<>();
        Iterator<String> names = parsed.keys();
        while (names.hasNext()) keys.add(names.next());
        if (!keys.equals(expectedFields)) throw new IOException("Pairing response fields rejected.");
        return parsed;
    }

    private static byte[] canonical(String json) throws IOException {
        return new JsonCanonicalizer(json).getEncodedUTF8();
    }

    private static byte[] readBounded(InputStream input) throws IOException {
        try (InputStream stream = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] chunk = new byte[4096];
            int total = 0;
            try {
                int count;
                while ((count = stream.read(chunk)) != -1) {
                    total += count;
                    if (total > MAX_RESPONSE_BYTES) throw new IOException("Pairing response too large.");
                    output.write(chunk, 0, count);
                }
                return output.toByteArray();
            } finally { Arrays.fill(chunk, (byte) 0); }
        }
    }

    private static void requireTrustedApi() throws IOException {
        if (!"https".equals(API_ORIGIN.getScheme()) || !API_HOST.equals(API_ORIGIN.getHost())
                || API_ORIGIN.getPort() != -1 || API_ORIGIN.getUserInfo() != null
                || API_ORIGIN.getQuery() != null || API_ORIGIN.getFragment() != null)
            throw new IOException("Native pairing API origin rejected.");
    }

    private static void requireToken(String token) throws IOException {
        if (token == null || token.isEmpty() || token.length() > 16384)
            throw new IOException("Access token rejected.");
    }

    private static void requireId(String id) throws IOException {
        if (id == null) throw new IOException("Account context rejected.");
        canonicalId(id);
    }

    private static String canonicalId(String value) throws IOException {
        try {
            String canonical = UUID.fromString(value).toString().toLowerCase(Locale.ROOT);
            if (!canonical.equals(value)) throw new IOException("Pairing identifier rejected.");
            return canonical;
        } catch (RuntimeException exception) {
            throw new IOException("Pairing identifier rejected.", exception);
        }
    }

    private static byte[] decodeCanonical(String value, int expectedLength) throws IOException {
        try {
            byte[] bytes = Base64.getUrlDecoder().decode(value);
            if (bytes.length != expectedLength || !encode(bytes).equals(value)) {
                Arrays.fill(bytes, (byte) 0);
                throw new IOException("Pairing nonce rejected.");
            }
            return bytes;
        } catch (IllegalArgumentException exception) {
            throw new IOException("Pairing nonce rejected.", exception);
        }
    }

    private static String encode(byte[] bytes) {
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static final class MembershipSnapshot {
        final String membershipJson;
        final String membershipDigest;
        final String keyEnvelopesJson;
        final String membershipChainJson;

        MembershipSnapshot(String membershipJson, String membershipDigest, String keyEnvelopesJson,
                String membershipChainJson) {
            this.membershipJson = membershipJson;
            this.membershipDigest = membershipDigest;
            this.keyEnvelopesJson = keyEnvelopesJson;
            this.membershipChainJson = membershipChainJson;
        }
    }

    private static final class ConfirmResult {
        final String pairingId;
        final String state;
        final Instant expiresAt;

        ConfirmResult(String pairingId, String state, Instant expiresAt) {
            this.pairingId = pairingId;
            this.state = state;
            this.expiresAt = expiresAt;
        }
    }

    private static final class RenewResult {
        final Instant expiresAt;
        final MembershipSnapshot snapshot;

        RenewResult(Instant expiresAt, MembershipSnapshot snapshot) {
            this.expiresAt = expiresAt;
            this.snapshot = snapshot;
        }
    }

    static final class Result {
        final String state;
        final String fingerprint;
        final String pairingId;
        final String membershipDigest;
        final String membershipJson;
        final String membershipChainJson;
        final String keyEnvelopesJson;
        final String currentMembershipJson;
        final String currentMembershipDigest;
        final String currentKeyEnvelopesJson;
        final String currentMembershipChainJson;
        Result(String state, String fingerprint, String pairingId) {
            this(state, fingerprint, pairingId, "", null, null, null, null, "", "[]", "[]");
        }
        Result(String state, String fingerprint, String pairingId, String membershipDigest,
                String membershipJson, String membershipChainJson, String keyEnvelopesJson,
                String currentMembershipJson, String currentMembershipDigest,
                String currentKeyEnvelopesJson, String currentMembershipChainJson) {
            this.state = state;
            this.fingerprint = fingerprint;
            this.pairingId = pairingId;
            this.membershipDigest = membershipDigest;
            this.membershipJson = membershipJson;
            this.membershipChainJson = membershipChainJson;
            this.keyEnvelopesJson = keyEnvelopesJson;
            this.currentMembershipJson = currentMembershipJson;
            this.currentMembershipDigest = currentMembershipDigest;
            this.currentKeyEnvelopesJson = currentKeyEnvelopesJson;
            this.currentMembershipChainJson = currentMembershipChainJson;
        }
    }

    private static final class SessionRecord implements AutoCloseable {
        final String accountId;
        final String generationId;
        final String deviceId;
        final String pairingId;
        final long membershipRevision;
        final long keyEpoch;
        final String membershipDigest;
        private byte[] contentKey;
        private byte[] sessionBytes;

        SessionRecord(String accountId, String generationId, String deviceId, String pairingId,
                long membershipRevision, long keyEpoch, String membershipDigest, byte[] contentKey, byte[] sessionBytes) {
            this.accountId = accountId;
            this.generationId = generationId;
            this.deviceId = deviceId;
            this.pairingId = pairingId;
            this.membershipRevision = membershipRevision;
            this.keyEpoch = keyEpoch;
            this.membershipDigest = membershipDigest;
            this.contentKey = contentKey.clone();
            this.sessionBytes = sessionBytes.clone();
        }

        byte[] copyContentKey() {
            if (contentKey == null) throw new IllegalStateException("Device content key was cleared.");
            return contentKey.clone();
        }

        byte[] copySessionBytes() {
            if (sessionBytes == null) throw new IllegalStateException("Device session was cleared.");
            return sessionBytes.clone();
        }

        String deviceSession() {
            byte[] copied = copySessionBytes();
            try { return encode(copied); }
            finally { Arrays.fill(copied, (byte) 0); }
        }

        @Override public void close() {
            if (contentKey != null) Arrays.fill(contentKey, (byte) 0);
            if (sessionBytes != null) Arrays.fill(sessionBytes, (byte) 0);
            contentKey = null;
            sessionBytes = null;
        }
    }

    private static final class HttpResult {
        final int status;
        final byte[] body;
        HttpResult(int status, byte[] body) { this.status = status; this.body = body; }
    }
}
