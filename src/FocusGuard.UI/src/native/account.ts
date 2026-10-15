import { parseHostInitialization, type WebViewTransport } from './protocol';

export type NativeAccount = Readonly<{
  signedIn: boolean;
  signInPending: boolean;
  signOutPending: boolean;
  checkingSession: boolean;
  checkingDevice: boolean;
  displayName: string | null;
  email: string | null;
  emailVerified: boolean;
  deviceLinked: boolean;
  accountHasLinkedDevice: boolean;
  deviceLinkPending: boolean;
  pairingBusy: boolean;
  pairingId: string | null;
  pairingFingerprint: string | null;
  pendingPairings: readonly PendingDevicePairing[];
  settingsTransferBusy: boolean;
  settingsTransferStatus: 'Shared' | 'Imported' | 'NoSharedSettings' | 'PortableExported' | 'PortableImported' | 'Cancelled' | null;
  settingsSyncBusy: boolean;
  settingsSyncEnabled: boolean;
  settingsSyncStatus: 'Enabled' | 'Disabled' | 'Enabling' | 'Disabling' | 'Resetting' | 'Resolving' | 'Recovering' | 'Cancelled' | 'Pending' | 'Conflict' | 'Attention' | null;
  settingsSyncConflict?: SettingsSyncConflictReview | null;
  settingsSyncRecoveryAvailable?: boolean;
  settingsSyncLocalOverrides?: SettingsSyncLocalOverrideSummary | null;
  error: string | null;
  remoteRevoked: boolean | null;
}>;

export type SettingsSyncField = 'theme' | 'language' | 'duration';
export type SettingsSyncChoiceSource = 'thisDevice' | 'otherDevices';
export type SettingsSyncResetChoice = 'thisDeviceReset' | 'otherDevices';
export type SettingsSyncConflictChoices = Readonly<Partial<Record<SettingsSyncField, SettingsSyncChoiceSource>>>;
export type SettingsSyncConflictReview = Readonly<{
  revision: number;
  fields: readonly SettingsSyncField[];
  current: Readonly<{ defaultDurationMinutes: number; theme: 'dark' | 'light'; uiLanguage: 'es' | 'en' }>;
  thisDevice: Readonly<{ defaultDurationMinutes: number; theme: 'dark' | 'light'; uiLanguage: 'es' | 'en' }>;
  resetConflict: boolean;
}>;
export type SettingsSyncLocalOverrideSummary = Readonly<{ theme: boolean; language: boolean; duration: boolean }>;

export type PendingDevicePairing = Readonly<{
  pairingId: string;
  deviceId: string;
  platform: string;
  clientVersion: string;
  fingerprint: string;
  expiresAtUtc: string;
}>;

export type AccountSnapshot = Readonly<{
  phase: 'waiting' | 'loading' | 'ready' | 'unavailable';
  account: NativeAccount | null;
  error: string | null;
}>;

export type AccountClient = ReturnType<typeof createNativeAccountClient>;

const empty: AccountSnapshot = Object.freeze({ phase: 'waiting', account: null, error: null });
const emptyUnavailable: AccountSnapshot = Object.freeze({ phase: 'unavailable', account: null, error: 'BridgeUnavailable' });
const exact = (value: Record<string, unknown>, keys: readonly string[]) =>
  Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const nullableText = (value: unknown, max: number): value is string | null => value === null || typeof value === 'string' && value.length <= max;
