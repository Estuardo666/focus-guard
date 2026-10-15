import { createDashboardClient, isNormalReview, isProfilePage, type NormalReview } from './dashboard';
import type { BridgeStatus, WebViewTransport } from './protocol';
import type { UserPreferences } from './preferences';

const token = '11111111111141118111111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
const profile = { id: profileId, name: 'Saved profile', applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 0 };
const catalog = { items: [profile], offset: 0, total: 1, pilotEnabled: true };
const preferences: UserPreferences = { uiLanguage: 'es', theme: 'Light', reduceMotion: false, sessionEnd: 'Notification', cyclePhaseNotifications: true, enforcementEnabled: true, lastDurationMinutes: 2, lastSessionMode: 'SimpleTimer', lastCyclePresetId: null };
const review: NormalReview = { preparationId: '33333333333343338333333333333333', profileId, profileName: profile.name, durationSeconds: 1500, enforcementEnabled: false, applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 0, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:25:00Z', expiresUtc: '2040-01-01T12:02:00Z', extensionReady: false };
const idle: BridgeStatus = { serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0, sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' };
function fixture() {
  const target = new EventTarget();
  const messages: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(message) { messages.push(message as Record<string, unknown>); } };
  const client = createDashboardClient(transport);
  client.start();
  const emit = (value: unknown) => target.dispatchEvent(new MessageEvent('message', { data: value }));
  const initialize = (documentToken = token) => emit({ type: 'host.initialize', version: 1, documentToken, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  const reply = (result: unknown, error: string | null = null, request = messages.at(-1)!) => emit({ version: 1, requestId: request.requestId, documentToken: request.documentToken, error, result });
  return { client, messages, emit, initialize, reply };
}
beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());
it('loads saved profiles and sends only typed review and one explicit confirmation', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  expect(f.client.getSnapshot().catalog).toEqual(catalog);
  const preparing = f.client.prepare(profileId, 25, false);
  expect(f.messages.at(-1)?.params).toEqual({ profileId: profileId.replaceAll('-', ''), durationSeconds: 1500, enforcementEnabled: false });
  f.reply(review); await preparing;
  expect(f.client.getSnapshot().phase).toBe('review');
  const starting = f.client.confirm('  Read chapter four  ');
  const request = f.messages.at(-1)!;
  expect(request.method).toBe('session.startNormal');
  expect(request.params).toEqual({ operationId: expect.any(String), preparationId: review.preparationId, objective: 'Read chapter four' });
  const second = f.client.confirm(); await second;
  expect(f.messages.filter(m => m.method === 'session.startNormal')).toHaveLength(1);
  f.reply({ sessionId: '44444444-4444-4444-8444-444444444444', startUtc: review.startUtc, endUtc: review.endUtc }); await starting;
  expect(f.client.getSnapshot().phase).toBe('started'); f.client.stop();
});
it('retries the initial preferences read when startup profile loading already owns the bridge', async () => {
  const f = fixture();
  f.initialize();
  expect(f.messages.at(-1)?.method).toBe('profiles.list');

  const loading = f.client.loadPreferences();
  f.reply(catalog, null, f.messages[0]!);
  await vi.advanceTimersByTimeAsync(300);

  const busyPreferenceRequest = f.messages.at(-1)!;
  expect(busyPreferenceRequest.method).toBe('preferences.get');
  f.reply(null, 'RequestInProgress', busyPreferenceRequest);
  await vi.advanceTimersByTimeAsync(300);

  const preferenceRequest = f.messages.at(-1)!;
  expect(preferenceRequest.method).toBe('preferences.get');
  expect(preferenceRequest.requestId).not.toBe(busyPreferenceRequest.requestId);
  f.reply(preferences, null, preferenceRequest);
  await loading;

  expect(f.client.getSnapshot()).toMatchObject({ preferences, preferencesLoading: false, preferencesError: null });
  f.client.stop();
});
it('waits for the bridge handshake instead of failing the startup preferences read', async () => {
  const f = fixture();
  const loading = f.client.loadPreferences();
  await vi.advanceTimersByTimeAsync(0);
  expect(f.messages).toHaveLength(0);
  expect(f.client.getSnapshot().preferencesError).toBeNull();

  await vi.advanceTimersByTimeAsync(300);
  f.initialize();
  f.reply(catalog, null, f.messages.at(-1)!);
  await vi.advanceTimersByTimeAsync(300);
  const request = f.messages.at(-1)!;
  expect(request.method).toBe('preferences.get');
  f.reply(preferences, null, request);
  await loading;

  expect(f.client.getSnapshot()).toMatchObject({ preferences, preferencesLoading: false, preferencesError: null });
  f.client.stop();
});
it('reports a read failure only after the bounded retries and keeps the save phase untouched', async () => {
  const f = fixture();
  const loading = f.client.loadPreferences();
  await vi.advanceTimersByTimeAsync(5000);
  await loading;
  expect(f.client.getSnapshot()).toMatchObject({ preferences: null, preferencesLoading: false, preferencesError: 'BridgeUnavailable', preferencesPhase: 'idle' });
  f.client.stop();
});
it('recovers an exhausted initial preferences read automatically after the bridge starts', async () => {
  const f = fixture();
  const loading = f.client.loadPreferences();
  await vi.advanceTimersByTimeAsync(4000);
  await loading;
  expect(f.client.getSnapshot().preferences).toBeNull();

  f.initialize();
  f.reply(catalog, null, f.messages.at(-1)!);
  await Promise.resolve();
  await vi.advanceTimersByTimeAsync(2000);

  const recoveredRead = f.messages.at(-1)!;
  expect(recoveredRead.method).toBe('preferences.get');
  f.reply(preferences, null, recoveredRead);
  await Promise.resolve();
  expect(f.client.getSnapshot()).toMatchObject({ preferences, preferencesError: null, preferencesPhase: 'idle' });
  expect(f.messages.some(message => message.method === 'preferences.set')).toBe(false);
  f.client.stop();
});
it('backs off repeated recovery reads when the bridge remains unavailable', async () => {
  const f = fixture();
  const loading = f.client.loadPreferences();
  await vi.advanceTimersByTimeAsync(4000);
  await loading;
  f.initialize();
  f.reply(catalog, null, f.messages.at(-1)!);
  await Promise.resolve();

  await vi.advanceTimersByTimeAsync(2000);
  const firstRecovery = f.messages.at(-1)!;
  expect(firstRecovery.method).toBe('preferences.get');
  f.reply(null, 'BridgeUnavailable', firstRecovery);
  await Promise.resolve();
  await vi.advanceTimersByTimeAsync(3999);
  expect(f.messages.filter(message => message.method === 'preferences.get')).toHaveLength(1);
  await vi.advanceTimersByTimeAsync(1);
  expect(f.messages.filter(message => message.method === 'preferences.get')).toHaveLength(2);

  const readsBeforeStop = f.messages.filter(message => message.method === 'preferences.get').length;
  f.client.stop();
  await vi.advanceTimersByTimeAsync(60000);
  expect(f.messages.filter(message => message.method === 'preferences.get')).toHaveLength(readsBeforeStop);
});
it('refreshes externally saved preferences without writes, preempts reads for saves, and stops its timer', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const loading = f.client.loadPreferences();
  const initialRead = f.messages.at(-1)!;
  f.reply(preferences, null, initialRead); await loading;

  await vi.advanceTimersByTimeAsync(2000);
  const externalRead = f.messages.at(-1)!;
  expect(externalRead.method).toBe('preferences.get');
  const remote = { ...preferences, uiLanguage: 'en' as const, theme: 'Dark' as const, lastDurationMinutes: 50 };
  f.reply(remote, null, externalRead); await Promise.resolve();
  expect(f.client.getSnapshot().preferences).toEqual(remote);
  expect(f.messages.some(message => message.method === 'preferences.set')).toBe(false);

  await vi.advanceTimersByTimeAsync(2000);
  const pendingRefresh = f.messages.at(-1)!;
  const updated: UserPreferences = { ...remote, uiLanguage: 'es', lastDurationMinutes: 17 };
  const saving = f.client.savePreferences(updated);
  expect(f.messages.at(-1)?.method).toBe('preferences.set');
  f.reply(updated, null, f.messages.at(-1)!); await saving;
  f.reply(remote, null, pendingRefresh);
  await Promise.resolve();
  expect(f.client.getSnapshot()).toMatchObject({ preferences: updated, preferencesPhase: 'idle', preferencesError: null });
  expect(f.messages.some(message => message.method === 'preferences.set')).toBe(true);

  f.client.observeStatus(idle, true);
  await vi.advanceTimersByTimeAsync(2000);
  const readBeforeAction = f.messages.at(-1)!;
  const preparing = f.client.prepare(profileId, 25, false);
  expect(f.messages.at(-1)?.method).toBe('session.prepareNormal');
  f.reply(remote, null, readBeforeAction);
  f.reply(review, null, f.messages.at(-1)!);
  await preparing;
  expect(f.client.getSnapshot().preferences).toEqual(updated);
  f.client.cancelReview();

  const readsBeforeStop = f.messages.filter(message => message.method === 'preferences.get').length;
  f.client.stop();
  await vi.advanceTimersByTimeAsync(10000);
  expect(f.messages.filter(message => message.method === 'preferences.get')).toHaveLength(readsBeforeStop);
});
it('validates profile accent colors as optional identity metadata', () => {
  expect(isProfilePage({ ...catalog, items: [{ ...profile, accentHex: '#aabbcc' }] })).toBe(true);
  expect(isProfilePage({ ...catalog, items: [{ ...profile, accentHex: '#ABC' }] })).toBe(false);
});
it('never retries uncertain activation and requires a new authoritative status object to reconcile', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  f.client.observeStatus(idle, true);
  const preparing = f.client.prepare(profileId, 25, false); f.reply(review); await preparing;
  const starting = f.client.confirm(); await vi.advanceTimersByTimeAsync(4001); await starting;
  expect(f.client.getSnapshot().phase).toBe('unknown');
  await f.client.prepare(profileId, 25, false); await f.client.confirm();
  f.client.observeStatus(idle, true); expect(f.client.getSnapshot().phase).toBe('unknown');
  expect(f.messages.filter(m => m.method === 'session.startNormal')).toHaveLength(1);
  f.client.observeStatus({ ...idle }, true); expect(f.client.getSnapshot().phase).toBe('idle'); f.client.stop();
});
it('rejects old document and mismatched response ids without accepting a forged review', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const preparing = f.client.prepare(profileId, 25, false);
  f.emit({ version: 1, requestId: token, documentToken: token, error: null, result: review });
  expect(f.client.getSnapshot().phase).toBe('preparing');
  f.initialize('55555555555545558555555555555555'); await preparing;
  expect(f.client.getSnapshot().review).toBeNull(); f.client.stop();
});
it('read-only capability cannot send preparation or confirmation', async () => {
  const f = fixture(); f.initialize(); f.reply({ ...catalog, pilotEnabled: false }); await Promise.resolve();
  await f.client.prepare(profileId, 25, true); await f.client.confirm();
  expect(f.messages).toHaveLength(1); f.client.stop();
});
it('bounds catalog data and validates actual UTC calendar dates and counts', () => {
  expect(isProfilePage(catalog)).toBe(true);
  expect(isProfilePage({ ...catalog, items: [profile, profile], total: 2 })).toBe(false);
  expect(isProfilePage({ ...catalog, total: 0 })).toBe(false);
  expect(isProfilePage({ ...catalog, items: [{ ...profile, name: 'x'.repeat(81) }] })).toBe(false);
  expect(isNormalReview(review)).toBe(true);
  expect(isNormalReview({ ...review, blockedApplicationCount: 2 })).toBe(false);
  expect(isNormalReview({ ...review, startUtc: '2040-02-30T12:00:00Z' })).toBe(false);
  expect(isNormalReview({ ...review, strictMode: true })).toBe(false);
  expect(isNormalReview({ ...review, expiresUtc: '2040-01-01T12:03:00Z' })).toBe(false);
});

