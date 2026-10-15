package com.totemmassmedia.focusguard;

import android.app.Activity;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;

import net.openid.appauth.AuthState;
import net.openid.appauth.AuthorizationException;
import net.openid.appauth.AuthorizationRequest;
import net.openid.appauth.AuthorizationResponse;
import net.openid.appauth.AuthorizationService;
import net.openid.appauth.AuthorizationServiceConfiguration;
import net.openid.appauth.ResponseTypeValues;
import net.openid.appauth.TokenResponse;

import app.focusguard.phase0.AndroidDeviceVault;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URLEncoder;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Base64;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicLong;

/** Native-only OIDC state and bootstrap account-context reader; credentials/tokens never cross into UI state. */
public final class AndroidOidcAccountSession implements AutoCloseable {
    public static final int AUTH_REQUEST_CODE = 7412;
    private static final Uri ISSUER = Uri.parse(BuildConfig.FOCUSGUARD_ISSUER);
    private static final Uri API_ORIGIN = Uri.parse(BuildConfig.FOCUSGUARD_API_ORIGIN);
    private static final Uri REDIRECT_URI = Uri.parse(BuildConfig.FOCUSGUARD_REDIRECT_URI);
    private static final String CLIENT_ID = BuildConfig.FOCUSGUARD_OIDC_CLIENT_ID;
    private static final int MAX_RESPONSE_BYTES = 16 * 1024;
    private static final String STATUS_SIGNED_OUT = "signed_out";
    private static final String STATUS_LOADING = "loading";
    private static final String STATUS_IDENTITY_ERROR = "identity_error";
    private static final String STATUS_ACCOUNT_ERROR = "account_error";
    private static final String STATUS_PAIRING_LOADING = "pairing_loading";
    private static final String STATUS_PAIRING_PENDING = "pairing_pending";
    private static final String STATUS_PAIRING_APPROVED = "pairing_approved";
    private static final String STATUS_PAIRING_LINKED = "pairing_linked";
    private static final String STATUS_PAIRING_EXPIRED = "pairing_expired";
    private static final String STATUS_PAIRING_ERROR = "pairing_error";
    private static final String STATUS_EMAIL_UNVERIFIED = "email_unverified";
    private static final String STATUS_ACCOUNT_EXISTS = "account_exists";
    private static final String STATUS_FIRST_DEVICE = "first_device";
    private static final String STATUS_LOCAL_SIGNOUT = "local_signout";
    private static final String STATUS_REMOTE_SIGNOUT_PENDING = "remote_signout_pending";
    private static final String STATUS_LOCAL_CLEANUP_FAILED = "local_cleanup_failed";

    private final Activity activity;
    private final AndroidDeviceVault vault;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService io = Executors.newSingleThreadExecutor();
    private final AtomicLong generation = new AtomicLong();
    private final Listener listener;
    private AuthorizationService authorizationService;
    private AuthState authState;
    private ViewState viewState = new ViewState(STATUS_SIGNED_OUT, null, false, false);
    private String currentAccountBinding;
    private String currentAccountId;
    private String currentGenerationId;
    private Long currentMembershipRevision;
    private volatile boolean closed;

    public AndroidOidcAccountSession(Activity activity, Listener listener) {
        this.activity = activity;
        this.listener = listener;
        this.vault = new AndroidDeviceVault(activity.getApplicationContext());
        this.authorizationService = new AuthorizationService(activity);
    }

    public ViewState currentState() { return viewState; }
    String accountBindingForNativePairing() { return currentAccountBinding; }
    String accountIdForNativePairing() { return currentAccountId; }
    String generationIdForNativePairing() { return currentGenerationId; }
    Long membershipRevisionForNativePairing() { return currentMembershipRevision; }