const isGuid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value);
const isFingerprint = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{43}$/.test(value);
const fields = ['theme', 'language', 'duration'] as const;
function settingsValues(value: unknown): value is SettingsSyncConflictReview['current'] {
  return isRecord(value) && exact(value, ['defaultDurationMinutes', 'theme', 'uiLanguage'])
    && Number.isInteger(value.defaultDurationMinutes) && Number(value.defaultDurationMinutes) >= 1 && Number(value.defaultDurationMinutes) <= 720
    && (value.theme === 'dark' || value.theme === 'light') && (value.uiLanguage === 'es' || value.uiLanguage === 'en');
}
function settingsConflict(value: unknown): value is SettingsSyncConflictReview | null {
  if (value === null) return true;
  return isRecord(value) && exact(value, ['revision', 'fields', 'current', 'thisDevice', 'resetConflict'])
    && Number.isSafeInteger(value.revision) && Number(value.revision) >= 0
    && Array.isArray(value.fields) && value.fields.length >= 1 && value.fields.length <= 3
    && value.fields.every(field => typeof field === 'string' && fields.includes(field as SettingsSyncField))
    && new Set(value.fields).size === value.fields.length
    && settingsValues(value.current) && settingsValues(value.thisDevice) && typeof value.resetConflict === 'boolean';
}
function settingsLocalOverrides(value: unknown): value is SettingsSyncLocalOverrideSummary | null {
  return value === null || isRecord(value) && exact(value, ['theme', 'language', 'duration'])
    && typeof value.theme === 'boolean' && typeof value.language === 'boolean' && typeof value.duration === 'boolean';
}

function account(value: unknown): NativeAccount | null {
  if (!isRecord(value) || !exact(value, ['signedIn', 'signInPending', 'signOutPending', 'checkingSession', 'checkingDevice',
    'displayName', 'email', 'emailVerified', 'deviceLinked', 'accountHasLinkedDevice', 'deviceLinkPending', 'error', 'remoteRevoked',
    'pairingBusy', 'pairingId', 'pairingFingerprint', 'pendingPairings', 'settingsTransferBusy', 'settingsTransferStatus',
    'settingsSyncBusy', 'settingsSyncEnabled', 'settingsSyncStatus', 'settingsSyncConflict', 'settingsSyncRecoveryAvailable', 'settingsSyncLocalOverrides'])) return null;
  const pairings = value.pendingPairings;
  const validPairings = pairings === null || Array.isArray(pairings) && pairings.length <= 10 && pairings.every(item =>
    isRecord(item) && exact(item, ['pairingId', 'deviceId', 'platform', 'clientVersion', 'fingerprint', 'expiresAtUtc']) &&
    isGuid(item.pairingId) && isGuid(item.deviceId) &&
    typeof item.platform === 'string' && item.platform.length > 0 && item.platform.length <= 20 &&
    typeof item.clientVersion === 'string' && item.clientVersion.length > 0 && item.clientVersion.length <= 32 &&
    isFingerprint(item.fingerprint) &&
    typeof item.expiresAtUtc === 'string' && item.expiresAtUtc.length <= 40 && Number.isFinite(Date.parse(item.expiresAtUtc)));
  const pendingPairingValid = value.pairingId === null && value.pairingFingerprint === null
    || isGuid(value.pairingId) && isFingerprint(value.pairingFingerprint);
  return typeof value.signedIn === 'boolean' && typeof value.signInPending === 'boolean' && typeof value.signOutPending === 'boolean'
    && typeof value.checkingSession === 'boolean' && typeof value.checkingDevice === 'boolean'
    && nullableText(value.displayName, 120) && nullableText(value.email, 254)
    && typeof value.emailVerified === 'boolean' && typeof value.deviceLinked === 'boolean'
    && typeof value.accountHasLinkedDevice === 'boolean' && typeof value.deviceLinkPending === 'boolean' && nullableText(value.error, 64)
    && (value.remoteRevoked === null || typeof value.remoteRevoked === 'boolean')
    && typeof value.pairingBusy === 'boolean' && pendingPairingValid
    && typeof value.settingsTransferBusy === 'boolean'
    && (value.settingsTransferStatus === null || ['Shared', 'Imported', 'NoSharedSettings', 'PortableExported', 'PortableImported', 'Cancelled'].includes(String(value.settingsTransferStatus)))
    && typeof value.settingsSyncBusy === 'boolean' && typeof value.settingsSyncEnabled === 'boolean'
    && (value.settingsSyncStatus === null || ['Enabled', 'Disabled', 'Enabling', 'Disabling', 'Resetting', 'Resolving', 'Recovering', 'Cancelled', 'Pending', 'Conflict', 'Attention'].includes(String(value.settingsSyncStatus)))
    && settingsConflict(value.settingsSyncConflict)
    && settingsLocalOverrides(value.settingsSyncLocalOverrides)
    && typeof value.settingsSyncRecoveryAvailable === 'boolean'
    && validPairings
    ? {
      signedIn: value.signedIn,
      signInPending: value.signInPending,
      signOutPending: value.signOutPending,
      checkingSession: value.checkingSession,
      checkingDevice: value.checkingDevice,
      displayName: value.displayName,
      email: value.email,
      emailVerified: value.emailVerified,
      deviceLinked: value.deviceLinked,
      accountHasLinkedDevice: value.accountHasLinkedDevice,
      deviceLinkPending: value.deviceLinkPending,
      pairingBusy: value.pairingBusy,
      pairingId: value.pairingId,
      pairingFingerprint: value.pairingFingerprint,
      pendingPairings: pairings ?? [],
      settingsTransferBusy: value.settingsTransferBusy,
      settingsTransferStatus: value.settingsTransferStatus as NativeAccount['settingsTransferStatus'],
      settingsSyncBusy: value.settingsSyncBusy,
      settingsSyncEnabled: value.settingsSyncEnabled,
      settingsSyncStatus: value.settingsSyncStatus as NativeAccount['settingsSyncStatus'],
      settingsSyncConflict: value.settingsSyncConflict as SettingsSyncConflictReview | null,
      settingsSyncRecoveryAvailable: value.settingsSyncRecoveryAvailable,
      settingsSyncLocalOverrides: value.settingsSyncLocalOverrides as SettingsSyncLocalOverrideSummary | null,
      error: value.error,
      remoteRevoked: value.remoteRevoked,
    } as NativeAccount : null;
}