it('custom cycle review sends exact typed input and confirms once using the cycle command', async () => {
  const { cycleReview, configuration } = await import('./cycle-fixture');
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const preparing = f.client.prepareCycle(profileId, { presetId: null, configuration }, false);
  expect(f.messages.at(-1)?.method).toBe('session.prepareCycle');
  expect(f.messages.at(-1)?.params).toEqual({ profileId: profileId.replaceAll('-', ''), presetId: null, configuration, enforcementEnabled: false });
  expect(f.messages.filter(m => m.method === 'session.startCycle')).toHaveLength(0);
  f.reply(cycleReview); await preparing;
  expect(f.client.getSnapshot().cycleReview).toEqual(cycleReview);
  const starting = f.client.confirm('Cycle objective');
  expect(f.messages.at(-1)?.method).toBe('session.startCycle');
  expect(f.messages.at(-1)?.params).toMatchObject({ objective: 'Cycle objective' });
  f.reply({ sessionId: profileId, startUtc: cycleReview.startUtc, endUtc: cycleReview.endUtc }); await starting;
  await f.client.confirm();
  expect(f.messages.filter(m => m.method === 'session.startCycle')).toHaveLength(1);
  expect(f.client.getSnapshot().phase).toBe('started'); f.client.stop();
});
it('lost cycle confirmation is unknown and never replayed; readonly cannot prepare', async () => {
  const { cycleReview, configuration } = await import('./cycle-fixture');
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const preparing = f.client.prepareCycle(profileId, { presetId: null, configuration }, false); f.reply(cycleReview); await preparing;
  const starting = f.client.confirm(); await vi.advanceTimersByTimeAsync(4001); await starting;
  expect(f.client.getSnapshot().phase).toBe('unknown');
  await f.client.confirm(); await f.client.prepareCycle(profileId, { presetId: null, configuration }, false);
  expect(f.messages.filter(m => m.method === 'session.startCycle')).toHaveLength(1); f.client.stop();
  const readonly = fixture(); readonly.initialize(); readonly.reply({ ...catalog, pilotEnabled: false }); await Promise.resolve();
  await readonly.client.prepareCycle(profileId, { presetId: null, configuration }, false);
  expect(readonly.messages.filter(m => m.method === 'session.prepareCycle')).toHaveLength(0); readonly.client.stop();
});
it('a new document clears cycle review and stale state cannot confirm it', async () => {
  const { cycleReview, configuration } = await import('./cycle-fixture');
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const preparing = f.client.prepareCycle(profileId, { presetId: null, configuration }, false); f.reply(cycleReview); await preparing;
  f.client.observeStatus(idle, false); await f.client.confirm();
  expect(f.client.getSnapshot().cycleReview).toBeNull();
  expect(f.messages.filter(m => m.method === 'session.startCycle')).toHaveLength(0); f.client.stop();
});