    public void restore() {
        long operation = generation.incrementAndGet();
        publish(new ViewState(STATUS_LOADING, null, false, false));
        io.execute(() -> {
            AuthState restored = null;
            try {
                if (vault.exists(sessionBinding())) {
                    byte[] clear = vault.load(sessionBinding());
                    try { restored = AuthState.jsonDeserialize(new String(clear, StandardCharsets.UTF_8)); }
                    finally { Arrays.fill(clear, (byte) 0); }
                    if (!isExpectedClient(restored)) {
                        vault.clear(sessionBinding());
                        restored = null;
                    }
                }
            } catch (Exception exception) {
                clearVaultQuietly();
                restored = null;
            }
            if (closed || operation != generation.get()) return;
            if (restored == null || !restored.isAuthorized()) {
                authState = null;
                clearAccountContext();
                publish(new ViewState(STATUS_SIGNED_OUT, null, false, false));
                return;
            }
            authState = restored;
            refreshAndReadAccount(operation);
        });
    }

    /** Starts AppAuth's authorization-code flow in the system browser (never in a WebView). */
    public void signIn() {
        long operation = generation.incrementAndGet();
        publish(new ViewState(STATUS_LOADING, null, false, false));
        if (!isTrustedIssuer() || !isTrustedApiOrigin()) {
            publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
            return;
        }
        AuthorizationServiceConfiguration.fetchFromIssuer(ISSUER, (configuration, exception) -> {
            if (closed || operation != generation.get()) return;
            if (exception != null || !isExpectedConfiguration(configuration)) {
                publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
                return;
            }
            AuthorizationRequest request = new AuthorizationRequest.Builder(configuration, CLIENT_ID,
                    ResponseTypeValues.CODE, REDIRECT_URI).setScope("openid profile email").build();
            try { activity.startActivityForResult(authorizationService.getAuthorizationRequestIntent(request), AUTH_REQUEST_CODE); }
            catch (RuntimeException exceptionWhileOpeningBrowser) {
                publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
            }
        });
    }

    public boolean handleActivityResult(int requestCode, int resultCode, android.content.Intent data) {
        if (requestCode != AUTH_REQUEST_CODE) return false;
        long operation = generation.incrementAndGet();
        if (resultCode != Activity.RESULT_OK || data == null) {
            publish(new ViewState(STATUS_SIGNED_OUT, null, false, false));
            return true;
        }
        AuthorizationResponse response = AuthorizationResponse.fromIntent(data);
        AuthorizationException authorizationError = AuthorizationException.fromIntent(data);
        if (authorizationError != null || response == null || response.request == null
                || !CLIENT_ID.equals(response.request.clientId) || !REDIRECT_URI.equals(response.request.redirectUri)
                || !isExpectedConfiguration(response.request.configuration)) {
            publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
            return true;
        }

        AuthState next = new AuthState(response, null);
        authState = next;
        publish(new ViewState(STATUS_LOADING, null, false, false));
        authorizationService.performTokenRequest(response.createTokenExchangeRequest(), (tokenResponse, tokenError) -> {
            next.update(tokenResponse, tokenError);
            if (closed || operation != generation.get()) return;
            if (tokenError != null || tokenResponse == null || tokenResponse.accessToken == null
                    || tokenResponse.refreshToken == null) {
                authState = null;
                publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
                return;
            }
            io.execute(() -> {
                if (!persistState(next, operation)) return;
                refreshAndReadAccount(operation);
            });
        });
        return true;
    }

    public void retryAccountCheck() {
        if (authState == null) return;
        long operation = generation.incrementAndGet();
        refreshAndReadAccount(operation);
    }

    private void refreshAndReadAccount(long operation) {
        AuthState current = authState;
        if (current == null || closed || operation != generation.get()) return;
        current.performActionWithFreshTokens(authorizationService, (accessToken, ignoredIdToken, exception) -> {
            if (closed || operation != generation.get()) return;
            if (exception != null || accessToken == null || accessToken.length() > 16384) {
                if (exception != null && exception.type == AuthorizationException.TYPE_OAUTH_TOKEN_ERROR
                        && "invalid_grant".equals(exception.error)) {
                    io.execute(() -> {
                        clearVaultQuietly();
                        if (operation == generation.get()) {
                            authState = null;
                            clearAccountContext();
                            publish(new ViewState(STATUS_SIGNED_OUT, null, false, false));
                        }
                    });
                } else publish(new ViewState(STATUS_IDENTITY_ERROR, null, false, false));
                return;
            }
            io.execute(() -> {
                if (!persistState(current, operation)) return;
                fetchProfileAndAccount(accessToken, operation);
            });
        });
    }

