import { afterEach, describe, expect, it, vi } from 'vitest';
import { createNativeAccountClient } from './account';
import type { WebViewTransport } from './protocol';

class FakeWebView extends EventTarget implements WebViewTransport {
  readonly posted: Record<string, unknown>[] = [];
  postMessage(message: unknown): void { this.posted.push(message as Record<string, unknown>); }
  send(data: unknown): void {
    const event = new Event('message');
    Object.defineProperty(event, 'data', { value: data });
    this.dispatchEvent(event);
  }
}

const documentToken = '1234567890abcdef1234567890abcdef';
const state = {
  signedIn: false, signInPending: false, signOutPending: false, checkingSession: false, checkingDevice: false,
  displayName: null, email: null, emailVerified: false, deviceLinked: false, deviceLinkPending: false, error: null, remoteRevoked: null,
  accountHasLinkedDevice: false, pairingBusy: false, pairingId: null, pairingFingerprint: null, pendingPairings: [],
  settingsTransferBusy: false, settingsTransferStatus: null,
  settingsSyncBusy: false, settingsSyncEnabled: false, settingsSyncStatus: 'Disabled', settingsSyncConflict: null,
  settingsSyncRecoveryAvailable: false, settingsSyncLocalOverrides: null,
};
function initialize(webview: FakeWebView): void {
  webview.send({ type: 'host.initialize', version: 1, documentToken, title: 'FocusGuard', description: 'Account', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
}
function reply(webview: FakeWebView, account: Record<string, unknown> = state): void {
  const request = webview.posted.at(-1)!;
  webview.send({ version: 1, requestId: request.requestId, documentToken, error: null, result: account });
}

afterEach(() => vi.useRealTimers());

describe('native account bridge', () => {
  it('reads only sanitized account state and starts login on explicit request', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.get', params: {} });
    reply(webview);
    expect(client.getSnapshot()).toMatchObject({ phase: 'ready', account: { signedIn: false, deviceLinked: false } });

    const pending = client.login();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.login', params: {} });
    reply(webview, { ...state, signInPending: true });
    await pending;
    expect(client.getSnapshot().account?.signInPending).toBe(true);
    client.stop();
  });

  it('accepts a sanitized local device-link state but rejects token material', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true });
    expect(client.getSnapshot()).toMatchObject({ phase: 'ready', account: { deviceLinked: true } });
    const pending = client.linkDevice();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.device.link', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinkPending: true });
    await pending;
    expect(client.getSnapshot().account?.deviceLinkPending).toBe(true);
    client.stop();
  });

  it('rejects account responses which imply tokens or expose a recovery kit', () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, recoveryKit: 'secret' });
    expect(client.getSnapshot()).toMatchObject({ phase: 'unavailable', account: null, error: 'InvalidResponse' });
    client.stop();
  });

  it('rejects malformed pairing fingerprints and only accepts canonical public pairing state', () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, accountHasLinkedDevice: true, pairingId: 'not-a-guid', pairingFingerprint: 'short' });
    expect(client.getSnapshot()).toMatchObject({ phase: 'unavailable', account: null, error: 'InvalidResponse' });
    client.stop();
  });

  it('sends settings transfer intent without placing preference values in bridge parameters', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, accountHasLinkedDevice: true });
    const pending = client.shareSettings();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.share', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsTransferBusy: true });
    await pending;
    expect(client.getSnapshot().account?.settingsTransferBusy).toBe(true);
    client.stop();
  });

  it('sends explicit automatic-sync opt-in and opt-out with empty parameters', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true });
    const enabling = client.enableSettingsSync();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.enable', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncBusy: true, settingsSyncStatus: 'Enabling' });
    await enabling;
    const disabling = client.disableSettingsSync();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.disable', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: false, settingsSyncStatus: 'Disabled' });
    await disabling;
    const localOverrides = client.updateLocalSettingsOverrides();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.overrides', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true,
      settingsSyncStatus: 'Enabled', settingsSyncLocalOverrides: { theme: true, language: false, duration: true } });
    await localOverrides;
    expect(client.getSnapshot().account?.settingsSyncLocalOverrides).toEqual({ theme: true, language: false, duration: true });
    client.stop();
  });

  it('sends only a revision and per-field source choices for conflict resolution', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true, settingsSyncStatus: 'Conflict',
      settingsSyncConflict: { revision: 4, fields: ['theme'], current: { defaultDurationMinutes: 50, theme: 'light', uiLanguage: 'es' },
        thisDevice: { defaultDurationMinutes: 50, theme: 'dark', uiLanguage: 'es' }, resetConflict: false } });
    const resolving = client.resolveSettingsSyncConflict(4, [{ field: 'theme', source: 'otherDevices' }]);
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.resolve', params: {
      revision: 4, choices: [{ field: 'theme', source: 'otherDevices' }],
    } });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true, settingsSyncStatus: 'Resolving' });
    await resolving;
    expect(client.getSnapshot().account?.settingsSyncStatus).toBe('Resolving');
    client.stop();
  });

  it('sends a revision and one explicit whole-reset recovery choice', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true, settingsSyncStatus: 'Conflict',
      settingsSyncConflict: { revision: 9, fields: ['theme', 'language', 'duration'],
        current: { defaultDurationMinutes: 35, theme: 'light', uiLanguage: 'en' },
        thisDevice: { defaultDurationMinutes: 50, theme: 'dark', uiLanguage: 'es' }, resetConflict: true } });
    const resolving = client.resolveSettingsSyncResetConflict(9, 'thisDeviceReset');
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.resolve-reset',
      params: { revision: 9, choice: 'thisDeviceReset' } });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncBusy: true, settingsSyncStatus: 'Resolving' });
    await resolving;
    client.stop();
  });

  it('sends only the revision and explicit whole-reset recovery choice', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true, settingsSyncStatus: 'Conflict',
      settingsSyncConflict: { revision: 12, fields: ['theme', 'language', 'duration'],
        current: { defaultDurationMinutes: 35, theme: 'light', uiLanguage: 'en' },
        thisDevice: { defaultDurationMinutes: 50, theme: 'dark', uiLanguage: 'es' }, resetConflict: true } });
    const resolving = client.resolveSettingsSyncResetConflict(12, 'thisDeviceReset');
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.resolve-reset',
      params: { revision: 12, choice: 'thisDeviceReset' } });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncBusy: true, settingsSyncStatus: 'Resolving' });
    await resolving;
    client.stop();
  });

  it('requests retry-checkpoint recovery without accepting a path or file data', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true,
      settingsSyncStatus: 'Attention', settingsSyncRecoveryAvailable: true });
    const recovering = client.recoverSettingsSync();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.recover', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true,
      settingsSyncBusy: true, settingsSyncStatus: 'Recovering', settingsSyncRecoveryAvailable: true });
    await recovering;
    expect(client.getSnapshot().account?.settingsSyncStatus).toBe('Recovering');
    client.stop();
  });

  it('requests a shared settings reset without accepting caller-supplied values', async () => {
    const webview = new FakeWebView();
    const client = createNativeAccountClient(webview);
    client.start();
    initialize(webview);
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true,
      settingsSyncStatus: 'Enabled' });
    const reset = client.resetSettingsSync();
    expect(webview.posted.at(-1)).toMatchObject({ method: 'account.settings.sync.reset', params: {} });
    reply(webview, { ...state, signedIn: true, deviceLinked: true, settingsSyncEnabled: true,
      settingsSyncBusy: true, settingsSyncStatus: 'Resetting' });
    await reset;
    expect(client.getSnapshot().account?.settingsSyncStatus).toBe('Resetting');
    client.stop();
  });
});
