import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountChip, AccountPanel, accountInitials } from './AccountPanel';
import type { AccountClient, AccountSnapshot } from './native/account';
import es from './locales/es.json';

const t = (key: keyof typeof es) => es[key];
const base: AccountSnapshot = { phase: 'ready', account: null, error: null };
let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

function client() {
  return {
    login: vi.fn(async () => base),
    cancelLogin: vi.fn(async () => base),
    logout: vi.fn(async () => base),
    openSecurity: vi.fn(async () => base),
    linkDevice: vi.fn(async () => base),
    requestPairing: vi.fn(async () => base),
    refreshPairings: vi.fn(async () => base),
    completePairing: vi.fn(async () => base),
    approvePairing: vi.fn(async () => base),
    shareSettings: vi.fn(async () => base),
    importSettings: vi.fn(async () => base),
    exportPortableSettings: vi.fn(async () => base),
    importPortableSettings: vi.fn(async () => base),
    enableSettingsSync: vi.fn(async () => base),
    disableSettingsSync: vi.fn(async () => base),
    updateLocalSettingsOverrides: vi.fn(async () => base),
    resetSettingsSync: vi.fn(async () => base),
    resolveSettingsSyncConflict: vi.fn(async () => base),
    resolveSettingsSyncResetConflict: vi.fn(async () => base),
    recoverSettingsSync: vi.fn(async () => base),
  } as unknown as AccountClient;
}