    private void fetchProfileAndAccount(String accessToken, long operation) {
        if (closed || operation != generation.get()) return;
        try {
            Uri userInfoUri = ISSUER.buildUpon().appendPath("protocol").appendPath("openid-connect")
                    .appendPath("userinfo").build();
            JSONObject profile = getJson(userInfoUri, accessToken, "auth.focusguard.totemmassmedia.com");
            if (closed || operation != generation.get()) return;
            String subject = boundedString(profile, "sub", 255);
            boolean verified = profile.optBoolean("email_verified", false);
            String email = safeDisplay(profile.optString("email", ""), 254);
            String displayName = safeDisplay(profile.optString("name", email), 120);
            String binding = accountBinding(subject);
            currentAccountBinding = binding;
            if (!verified) {
                currentAccountId = null; currentGenerationId = null; currentMembershipRevision = null;
                if (!closed && operation == generation.get()) publish(new ViewState(STATUS_EMAIL_UNVERIFIED, email, false, false));
                return;
            }

            Uri accountUri = API_ORIGIN.buildUpon().appendPath("v1").appendPath("bootstrap").appendPath("account").build();
            JSONObject account = getJson(accountUri, accessToken, "api.focusguard.totemmassmedia.com");
            if (closed || operation != generation.get()) return;
            boolean linked = account.getBoolean("deviceLinked");
            String accountId = linked ? boundedString(account, "accountId", 36) : null;
            String generationId = linked ? boundedString(account, "generationId", 36) : null;
            Long revision = linked && !account.isNull("membershipRevision") ? account.getLong("membershipRevision") : null;
            if (linked && (accountId == null || generationId == null || revision == null || revision < 1))
                throw new IOException("Account context invalid.");
            currentAccountId = accountId; currentGenerationId = generationId; currentMembershipRevision = revision;
            String status = linked ? STATUS_ACCOUNT_EXISTS : STATUS_FIRST_DEVICE;
            String displayEmail = email.isEmpty() ? displayName : email;
            String fingerprint = null;
            String pairingId = null;
            if (linked) {
                try {
                    AndroidPairingService.Result pairing = new AndroidPairingService(activity)
                            .refreshIfAvailable(binding, accountId, generationId, accessToken);
                    if (pairing != null) {
                        status = pairingStatus(pairing.state);
                        fingerprint = pairing.fingerprint;
                        pairingId = pairing.pairingId;
                    }
                } catch (Exception ignored) {
                    status = STATUS_PAIRING_ERROR;
                }
            }
            publish(new ViewState(status, displayEmail, true, linked, fingerprint, pairingId));
        } catch (Exception exception) {
            if (!closed && operation == generation.get())
                publish(new ViewState(STATUS_ACCOUNT_ERROR, null, false, false));
        }
    }

    public void requestDevicePairing() { runPairingOperation(false); }

    public void refreshDevicePairing() { runPairingOperation(true); }

