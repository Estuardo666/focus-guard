package com.totemmassmedia.focusguard;

import android.app.Activity;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/** Staging mobile account screen. It never receives or displays OAuth tokens. */
public final class MainActivity extends Activity implements AndroidOidcAccountSession.Listener {
    private AndroidOidcAccountSession session;
    private TextView email;
    private TextView accountState;
    private TextView status;
    private TextView fingerprint;
    private TextView pairingCode;
    private Button connect;
    private Button signOut;
    private Button retry;
    private Button pair;
    private Button refreshPairing;

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        int pad = Math.round(24 * getResources().getDisplayMetrics().density);
        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(pad, pad, pad, pad);

        TextView title = new TextView(this);
        title.setText(R.string.screen_title);
        title.setTextSize(25);
        content.addView(title);

        TextView description = new TextView(this);
        description.setText(R.string.screen_description);
        description.setTextSize(16);
        content.addView(description);

        email = new TextView(this);
        email.setTextSize(17);
        content.addView(email);

        accountState = new TextView(this);
        accountState.setTextSize(16);
        content.addView(accountState);

        status = new TextView(this);
        status.setTextSize(15);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        content.addView(status);

        fingerprint = new TextView(this);
        fingerprint.setTextSize(14);
        content.addView(fingerprint);

        pairingCode = new TextView(this);
        pairingCode.setTextSize(14);
        content.addView(pairingCode);

        connect = new Button(this);
        connect.setText(R.string.connect_account);
        connect.setOnClickListener(view -> session.signIn());
        content.addView(connect);

        retry = new Button(this);
        retry.setText(R.string.retry);
        retry.setOnClickListener(view -> session.retryAccountCheck());
        content.addView(retry);

        pair = new Button(this);
        pair.setText(R.string.request_pairing);
        pair.setOnClickListener(view -> session.requestDevicePairing());
        content.addView(pair);

        refreshPairing = new Button(this);
        refreshPairing.setText(R.string.refresh_pairing);
        refreshPairing.setOnClickListener(view -> session.refreshDevicePairing());
        content.addView(refreshPairing);

        signOut = new Button(this);
        signOut.setText(R.string.sign_out);
        signOut.setOnClickListener(view -> session.signOut());
        content.addView(signOut);

        TextView privacy = new TextView(this);
        privacy.setText(R.string.security_note);
        privacy.setTextSize(14);
        content.addView(privacy);

        ScrollView scroll = new ScrollView(this);
        scroll.addView(content);
        setContentView(scroll);

        session = new AndroidOidcAccountSession(this, this);
        render(session.currentState());
        session.restore();
    }

    @SuppressWarnings("deprecation")
    @Override protected void onActivityResult(int requestCode, int resultCode, android.content.Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (session != null) session.handleActivityResult(requestCode, resultCode, data);
    }

    @Override public void onState(AndroidOidcAccountSession.ViewState state) { render(state); }

    private void render(AndroidOidcAccountSession.ViewState state) {
        runOnUiThread(() -> {
            status.setText(statusResource(state.statusCode));
            boolean signedIn = state.emailVerified || "email_unverified".equals(state.statusCode)
                    || "account_error".equals(state.statusCode) || "local_cleanup_failed".equals(state.statusCode)
                    || "account_exists".equals(state.statusCode) || "first_device".equals(state.statusCode)
                    || state.statusCode.startsWith("pairing_");
            email.setText(signedIn && state.email != null ? getString(R.string.profile_email, state.email) : "");
            String summary = accountText(state.statusCode);
            accountState.setText(summary);
            accountState.setVisibility(summary.isEmpty() || summary.contentEquals(status.getText())
                    ? View.GONE : View.VISIBLE);
            boolean busy = "loading".equals(state.statusCode) || "pairing_loading".equals(state.statusCode);
            connect.setVisibility(signedIn ? View.GONE : View.VISIBLE);
            connect.setEnabled(!busy);
            retry.setVisibility("account_error".equals(state.statusCode) ? View.VISIBLE : View.GONE);
            retry.setEnabled(!busy);
            signOut.setVisibility(signedIn ? View.VISIBLE : View.GONE);
            signOut.setEnabled(!busy);
            boolean pairingInProgress = "pairing_pending".equals(state.statusCode)
                    || "pairing_approved".equals(state.statusCode)
                    || "pairing_linked".equals(state.statusCode);
            pair.setVisibility(state.accountExists && !pairingInProgress ? View.VISIBLE : View.GONE);
            pair.setEnabled(!busy);
            refreshPairing.setVisibility(pairingInProgress ? View.VISIBLE : View.GONE);
            refreshPairing.setText("pairing_linked".equals(state.statusCode)
                    ? R.string.refresh_linked : R.string.refresh_pairing);
            refreshPairing.setEnabled(!busy);
            fingerprint.setText(state.deviceFingerprint == null ? ""
                    : getString(R.string.device_fingerprint, formatFingerprint(state.deviceFingerprint)));
            boolean showPairingCode = state.pairingId != null &&
                    ("pairing_pending".equals(state.statusCode) || "pairing_approved".equals(state.statusCode));
            pairingCode.setText(showPairingCode ? getString(R.string.pairing_request_code, state.pairingId) : "");
        });
    }

    private String formatFingerprint(String value) {
        StringBuilder formatted = new StringBuilder(value.length() + value.length() / 4);
        for (int index = 0; index < value.length(); index++) {
            if (index > 0 && index % 4 == 0) formatted.append(' ');
            formatted.append(value.charAt(index));
        }
        return formatted.toString();
    }

    static int statusResource(String code) {
        switch (code) {
            case "account_exists":
            case "first_device": return R.string.status_account_connected;
            case "loading": return R.string.status_loading;
            case "identity_error": return R.string.status_identity_unavailable;
            case "account_error": return R.string.status_account_unavailable;
            case "pairing_loading": return R.string.status_pairing_loading;
            case "pairing_pending": return R.string.status_pairing_pending;
            case "pairing_approved": return R.string.status_pairing_approved;
            case "pairing_linked": return R.string.status_pairing_linked;
            case "pairing_expired": return R.string.status_pairing_expired;
            case "pairing_error": return R.string.status_pairing_error;
            case "email_unverified": return R.string.email_unverified;
            case "local_signout": return R.string.status_signed_out_local;
            case "remote_signout_pending": return R.string.status_signed_out_remote_pending;
            case "local_cleanup_failed": return R.string.status_cleanup_failed;
            default: return R.string.status_signed_out;
        }
    }

    private String accountText(String code) {
        if ("account_exists".equals(code)) return getString(R.string.status_account_exists);
        if ("pairing_linked".equals(code)) return getString(R.string.status_pairing_linked);
        if (code.startsWith("pairing_")) return getString(R.string.status_pairing_device);
        if ("first_device".equals(code)) return getString(R.string.status_first_device);
        if ("email_unverified".equals(code)) return getString(R.string.email_unverified);
        return "";
    }

    @Override protected void onDestroy() {
        if (session != null) session.close();
        super.onDestroy();
    }
}
