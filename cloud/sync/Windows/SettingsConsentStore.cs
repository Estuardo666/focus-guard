namespace FocusGuard.Sync;

/// <summary>Consent is local, DPAPI/scoped, off by default and independent from login or device membership.</summary>
public sealed class SettingsConsentStore(SettingsOutboxVault vault)
{
    public async Task<bool> IsEnabledAsync(CancellationToken token = default) =>
        (await vault.LoadAsync(token))?.SyncConsentEnabled == true;

    public Task<SettingsOutbox> SetAsync(bool enabled, bool ownerConfirmed, CancellationToken token = default)
    {
        if (enabled && !ownerConfirmed) throw new InvalidOperationException("Enabling synchronization requires deliberate local consent.");
        return vault.UpdateAsync(current => (current ?? throw new InvalidOperationException("Initialize the account baseline explicitly."))
            with
        { SyncConsentEnabled = enabled }, token);
    }
}