    private void runPairingOperation(boolean refresh) {
        AuthState current = authState;
        String binding = currentAccountBinding;
        String accountId = currentAccountId;
        String generationId = currentGenerationId;
        ViewState previous = viewState;
        if (current == null || binding == null || accountId == null || generationId == null
                || !previous.accountExists) {
            publish(new ViewState(STATUS_PAIRING_ERROR, previous.email, previous.emailVerified,
                    previous.accountExists, previous.deviceFingerprint, previous.pairingId));
            return;
        }
        long operation = generation.incrementAndGet();
        publish(new ViewState(STATUS_PAIRING_LOADING, previous.email, previous.emailVerified,
                previous.accountExists, previous.deviceFingerprint, previous.pairingId));
        current.performActionWithFreshTokens(authorizationService, (accessToken, ignoredIdToken, exception) -> {
            if (closed || operation != generation.get()) return;
            if (exception != null || accessToken == null || accessToken.length() > 16384) {
                publish(new ViewState(STATUS_PAIRING_ERROR, previous.email, previous.emailVerified,
                        previous.accountExists, previous.deviceFingerprint, previous.pairingId));
                return;
            }
            io.execute(() -> {
                if (!persistState(current, operation)) return;
                try {
                    AndroidPairingService service = new AndroidPairingService(activity);
                    AndroidPairingService.Result result = refresh
                            ? service.refresh(binding, accountId, generationId, accessToken)
                            : service.requestOrRecover(binding, accountId, generationId, accessToken);
                    if (closed || operation != generation.get()) return;
                    String status = "not_found".equals(result.state) ? STATUS_ACCOUNT_EXISTS
                            : pairingStatus(result.state);
                    publish(new ViewState(status, previous.email, previous.emailVerified,
                            previous.accountExists, result.fingerprint, result.pairingId));
                } catch (Exception pairingFailure) {
                    if (!closed && operation == generation.get())
                        publish(new ViewState(STATUS_PAIRING_ERROR, previous.email, previous.emailVerified,
                                previous.accountExists, previous.deviceFingerprint, previous.pairingId));
                }
            });
        });
    }

    private static String pairingStatus(String status) {
        return switch (status) {
            case "pending" -> STATUS_PAIRING_PENDING;
            case "approved" -> STATUS_PAIRING_APPROVED;
            case "linked", "consumed" -> STATUS_PAIRING_LINKED;
            case "expired" -> STATUS_PAIRING_EXPIRED;
            default -> STATUS_PAIRING_ERROR;
        };
    }

    /** Clears the local vault before sending the best-effort Keycloak refresh-session logout. */
    public void signOut() {
        long operation = generation.incrementAndGet();
        AuthState previous = authState;
        ViewState previousView = viewState;
        String refreshToken = previous == null ? null : previous.getRefreshToken();
        authState = null;
        publish(new ViewState(STATUS_LOADING, null, false, false));
        io.execute(() -> {
            try { vault.clear(sessionBinding()); }
            catch (Exception exception) {
                if (!closed && operation == generation.get()) {
                    authState = previous;
                    publish(new ViewState(STATUS_LOCAL_CLEANUP_FAILED, previousView.email,
                            previousView.emailVerified, previousView.accountExists, previousView.deviceFingerprint));
                }
                return;
            }
            boolean remoteEnded = false;
            if (refreshToken != null) {
                try { remoteEnded = postLogout(refreshToken); }
                catch (Exception exception) { remoteEnded = false; }
            }
            if (!closed && operation == generation.get()) {
                clearAccountContext();
                publish(new ViewState(remoteEnded || refreshToken == null ? STATUS_LOCAL_SIGNOUT
                        : STATUS_REMOTE_SIGNOUT_PENDING, null, false, false));
            }
        });
    }

    private boolean persistState(AuthState state, long operation) {
        if (closed || operation != generation.get()) return false;
        byte[] bytes = null;
        try {
            bytes = state.jsonSerializeString().getBytes(StandardCharsets.UTF_8);
            vault.save(sessionBinding(), bytes);
            return true;
        } catch (Exception exception) {
            if (!closed && operation == generation.get())
                publish(new ViewState(STATUS_LOCAL_CLEANUP_FAILED, null, false, false));
            return false;
        } finally { if (bytes != null) Arrays.fill(bytes, (byte) 0); }
    }

    private boolean isExpectedClient(AuthState state) {
        if (state == null || !state.isAuthorized() || state.getRefreshToken() == null) return false;
        AuthorizationResponse last = state.getLastAuthorizationResponse();
        return last != null && last.request != null && CLIENT_ID.equals(last.request.clientId)
                && REDIRECT_URI.equals(last.request.redirectUri)
                && isExpectedConfiguration(state.getAuthorizationServiceConfiguration());
    }

