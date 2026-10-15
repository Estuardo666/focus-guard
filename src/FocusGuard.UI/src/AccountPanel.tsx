import { useEffect, useState } from 'react';
import type { AccountClient, AccountSnapshot } from './native/account';
import type { SettingsSyncChoiceSource, SettingsSyncField } from './native/account';
import { CheckCircledIcon, LaptopIcon, LockClosedIcon, MixerHorizontalIcon, ReloadIcon } from '@radix-ui/react-icons';
import es from './locales/es.json';

type CopyKey = keyof typeof es;

export function AccountPanel({ client, snapshot, t }: {
  client: AccountClient;
  snapshot: AccountSnapshot;
  t(key: CopyKey): string;
}) {
  const account = snapshot.account;
  const conflictReview = account?.settingsSyncConflict ?? null;
  const conflictReviewKey = conflictReview ? `${conflictReview.revision}:${conflictReview.fields.join(',')}:${JSON.stringify(conflictReview.current)}:${JSON.stringify(conflictReview.thisDevice)}` : '';
  const [conflictChoices, setConflictChoices] = useState<Partial<Record<SettingsSyncField, SettingsSyncChoiceSource>>>({});
  const [resolvingConflict, setResolvingConflict] = useState(false);
  useEffect(() => { setConflictChoices({}); }, [conflictReviewKey]);
  const busy = snapshot.phase === 'loading' || !!account?.checkingSession || !!account?.signInPending
    || !!account?.signOutPending || !!account?.checkingDevice || !!account?.deviceLinkPending || !!account?.pairingBusy
    || !!account?.settingsTransferBusy || !!account?.settingsSyncBusy;
  const errorKey: Record<string, CopyKey> = {
    AccountConnectionUnavailable: 'accountConnectionUnavailable',
    AccountUnavailable: 'accountBridgeUnavailable',
    AccountPortalUnavailable: 'accountPortalUnavailable',
    RemoteLogoutUnconfirmed: 'accountLogoutUnconfirmed',
    RequestTimeout: 'accountRequestTimeout',
    BridgeUnavailable: 'accountBridgeUnavailable',
    SignInRequired: 'accountSignInRequired',
    AccountActionInProgress: 'accountActionInProgress',
    VerifiedEmailRequired: 'accountVerificationRequired',
    RecoveryKitNotConfirmed: 'accountRecoveryKitNotConfirmed',
    DevicePairingRequired: 'accountPairingRequired',
    DeviceMembershipUpdateRequired: 'accountMembershipUpdateRequired',
    DeviceVaultMismatch: 'accountDeviceVaultMismatch',
    DeviceConnectionUnavailable: 'accountDeviceConnectionUnavailable',
    DeviceCloudUnavailable: 'accountDeviceConnectionUnavailable',
    DeviceLinkUnavailable: 'accountDeviceConnectionUnavailable',
    DeviceLinkConflict: 'accountDeviceLinkConflict',
    DeviceChallengeRejected: 'accountDeviceLinkConflict',
    DeviceAccountUnavailable: 'accountDeviceConnectionUnavailable',
    DeviceRegistrationUnavailable: 'accountDeviceConnectionUnavailable',
    DeviceRegistrationRejected: 'accountDeviceLinkConflict',
    DeviceLinkFirstRequired: 'accountLinkFirstRequired',
    PairingRequestInvalid: 'accountPairingUnavailable',
    PairingRequestRequired: 'accountPairingUnavailable',
    PairingRecoveryUnavailable: 'accountPairingUnavailable',
    PairingExpired: 'accountPairingExpired',
    PairingUnavailable: 'accountPairingExpired',
    PairingApprovalNotConfirmed: 'accountPairingNotApproved',
    PairingFingerprintMismatch: 'accountPairingFingerprintMismatch',
    PairingFingerprintInvalid: 'accountPairingUnavailable',
    PairingMembershipInvalid: 'accountPairingUnavailable',
    PairingEnvelopeMissing: 'accountPairingUnavailable',
    DeviceLimitReached: 'accountDeviceLimitReached',
    PairingConfirmationRejected: 'accountPairingUnavailable',
    PairingApprovalUnavailable: 'accountPairingUnavailable',
    PairingApprovalRejected: 'accountPairingUnavailable',
    PairingStateUnavailable: 'accountPairingUnavailable',
    AccountSettingsPermissionRequired: 'accountSettingsPermissionRequired',
    AccountSettingsConflict: 'accountSettingsConflict',
    AccountSettingsResetUnavailable: 'accountSettingsUnavailable',
    AccountSettingsOverridesUnavailable: 'accountSettingsUnavailable',
    AccountSettingsDocumentInvalid: 'accountSettingsUnavailable',
    AccountSettingsSaveFailed: 'accountSettingsSaveFailed',
    AccountSettingsUnavailable: 'accountSettingsUnavailable',
    PortableSettingsInvalid: 'accountPortableSettingsInvalid',
    PortableSettingsStale: 'accountPortableSettingsStale',
    DeviceSessionRecoveryRequired: 'accountPairingUnavailable',
  };
  const conflictFieldLabels: Record<SettingsSyncField, CopyKey> = {
    theme: 'accountSettingsSyncTheme', language: 'accountSettingsSyncLanguage', duration: 'accountSettingsSyncDuration',
  };
  const conflictValue = (field: SettingsSyncField, values: NonNullable<typeof conflictReview>['current']) => field === 'theme'
    ? t(values.theme === 'light' ? 'accountSettingsSyncThemeLight' : 'accountSettingsSyncThemeDark')
    : field === 'language' ? values.uiLanguage.toUpperCase() : `${values.defaultDurationMinutes} ${t('accountSettingsSyncMinutes')}`;
  const canResolveConflict = !!conflictReview && !conflictReview.resetConflict
    && conflictReview.fields.every(field => conflictChoices[field] !== undefined);

  const initials = accountInitials(account?.displayName, account?.email);

  return <section className={`account-panel${account?.signedIn ? ' is-signed-in' : ''}`} aria-labelledby="account-panel-title">
    <header className="account-hero">
      <div className="account-hero-glow" aria-hidden="true" />
      <div className="account-panel-heading">
        <div>
          <p className="eyebrow">{t('accountEyebrow')}</p>
          <h2 id="account-panel-title">{t('accountTitle')}</h2>
        </div>
        <span className={`account-state-pill${account?.signedIn ? ' is-connected' : ''}`}>
          <span className="account-state-dot" aria-hidden="true" />
          {account?.signedIn ? t('accountSignedIn') : t('accountSignedOut')}
        </span>
      </div>
      {account?.signedIn ? <div className="account-identity">
        <span className="account-avatar" aria-hidden="true">{initials}</span>
        <div className="account-identity-text">
          <strong>{account.displayName || account.email || t('accountSignedIn')}</strong>
          {account.email && <span>{account.email}</span>}
        </div>
        <div className="account-actions account-hero-actions">
          {!account.deviceLinked && !account.accountHasLinkedDevice && <button type="button" className="account-primary" onClick={() => void client.linkDevice()}
            disabled={busy || !account.emailVerified}>{t('accountLinkDevice')}</button>}
          <button type="button" className="account-secondary" onClick={() => void client.openSecurity()} disabled={busy}>{t('accountSecurity')}</button>
          <button type="button" className="account-danger" onClick={() => void client.logout()} disabled={busy}>{t('accountLogout')}</button>
        </div>
      </div> : <p className="account-description">{t('accountDescription')}</p>}
      {account?.signedIn ? <dl className="account-facts">
        <div className={account.emailVerified ? 'is-ok' : 'is-pending'}><CheckCircledIcon aria-hidden="true" /><dt>{t('accountEmailStatus')}</dt><dd>{account.emailVerified ? t('accountVerified') : t('accountUnverified')}</dd></div>
        <div className={account.deviceLinked ? 'is-ok' : 'is-pending'}><LaptopIcon aria-hidden="true" /><dt>{t('accountDevice')}</dt><dd>{t(account.deviceLinked ? 'accountDeviceLinked' : 'accountDeviceNotLinked')}</dd></div>
        <div className="is-ok"><LockClosedIcon aria-hidden="true" /><dt>{t('accountTrustTitle')}</dt><dd>{t('accountTrustEncrypted')}</dd></div>
      </dl> : null}
    </header>
    <div className="account-notices">
    {snapshot.phase === 'loading' || account?.checkingSession
      ? <p className="account-notice" role="status">{t('accountChecking')}</p>
      : null}
    {account?.checkingDevice ? <p className="account-notice" role="status">{t('accountCheckingDevice')}</p> : null}
    {snapshot.phase === 'unavailable'
      ? <p className="account-notice" role="status">{t(errorKey[snapshot.error ?? ''] ?? 'accountBridgeUnavailable')}</p>
      : null}
    {account?.error
      ? <p className="account-notice" role={account.error === 'RemoteLogoutUnconfirmed' || account.error === 'DeviceLinkConflict' ? 'alert' : 'status'}>{t(errorKey[account.error] ?? 'accountConnectionUnavailable')}</p>
      : null}
    {account?.signOutPending ? <p className="account-notice" role="status">{t('accountSigningOut')}</p> : null}
    {account?.signInPending ? <p className="account-notice" role="status">{t('accountSigningIn')}</p> : null}
    {account?.deviceLinkPending ? <p className="account-notice" role="status">{t('accountLinkingDevice')}</p> : null}
    {account?.pairingBusy ? <p className="account-notice" role="status">{t('accountPairingWorking')}</p> : null}
    {account?.signedIn && !account.emailVerified && !account.deviceLinked
      ? <p className="account-notice" role="status">{t('accountVerificationRequired')}</p> : null}
    </div>

    <div className="account-sections">
      <div className="account-pairing" aria-labelledby="account-portable-settings-title">
        <div className="account-card-head"><span className="account-card-icon" aria-hidden="true"><MixerHorizontalIcon /></span><div>
          <h3 id="account-portable-settings-title">{t('accountPortableSettingsTitle')}</h3>
          <p>{t('accountPortableSettingsDescription')}</p></div></div>
        <div className="account-actions">
          <button type="button" className="account-secondary" onClick={() => void client.exportPortableSettings()} disabled={busy}>{t('accountPortableSettingsExport')}</button>
          <button type="button" className="account-secondary" onClick={() => void client.importPortableSettings()} disabled={busy}>{t('accountPortableSettingsImport')}</button>
        </div>
        {account?.settingsTransferBusy ? <p className="account-notice" role="status">{t('accountSettingsWorking')}</p> : null}
        {account?.settingsTransferStatus === 'PortableExported' ? <p className="account-pairing-empty" role="status">{t('accountPortableSettingsExported')}</p> : null}
        {account?.settingsTransferStatus === 'PortableImported' ? <p className="account-pairing-empty" role="status">{t('accountPortableSettingsImported')}</p> : null}
      </div>
    </div>

    {account?.signedIn ? <div className="account-sections">
      {account.accountHasLinkedDevice && !account.deviceLinked ? <div className="account-pairing" aria-labelledby="account-pairing-title">
        <h3 id="account-pairing-title">{t('accountPairingTitle')}</h3>
        <p>{t('accountPairingDescription')}</p>
        {account.pairingId && account.pairingFingerprint ? <div className="account-pairing-code">
          <span>{t('accountPairingCode')}</span>
          <code>{account.pairingId}</code>
          <span>{t('accountPairingFingerprint')}</span>
          <code>{account.pairingFingerprint}</code>
          <small>{t('accountPairingCompare')}</small>
          <button type="button" className="account-secondary" onClick={() => void client.completePairing()} disabled={busy}>
            {t('accountPairingCheck')}</button>
        </div> : <button type="button" className="account-primary" onClick={() => void client.requestPairing()}
          disabled={busy || !account.emailVerified}>{t('accountPairingRequest')}</button>}
      </div> : null}
      {account.deviceLinked ? <div className="account-pairing" aria-labelledby="account-pairing-requests-title">
        <div className="account-card-head"><span className="account-card-icon" aria-hidden="true"><LaptopIcon /></span><div>
          <h3 id="account-pairing-requests-title">{t('accountPairingRequests')}</h3>
          <p>{t('accountPairingApprovalDescription')}</p></div>
          <button type="button" className="account-secondary account-refresh" onClick={() => void client.refreshPairings()} disabled={busy}>
            <ReloadIcon aria-hidden="true" />{t('accountPairingRefresh')}</button></div>
        {account.pendingPairings.length > 0 ? <ul className="account-pairing-list">{account.pendingPairings.map(pairing => <li key={pairing.pairingId}>
          <div><strong>{pairing.platform} · {pairing.clientVersion}</strong><span>{t('accountPairingCode')}: <code>{pairing.pairingId}</code></span>
            <span>{t('accountPairingFingerprint')}: <code>{pairing.fingerprint}</code></span>
            <small>{t('accountPairingExpires')}: {new Date(pairing.expiresAtUtc).toLocaleString()}</small></div>
          <button type="button" className="account-primary" onClick={() => void client.approvePairing(pairing.pairingId, pairing.fingerprint)} disabled={busy}>
            {t('accountPairingApprove')}</button>
        </li>)}</ul> : <p className="account-pairing-empty">{t('accountPairingNone')}</p>}
      </div> : null}
      {account.deviceLinked ? <div className="account-pairing" aria-labelledby="account-settings-transfer-title">
        <div className="account-card-head"><span className="account-card-icon" aria-hidden="true"><MixerHorizontalIcon /></span><div>
          <h3 id="account-settings-transfer-title">{t('accountSettingsTransferTitle')}</h3>
          <p>{t('accountSettingsTransferDescription')}</p></div></div>
        <div className="account-actions">
          <button type="button" className="account-secondary" onClick={() => void client.shareSettings()} disabled={busy}>
            {t('accountSettingsShare')}</button>
          <button type="button" className="account-secondary" onClick={() => void client.importSettings()} disabled={busy}>
            {t('accountSettingsImport')}</button>
        </div>
        {account.settingsTransferBusy ? <p className="account-notice" role="status">{t('accountSettingsWorking')}</p> : null}
        {account.settingsTransferStatus === 'Shared' ? <p className="account-pairing-empty" role="status">{t('accountSettingsShared')}</p> : null}
        {account.settingsTransferStatus === 'Imported' ? <p className="account-pairing-empty" role="status">{t('accountSettingsImported')}</p> : null}
        {account.settingsTransferStatus === 'NoSharedSettings' ? <p className="account-pairing-empty" role="status">{t('accountSettingsNone')}</p> : null}
      </div> : null}
      {account.deviceLinked ? <div className="account-pairing" aria-labelledby="account-settings-sync-title">
        <div className="account-card-head"><span className="account-card-icon" aria-hidden="true"><ReloadIcon /></span><div>
          <h3 id="account-settings-sync-title">{t('accountSettingsSyncTitle')}</h3>
          <p>{t('accountSettingsSyncDescription')}</p></div></div>
        <div className="account-actions">
          {account.settingsSyncEnabled
            ? <>
              <button type="button" className="account-danger" onClick={() => void client.disableSettingsSync()} disabled={busy}>{t('accountSettingsSyncDisable')}</button>
              {account.settingsSyncStatus === 'Enabled' ? <button type="button" className="account-danger"
                onClick={() => void client.resetSettingsSync()} disabled={busy}>{t('accountSettingsSyncReset')}</button> : null}
            </>
            : <button type="button" className="account-primary" onClick={() => void client.enableSettingsSync()} disabled={busy}>{t('accountSettingsSyncEnable')}</button>}
        </div>
        {account.settingsSyncEnabled && account.settingsSyncStatus === 'Enabled' && account.settingsSyncLocalOverrides
          ? <div className="account-settings-conflict-review" role="group" aria-labelledby="account-settings-overrides-title">
            <p id="account-settings-overrides-title">{t('accountSettingsSyncOverridesTitle')}</p>
            <p>{t('accountSettingsSyncOverridesDescription')}</p>
            <dl>
              <div><dt>{t('accountSettingsSyncTheme')}</dt><dd>{t(account.settingsSyncLocalOverrides.theme
                ? 'accountSettingsSyncOverrideLocal' : 'accountSettingsSyncOverrideFollow')}</dd></div>
              <div><dt>{t('accountSettingsSyncLanguage')}</dt><dd>{t(account.settingsSyncLocalOverrides.language
                ? 'accountSettingsSyncOverrideLocal' : 'accountSettingsSyncOverrideFollow')}</dd></div>
              <div><dt>{t('accountSettingsSyncDuration')}</dt><dd>{t(account.settingsSyncLocalOverrides.duration
                ? 'accountSettingsSyncOverrideLocal' : 'accountSettingsSyncOverrideFollow')}</dd></div>
            </dl>
            <button type="button" className="account-secondary" onClick={() => void client.updateLocalSettingsOverrides()}
              disabled={busy}>{t('accountSettingsSyncOverridesAction')}</button>
          </div> : null}
        {account.settingsSyncBusy ? <p className="account-notice" role="status">{t('accountSettingsSyncWorking')}</p> : null}
        {account.settingsSyncStatus === 'Enabled' ? <p className="account-pairing-empty" role="status">{t('accountSettingsSyncEnabled')}</p> : null}
        {account.settingsSyncStatus === 'Disabled' ? <p className="account-pairing-empty" role="status">{t('accountSettingsSyncDisabled')}</p> : null}
        {account.settingsSyncStatus === 'Pending' ? <p className="account-notice" role="status">{t('accountSettingsSyncPending')}</p> : null}
        {account.settingsSyncStatus === 'Conflict' && !conflictReview
          ? <p className="account-notice" role="status">{t('accountSettingsSyncConflict')}</p> : null}
        {account.settingsSyncStatus === 'Conflict' && conflictReview && !conflictReview.resetConflict ? <div className="account-settings-conflict-review" aria-labelledby="account-settings-sync-conflict-title">
          <p id="account-settings-sync-conflict-title" role="status">{t('accountSettingsSyncConflictReview')}</p>
          {conflictReview.fields.map(field => <fieldset className="account-settings-conflict-field" key={field}>
            <legend>{t(conflictFieldLabels[field])}</legend>
            <label>
              <input type="radio" name={`settings-conflict-${field}`} checked={conflictChoices[field] === 'thisDevice'}
                onChange={() => setConflictChoices(current => ({ ...current, [field]: 'thisDevice' }))} />
              <span>{t('accountSettingsSyncThisDevice')}</span><strong>{conflictValue(field, conflictReview.thisDevice)}</strong>
            </label>
            <label>
              <input type="radio" name={`settings-conflict-${field}`} checked={conflictChoices[field] === 'otherDevices'}
                onChange={() => setConflictChoices(current => ({ ...current, [field]: 'otherDevices' }))} />
              <span>{t('accountSettingsSyncOtherDevices')}</span><strong>{conflictValue(field, conflictReview.current)}</strong>
            </label>
          </fieldset>)}
          <button type="button" className="account-primary" disabled={busy || resolvingConflict || !canResolveConflict}
            onClick={() => {
              if (!conflictReview || !canResolveConflict) return;
              setResolvingConflict(true);
              void client.resolveSettingsSyncConflict(conflictReview.revision, conflictReview.fields.map(field => ({
                field, source: conflictChoices[field]!,
              }))).finally(() => setResolvingConflict(false));
            }}>{t(resolvingConflict || account.settingsSyncBusy ? 'accountSettingsSyncWorking' : 'accountSettingsSyncResolve')}</button>
          {resolvingConflict ? <p className="account-notice" role="status">{t('accountSettingsSyncWorking')}</p> : null}
        </div> : null}
        {account.settingsSyncStatus === 'Conflict' && conflictReview?.resetConflict ? <div className="account-settings-conflict-review" aria-labelledby="account-settings-sync-reset-conflict-title">
          <p id="account-settings-sync-reset-conflict-title" className="account-notice" role="alert">{t('accountSettingsSyncResetConflict')}</p>
          {conflictReview.fields.map(field => <fieldset className="account-settings-conflict-field" key={field}>
            <legend>{t(conflictFieldLabels[field])}</legend>
            <p><span>{t('accountSettingsSyncOtherDevices')}</span><strong>{conflictValue(field, conflictReview.current)}</strong></p>
            <p><span>{t('accountSettingsSyncThisDevice')}</span><strong>{conflictValue(field, conflictReview.thisDevice)}</strong></p>
          </fieldset>)}
          <p className="account-settings-reset-boundary">{t('accountSettingsSyncResetBoundary')}</p>
          <div className="account-actions">
            <button type="button" className="account-danger" disabled={busy || resolvingConflict}
              onClick={() => {
                setResolvingConflict(true);
                void client.resolveSettingsSyncResetConflict(conflictReview.revision, 'thisDeviceReset')
                  .finally(() => setResolvingConflict(false));
              }}>{t('accountSettingsSyncKeepReset')}</button>
            <button type="button" className="account-primary" disabled={busy || resolvingConflict}
              onClick={() => {
                setResolvingConflict(true);
                void client.resolveSettingsSyncResetConflict(conflictReview.revision, 'otherDevices')
                  .finally(() => setResolvingConflict(false));
              }}>{t('accountSettingsSyncKeepOther')}</button>
          </div>
          {resolvingConflict ? <p className="account-notice" role="status">{t('accountSettingsSyncWorking')}</p> : null}
        </div> : null}
        {account.settingsSyncStatus === 'Attention' ? <div className="account-settings-recovery" role="group" aria-label={t('accountSettingsSyncTitle')}>
          <p className="account-notice" role="alert">{t('accountSettingsSyncAttention')}</p>
          {account.settingsSyncRecoveryAvailable ? <button type="button" className="account-secondary" disabled={busy}
            onClick={() => void client.recoverSettingsSync()}>{t('accountSettingsSyncRecover')}</button> : null}
        </div> : null}
        {account.settingsSyncStatus === 'Cancelled' ? <p className="account-pairing-empty" role="status">{t('accountSettingsSyncCancelled')}</p> : null}
      </div> : null}
    </div> : <>
      <p className="account-local-note">{t('accountLocalContinues')}</p>
      <div className="account-actions">
        <button type="button" className="account-primary" onClick={() => void client.login()}
          disabled={busy || snapshot.phase === 'unavailable' || account?.error === 'AccountUnavailable'}>
          {account?.signInPending ? t('accountSigningIn') : t('accountSignIn')}
        </button>
        {account?.signInPending && <button type="button" className="account-secondary" onClick={() => void client.cancelLogin()}>{t('accountCancel')}</button>}
      </div>
    </>}
    <p className="account-boundary">{t('accountBoundary')}</p>
  </section>;
}

export function accountInitials(displayName?: string | null, email?: string | null) {
  return (displayName || email || '?').split(/[\s@.]+/).filter(Boolean).slice(0, 2).map(part => part[0]!.toUpperCase()).join('');
}

export function AccountChip({ snapshot, onOpen, t }: {
  snapshot: AccountSnapshot;
  onOpen(): void;
  t(key: CopyKey): string;
}) {
  const account = snapshot.account;
  if (!account?.signedIn) return null;
  const name = account.displayName || account.email || t('accountSignedIn');
  return <button type="button" className="account-chip" onClick={onOpen} aria-label={`${t('accountTitle')}: ${name}`}>
    <span className="account-chip-avatar" aria-hidden="true">{accountInitials(account.displayName, account.email)}
      <span className={`account-chip-presence${account.deviceLinked ? ' is-linked' : ''}`} /></span>
    <span className="account-chip-text"><strong>{name}</strong>{account.email && account.displayName ? <small>{account.email}</small> : null}</span>
  </button>;
}