const activeControl: BridgeStatus = { ...idle, sessionState: 'Active', sessionId: profileId, profileId, startUtc: review.startUtc, endUtc: review.endUtc, remainingSeconds: 1500, mode: 'FocusCycle', currentPhase: { index: 1, kind: 'ShortBreak', focusCycleNumber: 1, remainingSeconds: 60, startUtc: review.startUtc, endUtc: '2040-01-01T12:01:00Z' }, nextPhase: null, finalEndUtc: review.endUtc };
it('stop review is local until distinct confirmation and stays locked until post-reply status', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve(); f.client.observeStatus(activeControl, true);
  f.client.reviewControl('stop'); expect(f.messages).toHaveLength(1);
  const confirming = f.client.confirmControl();
  expect(f.messages.at(-1)?.method).toBe('session.stopNormal');
  expect(f.messages.at(-1)?.params).toMatchObject({ sessionId: profileId.replaceAll('-', ''), confirmed: true });
  // A poll before the command reply must not reconcile the post-reply state.
  const interim = { ...activeControl }; f.client.observeStatus(interim, true);
  f.reply({ sessionId: profileId, action: 'Stopped' }); await confirming;
  expect(f.client.getSnapshot().phase).toBe('controlled');
  f.client.observeStatus(interim, true); f.client.reviewControl('stop'); await f.client.confirmControl();
  expect(f.messages.filter(m => m.method === 'session.stopNormal')).toHaveLength(1);
  f.client.observeStatus({ ...idle }, true); expect(f.client.getSnapshot().phase).toBe('idle'); f.client.stop();
});
it('skip only reviews the current break and cancels if phase or deadline changes', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  f.client.observeStatus({ ...activeControl, currentPhase: { ...activeControl.currentPhase!, kind: 'Focus' } }, true);
  f.client.reviewControl('skip'); expect(f.client.getSnapshot().controlReview).toBeNull();
  f.client.observeStatus(activeControl, true); f.client.reviewControl('skip');
  expect(f.client.getSnapshot().controlReview?.phaseIndex).toBe(1);
  f.client.observeStatus({ ...activeControl, currentPhase: { ...activeControl.currentPhase!, index: 2 } }, true);
  await f.client.confirmControl(); expect(f.messages).toHaveLength(1); f.client.stop();
});
it('lost stop reply is unknown with no replay and requires a fresh authoritative status', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve(); f.client.observeStatus(activeControl, true);
  f.client.reviewControl('stop'); const confirming = f.client.confirmControl(); await vi.advanceTimersByTimeAsync(4001); await confirming;
  expect(f.client.getSnapshot().phase).toBe('unknown');
  f.client.observeStatus(activeControl, true); f.client.reviewControl('stop'); await f.client.confirmControl();
  expect(f.messages.filter(m => m.method === 'session.stopNormal')).toHaveLength(1);
  f.client.observeStatus({ ...activeControl }, true); expect(f.client.getSnapshot().phase).toBe('idle'); f.client.stop();
});
it('readonly, stale and strict status cannot review session control', async () => {
  const f = fixture(); f.initialize(); f.reply({ ...catalog, pilotEnabled: false }); await Promise.resolve();
  f.client.observeStatus(activeControl, true); f.client.reviewControl('stop'); expect(f.client.getSnapshot().controlReview).toBeNull(); f.client.stop();
  const writable = fixture(); writable.initialize(); writable.reply(catalog); await Promise.resolve();
  writable.client.observeStatus({ ...activeControl, strictMode: true }, true); writable.client.reviewControl('stop'); expect(writable.client.getSnapshot().controlReview).toBeNull();
  writable.client.observeStatus(activeControl, false); writable.client.reviewControl('skip'); expect(writable.client.getSnapshot().controlReview).toBeNull(); writable.client.stop();
});
it('acknowledgment for another action is unknown and never enables a second mutation', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve(); f.client.observeStatus(activeControl, true);
  f.client.reviewControl('skip'); const confirming = f.client.confirmControl();
  expect(f.messages.at(-1)?.params).toMatchObject({ phaseIndex: 1, confirmed: true });
  f.reply({ sessionId: profileId, action: 'Stopped' }); await confirming;
  expect(f.client.getSnapshot().phase).toBe('unknown'); f.client.reviewControl('stop'); await f.client.confirmControl();
  expect(f.messages.filter(m => typeof m.method === 'string' && m.method.startsWith('session.'))).toHaveLength(1); f.client.stop();
});