    private static boolean isExpectedConfiguration(AuthorizationServiceConfiguration configuration) {
        if (configuration == null) return false;
        return isTrustedEndpoint(configuration.authorizationEndpoint, "auth.focusguard.totemmassmedia.com",
                    "/realms/focusguard/protocol/openid-connect/auth")
                && isTrustedEndpoint(configuration.tokenEndpoint, "auth.focusguard.totemmassmedia.com",
                    "/realms/focusguard/protocol/openid-connect/token");
    }

    private static boolean isTrustedEndpoint(Uri endpoint, String host, String expectedPath) {
        return endpoint != null && "https".equals(endpoint.getScheme()) && host.equals(endpoint.getHost())
                && endpoint.getPort() == -1 && expectedPath.equals(endpoint.getPath())
                && endpoint.getUserInfo() == null && endpoint.getQuery() == null && endpoint.getFragment() == null;
    }

    private static boolean isTrustedIssuer() {
        return "https".equals(ISSUER.getScheme()) && "auth.focusguard.totemmassmedia.com".equals(ISSUER.getHost())
                && "/realms/focusguard".equals(ISSUER.getPath()) && ISSUER.getPort() == -1
                && ISSUER.getUserInfo() == null && ISSUER.getQuery() == null && ISSUER.getFragment() == null;
    }

    private static boolean isTrustedApiOrigin() {
        return "https".equals(API_ORIGIN.getScheme()) && "api.focusguard.totemmassmedia.com".equals(API_ORIGIN.getHost())
                && (API_ORIGIN.getPath() == null || API_ORIGIN.getPath().isEmpty() || "/".equals(API_ORIGIN.getPath()))
                && API_ORIGIN.getPort() == -1 && API_ORIGIN.getUserInfo() == null
                && API_ORIGIN.getQuery() == null && API_ORIGIN.getFragment() == null;
    }

    private static JSONObject getJson(Uri uri, String accessToken, String expectedHost) throws IOException, JSONException {
        if (!"https".equals(uri.getScheme()) || !expectedHost.equals(uri.getHost()) || uri.getUserInfo() != null
                || uri.getFragment() != null || uri.getPort() != -1)
            throw new IOException("Native HTTPS destination rejected.");
        if (accessToken == null || accessToken.isEmpty() || accessToken.length() > 16384)
            throw new IOException("Access token rejected.");
        HttpURLConnection connection = (HttpURLConnection) new URL(uri.toString()).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestMethod("GET");
        connection.setRequestProperty("Authorization", "Bearer " + accessToken);
        connection.setRequestProperty("Accept", "application/json");
        try {
            if (connection.getResponseCode() != 200) throw new IOException("Remote account check rejected.");
            byte[] body = readBounded(connection.getInputStream());
            try { return new JSONObject(new String(body, StandardCharsets.UTF_8)); }
            finally { Arrays.fill(body, (byte) 0); }
        } finally { connection.disconnect(); }
    }

    private static boolean postLogout(String refreshToken) throws IOException {
        Uri logoutUri = ISSUER.buildUpon().appendPath("protocol").appendPath("openid-connect").appendPath("logout").build();
        if (!isTrustedEndpoint(logoutUri, "auth.focusguard.totemmassmedia.com", "/realms/focusguard/protocol/openid-connect/logout"))
            throw new IOException("Native logout destination rejected.");
        HttpURLConnection connection = (HttpURLConnection) new URL(logoutUri.toString()).openConnection();
        connection.setInstanceFollowRedirects(false);
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestMethod("POST");
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/x-www-form-urlencoded");
        byte[] body = ("client_id=" + encodeForm(CLIENT_ID) + "&refresh_token=" + encodeForm(refreshToken))
                .getBytes(StandardCharsets.UTF_8);
        try (OutputStream output = connection.getOutputStream()) { output.write(body); }
        finally { Arrays.fill(body, (byte) 0); }
        try {
            int response = connection.getResponseCode();
            return response == 200 || response == 204;
        } finally { connection.disconnect(); }
    }

