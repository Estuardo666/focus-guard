package com.totemmassmedia.focusguard;

import android.content.Context;
import android.content.Intent;
import android.app.Instrumentation;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;
import android.net.Uri;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import app.focusguard.phase0.AndroidDeviceIdentity;
import app.focusguard.phase0.AndroidDeviceVault;
import app.focusguard.phase0.AndroidMembershipVerifier;
import app.focusguard.phase0.AndroidPairingAcceptance;
import app.focusguard.phase0.AndroidSignedKeyEnvelopeCodec;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;
import net.openid.appauth.AuthorizationException;
import net.openid.appauth.AuthorizationRequest;
import net.openid.appauth.ResponseTypeValues;
import net.openid.appauth.AuthorizationServiceConfiguration;
import org.webpki.jcs.JsonCanonicalizer;

import java.nio.charset.StandardCharsets;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

@RunWith(AndroidJUnit4.class)
public final class AndroidDeviceIdentityInstrumentedTest {
    @Test public void linkedActivityShowsOneVerifiedMessageAndRefreshesTheLink() {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Context context = instrumentation.getTargetContext();
        MainActivity activity = (MainActivity) instrumentation.startActivitySync(
                new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        try {
            int[] counts = new int[5];
            instrumentation.runOnMainSync(() -> {
                activity.onState(new AndroidOidcAccountSession.ViewState("pairing_linked",
                        "ui-test@example.invalid", true, true, "A".repeat(43)));
                View root = activity.findViewById(android.R.id.content);
                counts[0] = visibleTextCount(root, activity.getString(R.string.status_pairing_linked));
                counts[1] = visibleTextCount(root, "Actualizar vínculo");
                counts[2] = visibleTextCount(root, activity.getString(R.string.refresh_pairing));

                activity.onState(new AndroidOidcAccountSession.ViewState("pairing_pending",
                        "ui-test@example.invalid", true, true, "A".repeat(43)));
                counts[3] = visibleTextCount(root, activity.getString(R.string.status_pairing_pending));
                counts[4] = visibleTextCount(root, activity.getString(R.string.refresh_pairing));
            });
            assertEquals(1, counts[0]);
            assertEquals(1, counts[1]);
            assertEquals(0, counts[2]);
            assertEquals(1, counts[3]);
            assertEquals(1, counts[4]);
        } finally { instrumentation.runOnMainSync(activity::finish); }
    }

    private static int visibleTextCount(View view, String text) {
        if (view.getVisibility() != View.VISIBLE) return 0;
        int count = view instanceof TextView && text.contentEquals(((TextView) view).getText()) ? 1 : 0;
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int index = 0; index < group.getChildCount(); index++)
                count += visibleTextCount(group.getChildAt(index), text);
        }
        return count;
    }

    @Test public void approvedKeyReceiptSurvivesPendingDeadlineButUnapprovedRequestExpires() {
        java.time.Instant now = java.time.Instant.parse("2026-10-13T18:00:00Z");
        java.time.Instant elapsed = now.minusSeconds(1);
        assertEquals("expired", AndroidPairingService.pairingStateAtTime("Pending", elapsed, now));
        assertEquals("approved", AndroidPairingService.pairingStateAtTime("Approved", elapsed, now));
        assertEquals("consumed", AndroidPairingService.pairingStateAtTime("Consumed", elapsed, now));
    }

    @Test public void verifiedSignedInAccountDoesNotDisplaySignedOutMessage() {
        assertEquals(R.string.status_account_connected, MainActivity.statusResource("account_exists"));
        assertEquals(R.string.status_account_connected, MainActivity.statusResource("first_device"));
        assertEquals(R.string.status_signed_out, MainActivity.statusResource("signed_out"));
    }

    @Test public void nativeIdentitySignsAndSurvivesEncryptedVaultReload() throws Exception {
        Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        AndroidDeviceVault vault = new AndroidDeviceVault(context);
        String binding = Base64.getUrlEncoder().withoutPadding().encodeToString(
                MessageDigest.getInstance("SHA-256").digest("FocusGuard Android instrumentation only".getBytes(StandardCharsets.UTF_8)));
        byte[] payload = "synthetic pairing proof".getBytes(StandardCharsets.UTF_8);
        AndroidDeviceIdentity first = null;
        AndroidDeviceIdentity restored = null;
        try {
            first = AndroidDeviceIdentity.loadOrCreate(vault, binding);
            String fingerprint = first.publicIdentity().fingerprint();
            String deviceId = first.deviceId();
            String signature = first.sign("FG-PAIRING-1", payload);
            assertTrue(AndroidDeviceIdentity.verify(first.publicIdentity(), "FG-PAIRING-1", payload, signature));
            assertFalse(AndroidDeviceIdentity.verify(first.publicIdentity(), "FG-REQUEST-1", payload, signature));
            first.close();
            first = null;

            restored = AndroidDeviceIdentity.loadOrCreate(vault, binding);
            assertEquals(fingerprint, restored.publicIdentity().fingerprint());
            assertEquals(deviceId, restored.deviceId());
            assertTrue(AndroidDeviceIdentity.verify(restored.publicIdentity(), "FG-PAIRING-1", payload,
                    restored.sign("FG-PAIRING-1", payload)));
        } finally {
            if (first != null) first.close();
            if (restored != null) restored.close();
            vault.clear(binding);
            Arrays.fill(payload, (byte) 0);
        }
    }

    @Test public void pairingProofUsesTheSharedCanonicalProtocolShape() throws Exception {
        String accountId = "11111111-1111-4111-8111-111111111111";
        String generationId = "22222222-2222-4222-8222-222222222222";
        String deviceId = "33333333-3333-4333-8333-333333333333";
        String nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        byte[] canonical = AndroidPairingService.canonicalPairingProof(deviceId, accountId, generationId,
                deviceId, nonce, AndroidPairingService.capabilities());
        try {
            String actual = new String(canonical, StandardCharsets.UTF_8);
            String expected = "{\"accountId\":\"11111111-1111-4111-8111-111111111111\","
                    + "\"capabilities\":{\"channels\":[],\"clientVersion\":\"" + BuildConfig.VERSION_NAME + "\","
                    + "\"normal\":\"Unsupported\",\"platform\":\"Android\",\"recoveryReady\":false,"
                    + "\"strict\":\"Unsupported\",\"strictPersistenceVerified\":false},"
                    + "\"challengeId\":\"33333333-3333-4333-8333-333333333333\","
                    + "\"deviceId\":\"33333333-3333-4333-8333-333333333333\","
                    + "\"generationId\":\"22222222-2222-4222-8222-222222222222\","
                    + "\"nonce\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\","
                    + "\"pairingId\":\"33333333-3333-4333-8333-333333333333\","
                    + "\"protocol\":\"FG-PAIRING-1\"}";
            assertEquals(expected, actual);
        } finally { Arrays.fill(canonical, (byte) 0); }
    }

    @Test public void approvedPairingAndSessionProofsUseTheSharedCanonicalEnvelope() throws Exception {
        Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        AndroidDeviceVault vault = new AndroidDeviceVault(context);
        String binding = Base64.getUrlEncoder().withoutPadding().encodeToString(
                MessageDigest.getInstance("SHA-256").digest("FocusGuard request-proof instrumentation".getBytes(StandardCharsets.UTF_8)));
        String accountId = "11111111-1111-4111-8111-111111111111";
        String generationId = "22222222-2222-4222-8222-222222222222";
        String pairingId = "33333333-3333-4333-8333-333333333333";
        String digest = Base64.getUrlEncoder().withoutPadding().encodeToString(new byte[32]);
        String deviceSession = Base64.getUrlEncoder().withoutPadding().encodeToString(new byte[32]);
        AndroidDeviceIdentity identity = null;
        byte[] body = null;
        byte[] confirmBody = null;
        byte[] renewBody = null;
        try {
            identity = AndroidDeviceIdentity.loadOrCreate(vault, binding);
            confirmBody = new JsonCanonicalizer(AndroidPairingService.confirmationBody(
                    pairingId, digest, deviceSession).toString()).getEncodedUTF8();
            assertEquals("{\"deviceSession\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\","
                    + "\"membershipDigest\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\","
                    + "\"pairingId\":\"33333333-3333-4333-8333-333333333333\"}",
                    new String(confirmBody, StandardCharsets.UTF_8));

            renewBody = new JsonCanonicalizer(AndroidPairingService.renewalBody(accountId, generationId,
                    identity.deviceId(), digest, deviceSession).toString()).getEncodedUTF8();
            assertTrue(new String(renewBody, StandardCharsets.UTF_8).contains("\"deviceId\":\"" + identity.deviceId() + "\""));

            body = "{\"synthetic\":\"request body\"}".getBytes(StandardCharsets.UTF_8);
            assertRequestProof(identity, accountId, generationId, "GET", "/v1/bootstrap/membership", body,
                    "FG-REQUEST-1", "X-FG-Proof");
            assertRequestProof(identity, accountId, generationId, "POST",
                    "/v1/bootstrap/pairings/" + pairingId + "/confirm", confirmBody,
                    "FG-PAIRING-CONFIRM-1", "X-FG-Pending-Proof");
            assertRequestProof(identity, accountId, generationId, "POST", "/v1/bootstrap/sessions/renew", renewBody,
                    "FG-DEVICE-SESSION-RENEW-1", "X-FG-Proof");
        } finally {
            if (identity != null) identity.close();
            vault.clear(binding);
            if (body != null) Arrays.fill(body, (byte) 0);
            if (confirmBody != null) Arrays.fill(confirmBody, (byte) 0);
            if (renewBody != null) Arrays.fill(renewBody, (byte) 0);
        }
    }

    @Test public void approvedPairingVerifiesWholeMembershipAndEnvelopeSetBeforeOpeningKey() throws Exception {
        Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        AndroidDeviceVault vault = new AndroidDeviceVault(context);
        String accountId = "11111111-1111-4111-8111-111111111111";
        String generationId = "22222222-2222-4222-8222-222222222222";
        String ownerBinding = binding("phase2-pairing-owner");
        String phoneBinding = binding("phase2-pairing-phone");
        String recoveryBinding = binding("phase2-pairing-recovery");
        String outsiderBinding = binding("phase2-pairing-outsider");
        AndroidDeviceIdentity owner = null;
        AndroidDeviceIdentity phone = null;
        AndroidDeviceIdentity recovery = null;
        AndroidDeviceIdentity outsider = null;
        byte[] accountKey = new byte[32];
        try {
            owner = AndroidDeviceIdentity.loadOrCreate(vault, ownerBinding);
            phone = AndroidDeviceIdentity.loadOrCreate(vault, phoneBinding);
            recovery = AndroidDeviceIdentity.loadOrCreate(vault, recoveryBinding);
            outsider = AndroidDeviceIdentity.loadOrCreate(vault, outsiderBinding);
            new SecureRandom().nextBytes(accountKey);

            String zeroDigest = encode(new byte[32]);
            String initial = signedMembership(accountId, generationId, 0, 1, 1, zeroDigest,
                    new JSONObject[] { member(owner, "Active", "ManageDevices", "Read", "StartNormal",
                            "StartStrict", "WriteSettings") }, recovery, owner);
            String initialChain = new JSONArray().put(new JSONObject(initial)).toString();
            String initialDigest = AndroidMembershipVerifier.verifyChainJson(
                    initialChain, accountId, generationId).digest();

            String current = signedMembership(accountId, generationId, 1, 2, 1, initialDigest,
                    new JSONObject[] {
                            member(owner, "Active", "ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"),
                            member(phone, "Active", "Read", "StartNormal", "WriteSettings")
                    }, recovery, owner);
            String chain = new JSONArray().put(new JSONObject(initial)).put(new JSONObject(current)).toString();
            AndroidMembershipVerifier.VerifiedMembership verified = AndroidMembershipVerifier.verifyChainJson(
                    chain, accountId, generationId);
            String digest = verified.digest();

            AndroidSignedKeyEnvelopeCodec.SignedEnvelope ownerEnvelope = AndroidSignedKeyEnvelopeCodec.sealForDevice(
                    accountKey, new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(accountId, generationId, 1,
                            owner.deviceId(), digest, owner.deviceId()), owner.publicIdentity().encryptionPublicKey(), owner, verified);
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope phoneEnvelope = AndroidSignedKeyEnvelopeCodec.sealForDevice(
                    accountKey, new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(accountId, generationId, 1,
                            phone.deviceId(), digest, owner.deviceId()), phone.publicIdentity().encryptionPublicKey(), owner, verified);
            AndroidSignedKeyEnvelopeCodec.SignedEnvelope recoveryEnvelope = AndroidSignedKeyEnvelopeCodec.sealForRecovery(accountKey,
                    new AndroidSignedKeyEnvelopeCodec.EnvelopeContext(accountId, generationId, 1,
                            accountId, digest, owner.deviceId()), recovery.publicIdentity().encryptionPublicKey(), owner, verified);
            JSONArray envelopes = new JSONArray()
                    .put(envelopeJson(ownerEnvelope))
                    .put(envelopeJson(phoneEnvelope))
                    .put(envelopeJson(recoveryEnvelope));
            String envelopeJson = envelopes.toString();

            try (AndroidPairingAcceptance.AcceptedKey opened = AndroidPairingAcceptance.verifyAndOpen(
                    current, chain, envelopeJson, accountId, generationId, 2, digest, phone)) {
                byte[] recovered = opened.copyContentKey();
                try { assertTrue(MessageDigest.isEqual(accountKey, recovered)); }
                finally { Arrays.fill(recovered, (byte) 0); }
                assertEquals(2, opened.membershipRevision());
                assertEquals(1, opened.keyEpoch());
                assertEquals(digest, opened.membershipDigest());
            }

            JSONArray missingRecovery = new JSONArray().put(envelopeJson(ownerEnvelope)).put(envelopeJson(phoneEnvelope));
            assertPairingRejected(current, chain, missingRecovery.toString(), accountId, generationId, digest, phone);

            JSONArray tamperedRecovery = new JSONArray(envelopeJson.toString());
            JSONObject changed = new JSONObject(tamperedRecovery.getJSONObject(2).toString());
            changed.put("signature", encode(new byte[64]));
            tamperedRecovery.put(2, changed);
            assertPairingRejected(current, chain, tamperedRecovery.toString(), accountId, generationId, digest, phone);
            assertPairingRejected(current, chain, envelopeJson, accountId, generationId, digest, outsider);
            assertPairingRejected(current, chain, envelopeJson,
                    "33333333-3333-4333-8333-333333333333", generationId, digest, phone);
        } finally {
            if (owner != null) owner.close();
            if (phone != null) phone.close();
            if (recovery != null) recovery.close();
            if (outsider != null) outsider.close();
            vault.clear(ownerBinding);
            vault.clear(phoneBinding);
            vault.clear(recoveryBinding);
            vault.clear(outsiderBinding);
            Arrays.fill(accountKey, (byte) 0);
        }
    }

    @Test public void appAuthFetchesThePublishedIssuerDiscoveryOnAndroid() throws Exception {
        CountDownLatch completed = new CountDownLatch(1);
        AtomicReference<AuthorizationServiceConfiguration> configuration = new AtomicReference<>();
        AtomicReference<AuthorizationException> failure = new AtomicReference<>();
        AuthorizationServiceConfiguration.fetchFromIssuer(Uri.parse(BuildConfig.FOCUSGUARD_ISSUER),
                (result, exception) -> {
                    configuration.set(result);
                    failure.set(exception);
                    completed.countDown();
                });
        assertTrue("OIDC discovery did not finish within the Android test timeout.",
                completed.await(30, TimeUnit.SECONDS));
        if (failure.get() != null) throw new AssertionError("AppAuth could not load OIDC discovery.", failure.get());
        AuthorizationServiceConfiguration fetched = configuration.get();
        assertTrue(fetched != null);
        assertEquals("auth.focusguard.totemmassmedia.com", fetched.authorizationEndpoint.getHost());

        AuthorizationRequest request = new AuthorizationRequest.Builder(fetched,
                BuildConfig.FOCUSGUARD_OIDC_CLIENT_ID, ResponseTypeValues.CODE,
                Uri.parse(BuildConfig.FOCUSGUARD_REDIRECT_URI)).setScope("openid profile email").build();
        assertTrue(request.codeVerifier != null && !request.codeVerifier.isEmpty());
        assertEquals("S256", request.codeVerifierChallengeMethod);
        Uri authorizationUri = request.toUri();
        assertEquals("https", authorizationUri.getScheme());
        assertEquals("auth.focusguard.totemmassmedia.com", authorizationUri.getHost());
        assertEquals("/realms/focusguard/protocol/openid-connect/auth", authorizationUri.getPath());

        HttpURLConnection connection = (HttpURLConnection) new URL(authorizationUri.toString()).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(10000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("Accept", "text/html");
        try {
            assertEquals("Android public client receives the Keycloak login page.", 200, connection.getResponseCode());
            assertTrue(connection.getContentType().startsWith("text/html"));
            byte[] page = readBounded(connection.getInputStream(), 64 * 1024);
            try {
                String html = new String(page, StandardCharsets.UTF_8);
                assertTrue("Keycloak authorization response is not a login form.",
                        html.contains("type=\"password\"") || html.contains("type='password'"));
            } finally { Arrays.fill(page, (byte) 0); }
        } finally { connection.disconnect(); }
    }

    private static String binding(String value) throws Exception {
        byte[] input = value.getBytes(StandardCharsets.UTF_8);
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(input);
        try { return encode(digest); }
        finally {
            Arrays.fill(input, (byte) 0);
            Arrays.fill(digest, (byte) 0);
        }
    }

    private static JSONObject member(AndroidDeviceIdentity identity, String status, String... roles) throws Exception {
        JSONArray roleArray = new JSONArray();
        for (String role : roles) roleArray.put(role);
        JSONObject result = new JSONObject();
        result.put("deviceId", identity.deviceId());
        result.put("signingPublicKey", identity.publicIdentity().signingPublicKey());
        result.put("encryptionPublicKey", identity.publicIdentity().encryptionPublicKey());
        result.put("status", status);
        result.put("roles", roleArray);
        return result;
    }

    private static String signedMembership(String accountId, String generationId, long baseRevision,
            long proposedRevision, long keyEpoch, String previousDigest, JSONObject[] members,
            AndroidDeviceIdentity recovery, AndroidDeviceIdentity signer) throws Exception {
        JSONArray memberArray = new JSONArray();
        for (JSONObject value : members) memberArray.put(value);
        JSONObject unsigned = new JSONObject();
        unsigned.put("protocol", "FG-MEMBERSHIP-1");
        unsigned.put("accountId", accountId);
        unsigned.put("generationId", generationId);
        unsigned.put("baseRevision", baseRevision);
        unsigned.put("proposedRevision", proposedRevision);
        unsigned.put("keyEpoch", keyEpoch);
        unsigned.put("previousDigest", previousDigest);
        unsigned.put("members", memberArray);
        unsigned.put("recoverySigningPublicKey", recovery.publicIdentity().signingPublicKey());
        unsigned.put("recoveryEncryptionPublicKey", recovery.publicIdentity().encryptionPublicKey());
        unsigned.put("signerDeviceId", signer.deviceId());
        byte[] canonical = new JsonCanonicalizer(unsigned.toString()).getEncodedUTF8();
        String signature;
        try { signature = signer.sign("FG-MEMBERSHIP-1", canonical); }
        finally { Arrays.fill(canonical, (byte) 0); }
        unsigned.put("signature", signature);
        return unsigned.toString();
    }

    private static JSONObject envelopeJson(AndroidSignedKeyEnvelopeCodec.SignedEnvelope envelope) throws Exception {
        byte[] canonical = AndroidSignedKeyEnvelopeCodec.canonicalEnvelopeJson(envelope);
        try { return new JSONObject(new String(canonical, StandardCharsets.UTF_8)); }
        finally { Arrays.fill(canonical, (byte) 0); }
    }

    private static void assertPairingRejected(String membership, String chain, String envelopes,
            String accountId, String generationId, String digest, AndroidDeviceIdentity identity) throws Exception {
        boolean rejected = false;
        try (AndroidPairingAcceptance.AcceptedKey ignored = AndroidPairingAcceptance.verifyAndOpen(
                membership, chain, envelopes, accountId, generationId, 2, digest, identity)) {
            // Any returned key means the hostile pairing material was accepted.
        } catch (GeneralSecurityException expected) { rejected = true; }
        assertTrue("Invalid or incomplete pairing material was accepted.", rejected);
    }

    private static String encode(byte[] bytes) {
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    private static byte[] readBounded(InputStream input, int limit) throws Exception {
        try (InputStream stream = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[2048];
            int total = 0;
            try {
                int count;
                while ((count = stream.read(buffer)) != -1) {
                    total += count;
                    if (total > limit) throw new AssertionError("OIDC login response exceeded the test limit.");
                    output.write(buffer, 0, count);
                }
                return output.toByteArray();
            } finally { Arrays.fill(buffer, (byte) 0); }
        }
    }

    private static void assertRequestProof(AndroidDeviceIdentity identity, String accountId, String generationId,
            String method, String target, byte[] body, String domain, String header) throws Exception {
        String token = "synthetic-oidc-access-token";
        JSONObject headers = AndroidPairingService.signedHeaders(identity, accountId, generationId, token,
                method, target, body, domain, header);
        byte[] tokenBytes = token.getBytes(StandardCharsets.UTF_8);
        byte[] tokenDigest = MessageDigest.getInstance("SHA-256").digest(tokenBytes);
        byte[] bodyDigest = MessageDigest.getInstance("SHA-256").digest(body);
        byte[] canonical = null;
        try {
            JSONObject proof = new JSONObject();
            proof.put("protocol", "FG-REQUEST-1");
            proof.put("method", method);
            proof.put("requestTarget", target);
            proof.put("accountId", accountId);
            proof.put("generationId", generationId);
            proof.put("deviceId", identity.deviceId());
            proof.put("requestId", headers.getString("X-FG-Request-Id"));
            proof.put("timestampUnixMilliseconds", java.time.Instant.parse(
                    headers.getString("X-FG-Timestamp")).toEpochMilli());
            proof.put("accessTokenDigest", Base64.getUrlEncoder().withoutPadding().encodeToString(tokenDigest));
            proof.put("bodyDigest", Base64.getUrlEncoder().withoutPadding().encodeToString(bodyDigest));
            canonical = new JsonCanonicalizer(proof.toString()).getEncodedUTF8();
            assertTrue(AndroidDeviceIdentity.verify(identity.publicIdentity(), domain, canonical,
                    headers.getString(header)));
            assertFalse(AndroidDeviceIdentity.verify(identity.publicIdentity(), "FG-WRONG-DOMAIN", canonical,
                    headers.getString(header)));
        } finally {
            Arrays.fill(tokenBytes, (byte) 0);
            Arrays.fill(tokenDigest, (byte) 0);
            Arrays.fill(bodyDigest, (byte) 0);
            if (canonical != null) Arrays.fill(canonical, (byte) 0);
        }
    }
}