function defaultTransport(): WebViewTransport | null {
  const host = globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } };
  return host.chrome?.webview ?? null;
}

/** Account state crosses the bridge; OIDC tokens never do. */
export function createNativeAccountClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? defaultTransport() : provided;
  const listeners = new Set<() => void>();
  const pending = new Map<string, { timer: ReturnType<typeof setTimeout>; resolve: (value: AccountSnapshot) => void }>();
  let snapshot: AccountSnapshot = transport ? empty : emptyUnavailable;
  let token: string | null = null;
  let active = false;
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let pollInterval: number | null = null;

  const publish = (next: AccountSnapshot) => {
    if (snapshot.phase === next.phase && snapshot.account === next.account && snapshot.error === next.error) return;
    snapshot = Object.freeze(next);
    for (const listener of listeners) listener();
    const accountState = snapshot.account;
    const nextPollInterval = !accountState ? null
      : accountState.signInPending || accountState.signOutPending || accountState.checkingSession || accountState.checkingDevice || accountState.deviceLinkPending || accountState.pairingBusy || accountState.settingsTransferBusy || accountState.settingsSyncBusy ? 2500
        : accountState.signedIn ? 120000 : null;
    if (nextPollInterval !== pollInterval) {
      if (pollTimer !== null) clearInterval(pollTimer);
      pollTimer = nextPollInterval === null ? null : setInterval(() => { void refresh(); }, nextPollInterval);
      pollInterval = nextPollInterval;
    }
  };

  const onMessage = (event: Event) => {
    if (!active) return;
    const initialization = parseHostInitialization((event as MessageEvent<unknown>).data);
    if (initialization) {
      token = initialization.documentToken;
      publish({ phase: 'loading', account: snapshot.account, error: null });
      void refresh();
      return;
    }
    const value: unknown = (event as MessageEvent<unknown>).data;
    if (!isRecord(value) || !exact(value, ['version', 'requestId', 'documentToken', 'error', 'result'])
      || value.version !== 1 || typeof value.requestId !== 'string' || !/^[a-f0-9]{32}$/i.test(value.requestId)
      || value.documentToken !== token || !(value.error === null || typeof value.error === 'string')) return;
    const request = pending.get(value.requestId);
    if (!request) return;
    clearTimeout(request.timer);
    pending.delete(value.requestId);
    const data = account(value.result);
    if (value.error !== null || !data) {
      const error = typeof value.error === 'string' ? value.error : 'InvalidResponse';
      publish({ phase: 'unavailable', account: null, error });
    } else {
      publish({ phase: 'ready', account: data, error: data.error });
    }
    request.resolve(snapshot);
  };

  const request = (method: 'account.get' | 'account.login' | 'account.login.cancel' | 'account.logout' | 'account.security.open'
    | 'account.device.link' | 'account.device.pair.request' | 'account.device.pair.refresh'
    | 'account.device.pair.complete' | 'account.device.pair.approve' | 'account.settings.share' | 'account.settings.import'
    | 'account.settings.sync.enable' | 'account.settings.sync.disable' | 'account.settings.sync.reset' | 'account.settings.sync.resolve'
    | 'account.settings.sync.overrides' | 'account.settings.sync.resolve-reset' | 'account.settings.sync.recover'
    | 'account.settings.portable.export' | 'account.settings.portable.import', params: Record<string, unknown> = {}):
    Promise<AccountSnapshot> => {
    if (!active || !transport || !token || !globalThis.crypto) {
      publish({ phase: 'unavailable', account: null, error: 'BridgeUnavailable' });
      return Promise.resolve(snapshot);
    }
    const requestId = globalThis.crypto.randomUUID().replaceAll('-', '').toLowerCase();
    return new Promise(resolve => {
      const timer = setTimeout(() => {
        pending.delete(requestId);
        publish({ phase: snapshot.account ? 'ready' : 'unavailable', account: snapshot.account, error: 'RequestTimeout' });
        resolve(snapshot);
      }, 5000);
      pending.set(requestId, { timer, resolve });
      try { transport.postMessage({ version: 1, requestId, documentToken: token, method, params }); }
      catch {
        clearTimeout(timer); pending.delete(requestId);
        publish({ phase: 'unavailable', account: null, error: 'BridgeUnavailable' });
        resolve(snapshot);
      }
    });
  };

  function refresh(): Promise<AccountSnapshot> { return request('account.get'); }

  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot,
    start() {
      if (active) return;
      active = true;
      transport?.addEventListener('message', onMessage);
      if (!transport) publish(emptyUnavailable);
    },
    stop() {
      active = false;
      transport?.removeEventListener('message', onMessage);
      if (pollTimer !== null) clearInterval(pollTimer);
      pollTimer = null;
      pollInterval = null;
      for (const item of pending.values()) { clearTimeout(item.timer); item.resolve(snapshot); }
      pending.clear();
    },
    refresh,
    login() { return request('account.login'); },
    cancelLogin() { return request('account.login.cancel'); },
    logout() { return request('account.logout'); },
    openSecurity() { return request('account.security.open'); },
    linkDevice() { return request('account.device.link'); },
    requestPairing() { return request('account.device.pair.request'); },
    refreshPairings() { return request('account.device.pair.refresh'); },
    completePairing() { return request('account.device.pair.complete'); },
    approvePairing(pairingId: string, fingerprint: string) {
      return request('account.device.pair.approve', { pairingId, fingerprint });
    },
    shareSettings() { return request('account.settings.share'); },
    importSettings() { return request('account.settings.import'); },
    exportPortableSettings() { return request('account.settings.portable.export'); },
    importPortableSettings() { return request('account.settings.portable.import'); },
    enableSettingsSync() { return request('account.settings.sync.enable'); },
    disableSettingsSync() { return request('account.settings.sync.disable'); },
    updateLocalSettingsOverrides() { return request('account.settings.sync.overrides'); },
    resetSettingsSync() { return request('account.settings.sync.reset'); },
    resolveSettingsSyncConflict(revision: number, choices: readonly { field: SettingsSyncField; source: SettingsSyncChoiceSource }[]) {
      return request('account.settings.sync.resolve', { revision, choices });
    },
    resolveSettingsSyncResetConflict(revision: number, choice: SettingsSyncResetChoice) {
      return request('account.settings.sync.resolve-reset', { revision, choice });
    },
    recoverSettingsSync() { return request('account.settings.sync.recover'); },
  };
}