describe('native account panel', () => {
  it('offers portable settings locally without requiring a signed-in account', () => {
    const account = client();
    act(() => root.render(<AccountPanel client={account} snapshot={base} t={t}/>));
    const exportButton = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountPortableSettingsExport)!;
    const importButton = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountPortableSettingsImport)!;
    act(() => exportButton.click());
    act(() => importButton.click());
    expect(account.exportPortableSettings).toHaveBeenCalledOnce();
    expect(account.importPortableSettings).toHaveBeenCalledOnce();
  });

  it('keeps local use explicit and starts secure account login only on request', () => {
    const account = client();
    act(() => root.render(<AccountPanel client={account} snapshot={base} t={t}/>));
    expect(container.textContent).toContain(es.accountLocalContinues);
    expect(container.textContent).toContain(es.accountBoundary);
    const signIn = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSignIn)!;
    act(() => signIn.click());
    expect(account.login).toHaveBeenCalledOnce();
  });

  it('shows verification and unlinked-device status without implying synchronization', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: false,
      deviceLinked: false, accountHasLinkedDevice: false, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: null, pairingFingerprint: null, pendingPairings: [], error: null, remoteRevoked: null,
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain('user@example.invalid');
    expect(container.textContent).toContain(es.accountUnverified);
    expect(container.textContent).toContain(es.accountVerificationRequired);
    expect(container.textContent).toContain(es.accountDeviceNotLinked);
    const buttons = [...container.querySelectorAll('button')];
    expect(buttons.find(button => button.textContent === es.accountLinkDevice)?.disabled).toBe(true);
    act(() => buttons.find(button => button.textContent === es.accountSecurity)!.click());
    act(() => buttons.find(button => button.textContent === es.accountLogout)!.click());
    expect(accountClient.openSecurity).toHaveBeenCalledOnce();
    expect(accountClient.logout).toHaveBeenCalledOnce();
  });

  it('starts device linking only after verified email and explicit consent', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: false, accountHasLinkedDevice: false, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: null, pairingFingerprint: null, pendingPairings: [], error: null, remoteRevoked: null,
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    const button = [...container.querySelectorAll('button')].find(item => item.textContent === es.accountLinkDevice)!;
    expect(button.disabled).toBe(false);
    act(() => button.click());
    expect(accountClient.linkDevice).toHaveBeenCalledOnce();
  });

  it('shows the request fingerprint and approval check on a new device', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: false, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: 'f8d742d5-2c86-42f2-9657-66a7fbddfc6e', pairingFingerprint: 'A'.repeat(43), pendingPairings: [],
      error: null, remoteRevoked: null,
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain('f8d742d5-2c86-42f2-9657-66a7fbddfc6e');
    expect(container.textContent).toContain('A'.repeat(43));
    expect(container.textContent).toContain(es.accountPairingCompare);
    const check = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountPairingCheck)!;
    act(() => check.click());
    expect(accountClient.completePairing).toHaveBeenCalledOnce();
    expect([...container.querySelectorAll('button')].some(button => button.textContent === es.accountLinkDevice)).toBe(false);
  });

  it('starts pairing instead of first-device bootstrap when the account already has a linked device', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: false, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: null, pairingFingerprint: null, pendingPairings: [], error: 'DevicePairingRequired', remoteRevoked: null,
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    const request = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountPairingRequest)!;
    act(() => request.click());
    expect(accountClient.requestPairing).toHaveBeenCalledOnce();
    expect(accountClient.linkDevice).not.toHaveBeenCalled();
  });

  it('lists only sanitized pairing details and delegates approval to native confirmation', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: null, pairingFingerprint: null, error: null, remoteRevoked: null,
      pendingPairings: [{ pairingId: 'f8d742d5-2c86-42f2-9657-66a7fbddfc6e', deviceId: '51a42b79-52b0-4ccb-9dca-b99c49da2f10',
        platform: 'Windows', clientVersion: '0.1.152', fingerprint: 'B'.repeat(43), expiresAtUtc: '2026-10-12T23:00:00Z' }],
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain('Windows · 0.1.152');
    expect(container.textContent).toContain('f8d742d5-2c86-42f2-9657-66a7fbddfc6e');
    expect(container.textContent).toContain('B'.repeat(43));
    const approve = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountPairingApprove)!;
    act(() => approve.click());
    expect(accountClient.approvePairing).toHaveBeenCalledWith('f8d742d5-2c86-42f2-9657-66a7fbddfc6e', 'B'.repeat(43));
  });

  it('offers only the consented one-time settings transfer on a linked device', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: 'Shared', settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: null,
      pairingId: null, pairingFingerprint: null,
      pendingPairings: [], error: null, remoteRevoked: null,
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsTransferDescription);
    expect(container.textContent).toContain(es.accountSettingsShared);
    act(() => [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsShare)!.click());
    act(() => [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsImport)!.click());
    expect(accountClient.shareSettings).toHaveBeenCalledOnce();
    expect(accountClient.importSettings).toHaveBeenCalledOnce();
  });

  it('offers explicit automatic sync consent controls only on a linked device', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false,
      settingsSyncEnabled: false, settingsSyncStatus: 'Disabled', pairingId: null, pairingFingerprint: null,
      pendingPairings: [], error: null, remoteRevoked: null,
      settingsSyncLocalOverrides: { theme: false, language: false, duration: false },
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncDescription);
    const enable = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncEnable)!;
    act(() => enable.click());
    expect(accountClient.enableSettingsSync).toHaveBeenCalledOnce();

    act(() => root.render(<AccountPanel client={accountClient} snapshot={{ ...snapshot,
      account: { ...snapshot.account!, settingsSyncBusy: true, settingsSyncStatus: 'Enabling' } }} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncWorking);
    expect([...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncEnable)!.disabled).toBe(true);

    act(() => root.render(<AccountPanel client={accountClient} snapshot={{ ...snapshot,
      account: { ...snapshot.account!, settingsSyncBusy: false, settingsSyncStatus: 'Cancelled' } }} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncCancelled);

    act(() => root.render(<AccountPanel client={accountClient} snapshot={{ ...snapshot,
      account: { ...snapshot.account!, settingsSyncEnabled: true, settingsSyncStatus: 'Pending' } }} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncPending);

    act(() => root.render(<AccountPanel client={accountClient} snapshot={{ ...snapshot,
      account: { ...snapshot.account!, settingsSyncEnabled: true, settingsSyncStatus: 'Enabled' } }} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncEnabled);
    expect(container.textContent).toContain(es.accountSettingsSyncOverrideFollow);
    const localSettings = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncOverridesAction)!;
    act(() => localSettings.click());
    expect(accountClient.updateLocalSettingsOverrides).toHaveBeenCalledOnce();
    const reset = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncReset)!;
    act(() => reset.click());
    expect(accountClient.resetSettingsSync).toHaveBeenCalledOnce();
    act(() => [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncDisable)!.click());
    expect(accountClient.disableSettingsSync).toHaveBeenCalledOnce();
  });

  it('hides the local override action unless sync is consented and stable', () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false,
      settingsSyncEnabled: true, settingsSyncStatus: 'Pending', pairingId: null, pairingFingerprint: null,
      pendingPairings: [], error: null, remoteRevoked: null,
      settingsSyncLocalOverrides: { theme: false, language: false, duration: false },
    } };
    act(() => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect([...container.querySelectorAll('button')].some(button => button.textContent === es.accountSettingsSyncOverridesAction)).toBe(false);
  });

  it('shows conflicting values and submits only the selected source for each conflicted field', async () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: true,
      settingsSyncStatus: 'Conflict', pairingId: null, pairingFingerprint: null, pendingPairings: [], error: null, remoteRevoked: null,
      settingsSyncConflict: { revision: 3, fields: ['theme', 'duration'],
        current: { defaultDurationMinutes: 45, theme: 'light', uiLanguage: 'es' },
        thisDevice: { defaultDurationMinutes: 50, theme: 'dark', uiLanguage: 'es' }, resetConflict: false },
    } };
    await act(async () => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncThemeLight);
    expect(container.textContent).toContain(es.accountSettingsSyncThemeDark);
    const apply = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncResolve)!;
    expect(apply.disabled).toBe(true);
    const radios = [...container.querySelectorAll<HTMLInputElement>('input[type="radio"]')];
    await act(async () => { radios[0]!.click(); radios[3]!.click(); });
    expect(apply.disabled).toBe(false);
    await act(async () => apply.click());
    expect(accountClient.resolveSettingsSyncConflict).toHaveBeenCalledWith(3, [
      { field: 'theme', source: 'thisDevice' }, { field: 'duration', source: 'otherDevices' },
    ]);
  });

  it('shows reset consequences and requires an explicit reset-or-remote choice', async () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: true,
      settingsSyncStatus: 'Conflict', pairingId: null, pairingFingerprint: null, pendingPairings: [], error: null, remoteRevoked: null,
      settingsSyncConflict: { revision: 8, fields: ['theme', 'language', 'duration'],
        current: { defaultDurationMinutes: 35, theme: 'light', uiLanguage: 'en' },
        thisDevice: { defaultDurationMinutes: 50, theme: 'dark', uiLanguage: 'es' }, resetConflict: true },
    } };
    await act(async () => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncResetBoundary);
    expect(container.textContent).toContain(es.accountSettingsSyncThemeLight);
    const keepReset = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncKeepReset)!;
    const keepRemote = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncKeepOther)!;
    expect(keepReset.disabled).toBe(false);
    expect(keepRemote.disabled).toBe(false);
    await act(async () => keepRemote.click());
    expect(accountClient.resolveSettingsSyncResetConflict).toHaveBeenCalledWith(8, 'otherDevices');
  });

  it('offers checkpoint recovery only when the Host reports a recoverable storage failure', async () => {
    const accountClient = client();
    const snapshot: AccountSnapshot = { phase: 'ready', error: null, account: {
      signedIn: true, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
      displayName: 'Test User', email: 'user@example.invalid', emailVerified: true,
      deviceLinked: true, accountHasLinkedDevice: true, deviceLinkPending: false, pairingBusy: false,
      settingsTransferBusy: false, settingsTransferStatus: null, settingsSyncBusy: false, settingsSyncEnabled: true,
      settingsSyncStatus: 'Attention', settingsSyncRecoveryAvailable: true,
      pairingId: null, pairingFingerprint: null, pendingPairings: [], error: null, remoteRevoked: null,
    } };
    await act(async () => root.render(<AccountPanel client={accountClient} snapshot={snapshot} t={t}/>));
    expect(container.textContent).toContain(es.accountSettingsSyncAttention);
    const recover = [...container.querySelectorAll('button')].find(button => button.textContent === es.accountSettingsSyncRecover)!;
    expect(recover.disabled).toBe(false);
    await act(async () => recover.click());
    expect(accountClient.recoverSettingsSync).toHaveBeenCalledOnce();

    await act(async () => root.render(<AccountPanel client={accountClient} snapshot={{ ...snapshot,
      account: { ...snapshot.account!, settingsSyncRecoveryAvailable: false } }} t={t}/>));
    expect([...container.querySelectorAll('button')].some(button => button.textContent === es.accountSettingsSyncRecover)).toBe(false);
  });
  it('shows the signed-in identity chip only after sign-in and opens the account tab', () => {
    const onOpen = vi.fn();
    act(() => root.render(<AccountChip snapshot={base} onOpen={onOpen} t={t} />));
    expect(container.querySelector('.account-chip')).toBeNull();
    const signedIn = { ...base, account: { signedIn: true, displayName: 'Ada Lovelace', email: 'ada@example.com', deviceLinked: true } } as unknown as AccountSnapshot;
    act(() => root.render(<AccountChip snapshot={signedIn} onOpen={onOpen} t={t} />));
    const chip = container.querySelector<HTMLButtonElement>('.account-chip')!;
    expect(chip.textContent).toContain('Ada Lovelace');
    expect(chip.getAttribute('aria-label')).toContain('Ada Lovelace');
    act(() => chip.click());
    expect(onOpen).toHaveBeenCalledOnce();
    expect(accountInitials(null, 'ada@example.com')).toBe('AE');
  });
});