it('known control rejection requires fresh status before a new review', async () => {
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve(); f.client.observeStatus(activeControl, true);
  f.client.reviewControl('stop'); const confirming = f.client.confirmControl(); f.reply(null, 'SessionChanged'); await confirming;
  expect(f.client.getSnapshot().phase).toBe('error'); f.client.reviewControl('stop'); expect(f.client.getSnapshot().controlReview).toBeNull();
  f.client.observeStatus(activeControl, true); expect(f.client.getSnapshot().phase).toBe('error');
  f.client.observeStatus({ ...activeControl }, true); f.client.reviewControl('stop'); expect(f.client.getSnapshot().phase).toBe('actionReview'); f.client.stop();
});
it('resolves captures once a strict session has ended but not while strict is still in force', async () => {
  const sessionId = '55555555-5555-4555-8555-555555555555';
  const captureId = '66666666-6666-4666-8666-666666666666';
  const summary = { sessionId, outcome: 'Completed', profileName: 'gpt test', plannedMinutes: 10, focusedMinutes: 10, blockedAttempts: 1, streakDays: 0,
    endedAtUtc: '2026-10-10T06:51:13Z', objective: '', reflection: '', capturesTruncated: false,
    captures: [{ id: captureId, text: 'Reply to Ana', sourceKind: 'Website', sourceLabel: 'example.com', capturedAtUtc: '2026-10-10T06:45:00Z', resolution: 'Pending', openable: true }] };
  const strict: BridgeStatus = { ...idle, sessionState: 'Active', remainingSeconds: 60, sessionId, profileId, startUtc: '2026-10-10T06:41:13Z', endUtc: '2026-10-10T06:51:13Z', strictMode: true, enforcementConnected: true };
  const f = fixture(); f.initialize(); f.reply(catalog); await Promise.resolve();
  const loading = f.client.loadSessionSummary(sessionId); f.reply(summary); await loading;

  for (const state of ['Active', 'RecoveryPending'] as const) {
    f.client.observeStatus({ ...strict, sessionState: state, enforcementConnected: state === 'Active' }, true);
    expect(await f.client.resolveCapture(sessionId, captureId, 'dismiss')).toBeNull();
    expect(f.client.getSnapshot().captureError).toBe('CaptureUnavailable');
  }
  expect(f.messages.some(m => m.method === 'capture.dismiss')).toBe(false);

  f.client.observeStatus({ ...strict, sessionState: 'Expired', remainingSeconds: 0, enforcementConnected: false }, true);
  const resolving = f.client.resolveCapture(sessionId, captureId, 'dismiss');
  const request = f.messages.at(-1)!;
  expect(request.method).toBe('capture.dismiss');
  f.reply({ sessionId, captureId, action: 'Dismissed' }, null, request); await Promise.resolve(); await Promise.resolve();
  f.reply({ ...summary, captures: [] });
  expect(await resolving).toEqual({ sessionId, captureId, action: 'Dismissed' });
  expect(f.client.getSnapshot().captureError).toBeNull();
  f.client.stop();
});
