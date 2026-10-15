package app.focusguard.phase0;

import android.app.Activity;
import android.app.AppOpsManager;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.View;
import android.view.WindowInsets;
import android.widget.ScrollView;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/** Capability inspection only: no network, enforcement, permission automation or persistence. */
public final class ProbeActivity extends Activity {
    private TextView status;

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        int padding = Math.round(20 * getResources().getDisplayMetrics().density);
        layout.setPadding(padding, padding, padding, padding);
        // Target SDK 35 enables edge-to-edge; keep diagnostics outside system bars.
        layout.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                view.setPadding(padding + bars.left, padding + bars.top, padding + bars.right, padding + bars.bottom);
            } else {
                applyLegacyInsets(view, insets, padding);
            }
            return insets;
        });
        TextView title = new TextView(this);
        title.setText("FocusGuard · ensayo de capacidades");
        title.setTextSize(23);
        layout.addView(title);
        TextView notice = new TextView(this);
        notice.setText("No bloquea aplicaciones ni sitios. No conecta a una cuenta ni envía datos. Esta pantalla sirve para registrar capacidades reales antes de diseñar el bloqueo Android.");
        notice.setTextSize(17);
        layout.addView(notice);
        status = new TextView(this);
        status.setTextSize(17);
        status.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        layout.addView(status);
        Button refresh = new Button(this);
        refresh.setText("Comprobar capacidades");
        refresh.setOnClickListener(view -> refreshCapabilities());
        layout.addView(refresh);
        TextView crypto = new TextView(this);
        crypto.setTextSize(14);
        try { crypto.setText(CryptoRuntimeProbe.run()); }
        catch (RuntimeException | LinkageError ex) { crypto.setText("CRYPTO_PROBE_FAILED: " + ex.getClass().getSimpleName() + ": " + ex.getMessage()); }
        layout.addView(crypto);
        TextView vault = new TextView(this);
        vault.setTextSize(14);
        vault.setText(AndroidVaultRuntimeProbe.run(this));
        layout.addView(vault);
        TextView identity = new TextView(this);
        identity.setTextSize(14);
        identity.setText(AndroidIdentityRuntimeProbe.run(this));
        layout.addView(identity);
        TextView e2ee = new TextView(this);
        e2ee.setTextSize(14);
        e2ee.setText(AndroidE2eeRuntimeProbe.run(this));
        layout.addView(e2ee);
        TextView keyEnvelope = new TextView(this);
        keyEnvelope.setTextSize(14);
        keyEnvelope.setText(AndroidKeyEnvelopeRuntimeProbe.run(this));
        layout.addView(keyEnvelope);
        TextView membership = new TextView(this);
        membership.setTextSize(14);
        membership.setText(AndroidMembershipRuntimeProbe.run(this));
        layout.addView(membership);
        ScrollView scroll = new ScrollView(this);
        scroll.addView(layout);
        setContentView(scroll);
        layout.requestApplyInsets();
        refreshCapabilities();
    }

    @SuppressWarnings("deprecation")
    private void applyLegacyInsets(View view, WindowInsets insets, int padding) {
        view.setPadding(padding + insets.getSystemWindowInsetLeft(), padding + insets.getSystemWindowInsetTop(),
                padding + insets.getSystemWindowInsetRight(), padding + insets.getSystemWindowInsetBottom());
    }

    private void refreshCapabilities() {
        AppOpsManager ops = (AppOpsManager) getSystemService(APP_OPS_SERVICE);
        boolean usage = hasUsagePermission(ops);
        String result = "Android API: " + Build.VERSION.SDK_INT
                + "\nModelo (solo local): " + Build.MODEL
                + "\nAcceso de uso: " + (usage ? "concedido" : "no concedido")
                + "\nOverlay: " + (Settings.canDrawOverlays(this) ? "concedido" : "no concedido")
                + "\nEnforcement normal: NO VALIDADO"
                + "\nEnforcement estricto: NO VALIDADO"
                + "\nLa presencia de un permiso no prueba bloqueo ni resistencia a force-stop.";
        status.setText(result);
    }

    private boolean hasUsagePermission(AppOpsManager ops) {
        if (ops == null) return false;
        int mode = Build.VERSION.SDK_INT >= 29
                ? ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, android.os.Process.myUid(), getPackageName())
                : legacyUsageMode(ops);
        return mode == AppOpsManager.MODE_ALLOWED;
    }

    // The legacy API remains necessary for the declared Android 26-28 baseline.
    @SuppressWarnings("deprecation")
    private int legacyUsageMode(AppOpsManager ops) {
        return ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, android.os.Process.myUid(), getPackageName());
    }
}