    private static byte[] readBounded(InputStream input) throws IOException {
        try (InputStream stream = input; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] chunk = new byte[2048];
            int total = 0;
            try {
                int count;
                while ((count = stream.read(chunk)) != -1) {
                    total += count;
                    if (total > MAX_RESPONSE_BYTES) throw new IOException("Identity response too large.");
                    output.write(chunk, 0, count);
                }
                return output.toByteArray();
            } finally { Arrays.fill(chunk, (byte) 0); }
        }
    }

    private static String boundedString(JSONObject object, String key, int maxLength) throws JSONException, IOException {
        String value = object.getString(key);
        if (value.isEmpty() || value.length() > maxLength || value.chars().anyMatch(Character::isISOControl))
            throw new IOException("Identity profile field rejected.");
        return value;
    }

    private static String safeDisplay(String value, int maxLength) {
        if (value == null || value.length() > maxLength || value.chars().anyMatch(Character::isISOControl)) return "";
        return value;
    }

    private static String accountBinding(String subject) throws GeneralSecurityException {
        byte[] input = (BuildConfig.FOCUSGUARD_ISSUER + "\0" + subject).getBytes(StandardCharsets.UTF_8);
        byte[] digest = null;
        try {
            digest = MessageDigest.getInstance("SHA-256").digest(input);
            return Base64.getUrlEncoder().withoutPadding().encodeToString(digest);
        } finally {
            Arrays.fill(input, (byte) 0);
            if (digest != null) Arrays.fill(digest, (byte) 0);
        }
    }

    private static String sessionBinding() throws GeneralSecurityException {
        byte[] input = "FocusGuard.AndroidOidcSession.v1".getBytes(StandardCharsets.UTF_8);
        byte[] digest = null;
        try {
            digest = MessageDigest.getInstance("SHA-256").digest(input);
            return Base64.getUrlEncoder().withoutPadding().encodeToString(digest);
        } finally {
            Arrays.fill(input, (byte) 0);
            if (digest != null) Arrays.fill(digest, (byte) 0);
        }
    }

    private static String encodeForm(String value) throws IOException {
        return URLEncoder.encode(value, "UTF-8");
    }

    private void clearVaultQuietly() {
        try { vault.clear(sessionBinding()); } catch (Exception ignored) { }
    }

    private void clearAccountContext() {
        currentAccountBinding = null;
        currentAccountId = null;
        currentGenerationId = null;
        currentMembershipRevision = null;
    }

    private void publish(ViewState state) {
        viewState = state;
        main.post(() -> { if (!closed) listener.onState(state); });
    }

    @Override public void close() {
        closed = true;
        generation.incrementAndGet();
        if (authorizationService != null) authorizationService.dispose();
        io.shutdownNow();
    }

    public static final class ViewState {
        public final String statusCode;
        public final String email;
        public final boolean emailVerified;
        public final boolean accountExists;
        public final String deviceFingerprint;
        public final String pairingId;

        ViewState(String statusCode, String email, boolean emailVerified, boolean accountExists) {
            this(statusCode, email, emailVerified, accountExists, null);
        }

        ViewState(String statusCode, String email, boolean emailVerified, boolean accountExists,
                String deviceFingerprint) {
            this(statusCode, email, emailVerified, accountExists, deviceFingerprint, null);
        }

        ViewState(String statusCode, String email, boolean emailVerified, boolean accountExists,
                String deviceFingerprint, String pairingId) {
            this.statusCode = statusCode;
            this.email = email;
            this.emailVerified = emailVerified;
            this.accountExists = accountExists;
            this.deviceFingerprint = deviceFingerprint;
            this.pairingId = pairingId;
        }
    }

    public interface Listener { void onState(ViewState state); }
}
