import { parseHostInitialization, utcMilliseconds, type BridgeStatus, type WebViewTransport } from './protocol';
import { isCycleConfiguration, isCycleInput, isCyclePage, isCycleReview, isCyclePresetMutation, sameCycleConfiguration, type CycleConfiguration, type CycleInput, type CyclePage, type CycleReview, type CyclePresetMutation } from './cycles';
import { isScheduleList, isTodaySummary, type ScheduleList, type TodaySummary } from './workflows';
import { isCaptureActionResult, isSessionReflectionSaved, isSessionSummary, type CaptureActionResult, type SessionReflectionSaved, type SessionSummary } from './history';
import { isUserPreferences, type UserPreferences } from './preferences';
import { isSessionReplayIntent, type SessionReplayIntent } from './replay';
import type { RecoveryInfo, StrictReview } from './strict';

export type ProfileSummary = Readonly<{ id: string; name: string; applicationRuleCount: number; websiteRuleCount: number; blockedApplicationCount: number; blockedWebsiteCount: number; accentHex?: string | null; defaultCyclePresetId?: string | null; defaultCyclePresetName?: string | null; defaultCyclePresetConfiguration?: CycleConfiguration | null }>;
export type ProfilePage = Readonly<{ items: readonly ProfileSummary[]; offset: number; total: number; pilotEnabled: boolean }>;
export type NormalReview = Readonly<{ preparationId: string; profileId: string; profileName: string; durationSeconds: number; enforcementEnabled: boolean; applicationRuleCount: number; websiteRuleCount: number; blockedApplicationCount: number; blockedWebsiteCount: number; startUtc: string; endUtc: string; expiresUtc: string; extensionReady: boolean }>;
export type StartedSession = Readonly<{ sessionId: string; startUtc: string; endUtc: string }>;
export type ControlReview = Readonly<{ action: 'stop' | 'skip'; sessionId: string; endUtc: string; phaseIndex: number | null; phaseEndUtc: string | null }>;
type ControlledSession = Readonly<{ sessionId: string; action: 'Stopped' | 'BreakSkipped' }>;
type StrictCancelled = Readonly<{ cancelled: boolean }>;
type StrictCodeSaved = Readonly<{ saved: boolean }>;
export type DashboardSnapshot = Readonly<{ preferences: UserPreferences | null; preferencesLoading: boolean; preferencesError: string | null; preferencesPhase: 'idle' | 'writing' | 'unknown' | 'error'; sessionSummary: SessionSummary | null; summaryLoading: boolean; summaryError: string | null; sessionReplay: SessionReplayIntent | null; replayLoading: boolean; replayError: string | null; recoveryInfo: RecoveryInfo | null; recoveryInfoLoading: boolean; recoveryInfoError: string | null; strictReview: StrictReview | null; strictPhase: 'idle' | 'preparing' | 'review' | 'activating' | 'unknown' | 'started' | 'error'; strictError: string | null; capturePhase: 'idle' | 'writing' | 'unknown' | 'error'; captureError: string | null; schedulePhase: 'idle' | 'writing' | 'unknown' | 'error'; scheduleError: string | null; today: TodaySummary | null; todayLoading: boolean; todayError: string | null; schedules: ScheduleList | null; schedulesLoading: boolean; schedulesError: string | null; presetPhase: 'idle' | 'writing' | 'unknown' | 'error'; presetError: string | null; controlAction: 'stop' | 'skip' | null; controlReview: ControlReview | null; cycles: CyclePage | null; cyclesLoading: boolean; cyclesError: string | null; cycleReview: CycleReview | null; catalog: ProfilePage | null; catalogLoading: boolean; catalogError: string | null; phase: 'idle' | 'preparing' | 'review' | 'starting' | 'unknown' | 'started' | 'error' | 'actionReview' | 'controlling' | 'controlled'; review: NormalReview | null; started: StartedSession | null; error: string | null }>;
type Method = 'profiles.list' | 'session.prepareNormal' | 'session.startNormal' | 'cycles.list' | 'session.prepareCycle' | 'session.startCycle' | 'session.stopNormal' | 'session.skipBreak' | 'cyclePreset.save' | 'cyclePreset.delete' | 'cyclePreset.duplicate' | 'today.get' | 'schedules.list' | 'schedule.save' | 'schedule.enable' | 'schedule.delete' | 'session.summary' | 'session.intent.get' | 'session.reflection.save' | 'session.strict.prepare' | 'session.strict.activate' | 'session.strict.cancel' | 'session.strict.saveRecoveryCode' | 'recovery.info' | 'capture.open' | 'capture.dismiss' | 'preferences.get' | 'preferences.set';
type Result = ProfilePage | NormalReview | StartedSession | CyclePage | CycleReview | ControlledSession | CyclePresetMutation | TodaySummary | ScheduleList | SessionSummary | SessionReplayIntent | SessionReflectionSaved | StrictReview | StrictCancelled | StrictCodeSaved | RecoveryInfo | CaptureActionResult | UserPreferences;
type Pending = { id: string; token: string; method: Method; validate(value: unknown): value is Result; resolve(value: Result): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> };
const guidN = /^(?!0{32})[0-9a-f]{32}$/i;
const guidD = /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const integer = (v: unknown, maximum = 10000): v is number => Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= maximum;
const name = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 80;
const mutation = (method: Method) => method.startsWith('session.start') || method === 'session.stopNormal' || method === 'session.skipBreak'
  || method.startsWith('session.strict.') || method === 'session.reflection.save' || method.startsWith('cyclePreset.') || method.startsWith('schedule.')
  || method.startsWith('capture.') || method === 'preferences.set';
const isControlledSession = (v: unknown): v is ControlledSession => record(v) && exact(v, ['sessionId', 'action']) && typeof v.sessionId === 'string' && guidD.test(v.sessionId) && (v.action === 'Stopped' || v.action === 'BreakSkipped');
const isStrictCancelled = (v: unknown): v is StrictCancelled => record(v) && exact(v, ['cancelled']) && typeof v.cancelled === 'boolean';
const isStrictCodeSaved = (v: unknown): v is StrictCodeSaved => record(v) && exact(v, ['saved']) && typeof v.saved === 'boolean';
const terminalStates = ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'];
function utc(v: unknown): v is string {
  return typeof v === 'string' && utcMilliseconds(v) !== null;
}
const counts = (v: Record<string, unknown>) => integer(v.applicationRuleCount) && integer(v.websiteRuleCount) && integer(v.blockedApplicationCount) && integer(v.blockedWebsiteCount) && v.blockedApplicationCount <= v.applicationRuleCount && v.blockedWebsiteCount <= v.websiteRuleCount;
export function isProfilePage(v: unknown): v is ProfilePage {
  if (!record(v) || !exact(v, ['items', 'offset', 'total', 'pilotEnabled']) || !integer(v.offset) || !integer(v.total) || typeof v.pilotEnabled !== 'boolean' || !Array.isArray(v.items) || v.items.length > 8 || v.items.length > Math.max(0, v.total - v.offset)) return false;
  const ids = new Set<string>();
  return v.items.every((item: unknown) => {
    const base = ['id', 'name', 'applicationRuleCount', 'websiteRuleCount', 'blockedApplicationCount', 'blockedWebsiteCount'];
    const shapes = [
      base, [...base, 'accentHex'], [...base, 'defaultCyclePresetId', 'defaultCyclePresetName'],
      [...base, 'defaultCyclePresetId', 'defaultCyclePresetName', 'defaultCyclePresetConfiguration'],
      [...base, 'accentHex', 'defaultCyclePresetId', 'defaultCyclePresetName'],
      [...base, 'accentHex', 'defaultCyclePresetId', 'defaultCyclePresetName', 'defaultCyclePresetConfiguration'],
    ];
    if (!record(item) || !shapes.some(keys => exact(item, keys))) return false;
    if (typeof item.id !== 'string' || !guidD.test(item.id) || ids.has(item.id.toLowerCase())
      || !name(item.name) || !counts(item)) return false;
    ids.add(item.id.toLowerCase());
    if (Object.hasOwn(item, 'accentHex') && !(item.accentHex === null
      || typeof item.accentHex === 'string' && /^#[0-9a-f]{6}$/i.test(item.accentHex))) return false;
    if (Object.hasOwn(item, 'defaultCyclePresetId')) {
      if (item.defaultCyclePresetId === null) {
        if (item.defaultCyclePresetName !== null || Object.hasOwn(item, 'defaultCyclePresetConfiguration') && item.defaultCyclePresetConfiguration !== null) return false;
      } else if (typeof item.defaultCyclePresetId !== 'string' || !guidD.test(item.defaultCyclePresetId)
        || !name(item.defaultCyclePresetName)
        || Object.hasOwn(item, 'defaultCyclePresetConfiguration') && !isCycleConfiguration(item.defaultCyclePresetConfiguration)) return false;
    }
    return true;
  });
}
export function isNormalReview(v: unknown): v is NormalReview {
  if (!record(v) || !exact(v, ['preparationId', 'profileId', 'profileName', 'durationSeconds', 'enforcementEnabled', 'applicationRuleCount', 'websiteRuleCount', 'blockedApplicationCount', 'blockedWebsiteCount', 'startUtc', 'endUtc', 'expiresUtc', 'extensionReady'])) return false;
  return typeof v.preparationId === 'string' && guidN.test(v.preparationId) && typeof v.profileId === 'string' && guidD.test(v.profileId) && name(v.profileName) && integer(v.durationSeconds, 43200) && v.durationSeconds >= 60 && v.durationSeconds % 60 === 0 && typeof v.enforcementEnabled === 'boolean' && typeof v.extensionReady === 'boolean' && counts(v) && utc(v.startUtc) && utc(v.endUtc) && utc(v.expiresUtc) && Date.parse(v.endUtc) - Date.parse(v.startUtc) === v.durationSeconds * 1000 && Date.parse(v.expiresUtc) > Date.parse(v.startUtc) && Date.parse(v.expiresUtc) - Date.parse(v.startUtc) <= 120000;
}
export function isStartedSession(v: unknown): v is StartedSession {
  return record(v) && exact(v, ['sessionId', 'startUtc', 'endUtc']) && typeof v.sessionId === 'string' && guidD.test(v.sessionId) && utc(v.startUtc) && utc(v.endUtc) && Date.parse(v.endUtc) > Date.parse(v.startUtc);
}
export function createDashboardClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? (globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } }).chrome?.webview ?? null : provided;
  const listeners = new Set<() => void>();
  let snapshot: DashboardSnapshot = Object.freeze({ preferences: null, preferencesLoading: false, preferencesError: null, preferencesPhase: 'idle', sessionSummary: null, summaryLoading: false, summaryError: null, sessionReplay: null, replayLoading: false, replayError: null, recoveryInfo: null, recoveryInfoLoading: false, recoveryInfoError: null, strictReview: null, strictPhase: 'idle', strictError: null, capturePhase: 'idle', captureError: null, schedulePhase: 'idle', scheduleError: null, today: null, todayLoading: false, todayError: null, schedules: null, schedulesLoading: false, schedulesError: null, presetPhase: 'idle', presetError: null, controlAction: null, controlReview: null, cycles: null, cyclesLoading: false, cyclesError: null, cycleReview: null, catalog: null, catalogLoading: false, catalogError: null, phase: 'idle', review: null, started: null, error: null });
  let active = false;
  let token: string | null = null;
  let pending: Pending | null = null;
  let latestStatus: BridgeStatus | null = null;
  let uncertainStatus: BridgeStatus | null = null;
  let startingStatus: BridgeStatus | null = null;
  let controlStatus: BridgeStatus | null = null;
  let strictStartingStatus: BridgeStatus | null = null;
  let statusReady = false;
  const canControl = (status: BridgeStatus | null) => statusReady && !!snapshot.catalog?.pilotEnabled && !!status && !status.strictMode && ['Active', 'Activating'].includes(status.sessionState) && !!status.sessionId && !!status.endUtc;
  const matchesControl = (review: ControlReview, status: BridgeStatus | null) => canControl(status) && status!.sessionId === review.sessionId && status!.endUtc === review.endUtc && (review.action === 'stop' || status!.mode === 'FocusCycle' && status!.currentPhase?.index === review.phaseIndex && status!.currentPhase.endUtc === review.phaseEndUtc && ['ShortBreak', 'LongBreak'].includes(status!.currentPhase.kind));
  let readRetry: ReturnType<typeof setTimeout> | null = null;
  const publish = (patch: Partial<DashboardSnapshot>) => { snapshot = Object.freeze({ ...snapshot, ...patch }); for (const listener of listeners) listener(); };
  const cancelPending = (code: string) => { if (pending) { const p = pending; pending = null; clearTimeout(p.timer); p.reject(new Error(code)); } };
  const request = (method: Method, params: object, validate: Pending['validate']): Promise<Result> => {
    if (!active || !token || !transport) return Promise.reject(new Error('BridgeUnavailable'));
    if (pending) return Promise.reject(new Error('RequestInProgress'));
    const id = crypto.randomUUID().replaceAll('-', '');
    return new Promise((resolve, reject) => {
      const p: Pending = { id, token: token!, method, validate, resolve, reject, timer: setTimeout(() => { if (pending === p) { pending = null; reject(new Error(mutation(method) ? 'OutcomeUnknown' : 'RequestTimeout')); } }, 4000) };
      pending = p;
      try { transport.postMessage({ version: 1, requestId: id, documentToken: token, method, params }); }
      catch { pending = null; clearTimeout(p.timer); reject(new Error(mutation(method) ? 'OutcomeUnknown' : 'BridgeUnavailable')); }
    });
  };
  const cancelStrictReviewRequest = (preparationId: string) => {
    if (!active) return;
    void request('session.strict.cancel', { preparationId: preparationId.replaceAll('-', '') }, isStrictCancelled).catch(() => undefined);
  };
  const loadSessionSummary = async (sessionId: string, retry = 0): Promise<SessionSummary | null> => {
    publish({ summaryLoading: true, summaryError: null });
    try {
      const result = await request('session.summary', { sessionId: sessionId.replaceAll('-', '') }, isSessionSummary) as SessionSummary;
      if (result.sessionId.toLowerCase() !== sessionId.toLowerCase()) throw new Error('InvalidResponse');
      publish({ sessionSummary: result, summaryLoading: false, capturePhase: snapshot.capturePhase === 'unknown' ? 'idle' : snapshot.capturePhase, captureError: null });
      return result;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'InvalidResponse';
      if (active && code === 'RequestInProgress' && retry < 3) {
        await new Promise(resolve => setTimeout(resolve, 300));
        if (active) return loadSessionSummary(sessionId, retry + 1);
      }
      publish({ summaryLoading: false, summaryError: code });
      return null;
    }
  };
  const loadSessionIntent = async (sessionId: string, retry = 0): Promise<SessionReplayIntent | null> => {
    publish({ replayLoading: true, replayError: null });
    try {
      const result = await request('session.intent.get', { sessionId: sessionId.replaceAll('-', '') }, isSessionReplayIntent) as SessionReplayIntent;
      if (result.sessionId.toLowerCase() !== sessionId.toLowerCase()) throw new Error('InvalidResponse');
      publish({ sessionReplay: result, replayLoading: false, replayError: null });
      return result;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'InvalidResponse';
      if (active && code === 'RequestInProgress' && retry < 3) {
        await new Promise(resolve => setTimeout(resolve, 300));
        if (active) return loadSessionIntent(sessionId, retry + 1);
      }
      publish({ replayLoading: false, replayError: code });
      return null;
    }
  };
  const prepareStrict = async (profileId: string, mode: 'SimpleTimer' | 'FocusCycle', durationSeconds: number,
    cycleInput: CycleInput | null, enforcementEnabled: boolean): Promise<StrictReview | null> => {
    if (['preparing', 'activating', 'unknown', 'started'].includes(snapshot.strictPhase)
      || !statusReady || !latestStatus || !snapshot.catalog?.pilotEnabled || !terminalStates.includes(latestStatus.sessionState)
      || !guidD.test(profileId) || mode === 'SimpleTimer' && (!Number.isInteger(durationSeconds) || durationSeconds < 60 || durationSeconds > 43200 || durationSeconds % 60 !== 0)
      || mode === 'FocusCycle' && (!cycleInput || !isCycleInput(cycleInput))) {
      publish({ strictPhase: 'error', strictReview: null, strictError: 'StrictReviewUnavailable' });
      return null;
    }
    const params = {
      profileId: profileId.replaceAll('-', ''), mode, durationSeconds: mode === 'SimpleTimer' ? durationSeconds : 0,
      presetId: mode === 'FocusCycle' ? cycleInput!.presetId?.replaceAll('-', '') ?? null : null,
      configuration: mode === 'FocusCycle' ? cycleInput!.configuration : null, enforcementEnabled,
    };
    publish({ strictPhase: 'preparing', strictReview: null, strictError: null });
    try {
      const { isStrictReview } = await import('./strict');
      const review = await request('session.strict.prepare', params, isStrictReview) as StrictReview;
      const mismatched = review.profileId.toLowerCase() !== profileId.toLowerCase() || review.mode !== mode
        || review.enforcementEnabled !== enforcementEnabled
        || mode === 'SimpleTimer' && review.durationSeconds !== durationSeconds
        || mode === 'FocusCycle' && cycleInput!.configuration !== null
          && (review.configuration === null || !sameCycleConfiguration(review.configuration, cycleInput!.configuration))
        || mode === 'FocusCycle' && cycleInput!.presetId !== null
          && !snapshot.cycles?.items.some(preset => preset.id.toLowerCase() === cycleInput!.presetId!.toLowerCase()
            && review.configuration !== null && sameCycleConfiguration(preset.configuration, review.configuration));
      if (mismatched) {
        cancelStrictReviewRequest(review.preparationId);
        throw new Error('InvalidResponse');
      }
      publish({ strictReview: review, strictPhase: 'review', strictError: null });
      return review;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'OutcomeUnknown';
      const unknown = ['OutcomeUnknown', 'InvalidResponse', 'DocumentChanged'].includes(code);
      publish({ strictPhase: unknown ? 'unknown' : 'error', strictReview: null, strictError: code });
      return null;
    }
  };
  const cancelStrictReview = () => {
    const review = snapshot.strictReview;
    publish({ strictPhase: 'idle', strictReview: null, strictError: null });
    if (review) cancelStrictReviewRequest(review.preparationId);
  };
  const saveStrictRecoveryCode = async (): Promise<boolean> => {
    const review = snapshot.strictReview;
    if (snapshot.strictPhase !== 'review' || !review || pending) return false;
    publish({ strictError: null });
    try {
      const result = await request('session.strict.saveRecoveryCode',
        { preparationId: review.preparationId, confirmed: true }, isStrictCodeSaved) as StrictCodeSaved;
      if (!result.saved) { publish({ strictError: 'RecoverySaveCancelled' }); return false; }
      publish({ strictError: null });
      return true;
    } catch (error) {
      publish({ strictError: error instanceof Error ? error.message : 'RecoverySaveFailed' });
      return false;
    }
  };
  const confirmStrict = async (typedConfirmation: string, recoverySaved: boolean, unsavedWorkConfirmed: boolean, objective = ''): Promise<StartedSession | null> => {
    const review = snapshot.strictReview;
    if (snapshot.strictPhase !== 'review' || !review || !recoverySaved || !unsavedWorkConfirmed
      || !statusReady || !latestStatus || !snapshot.catalog?.pilotEnabled || !terminalStates.includes(latestStatus.sessionState)) return null;
    strictStartingStatus = latestStatus;
    publish({ strictPhase: 'activating', strictReview: null, strictError: null });
    try {
      const started = await request('session.strict.activate', {
        preparationId: review.preparationId, operationId: crypto.randomUUID().replaceAll('-', ''),
        typedConfirmation, recoverySaved, unsavedWorkConfirmed, confirmed: true, objective: objective.slice(0, 240).trim(),
      }, isStartedSession) as StartedSession;
      if (Date.parse(started.startUtc) !== Date.parse(review.startUtc) || Date.parse(started.endUtc) !== Date.parse(review.endUtc))
        throw new Error('OutcomeUnknown');
      publish({ strictPhase: 'started', strictError: null });
      return started;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'OutcomeUnknown';
      if (code === 'RequestInProgress') publish({ strictPhase: 'review', strictReview: review, strictError: code });
      else publish({ strictPhase: ['OutcomeUnknown', 'InvalidResponse', 'DocumentChanged'].includes(code) ? 'unknown' : 'error', strictReview: null, strictError: code });
      return null;
    }
  };
  const loadRecoveryInfo = async (retry = 0): Promise<RecoveryInfo | null> => {
    publish({ recoveryInfoLoading: true, recoveryInfoError: null });
    try {
      const { isRecoveryInfo } = await import('./strict');
      const info = await request('recovery.info', {}, isRecoveryInfo) as RecoveryInfo;
      publish({ recoveryInfo: info, recoveryInfoLoading: false, recoveryInfoError: null });
      return info;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'InvalidResponse';
      if (active && code === 'RequestInProgress' && retry < 3) {
        await new Promise(resolve => setTimeout(resolve, 300));
        if (active) return loadRecoveryInfo(retry + 1);
      }
      publish({ recoveryInfoLoading: false, recoveryInfoError: code });
      return null;
    }
  };
  const loadProfiles = async (offset = 0, retry = 0): Promise<void> => {
    publish({ catalogLoading: true, catalogError: null });
    try {
      const page = await request('profiles.list', { offset }, isProfilePage) as ProfilePage;
      if (page.offset !== offset) throw new Error('InvalidResponse');
      publish({ catalog: page, catalogLoading: false });
    } catch (error) {
      const code = error instanceof Error ? error.message : 'InvalidResponse';
      if (active && code === 'RequestInProgress' && retry < 3) {
        readRetry = setTimeout(() => { readRetry = null; void loadProfiles(offset, retry + 1); }, 500);
      } else publish({ catalogLoading: false, catalogError: code });
    }
  };
  const onMessage = (event: Event) => {
    if (!active) return;
    const data: unknown = (event as MessageEvent<unknown>).data;
    const init = parseHostInitialization(data);
    if (init) {
      if (token === init.documentToken) return;
      cancelPending('DocumentChanged');
      token = init.documentToken;
      statusReady = false;
      publish({ presetPhase: 'idle', presetError: null, sessionSummary: null, summaryError: null, sessionReplay: null, replayError: null, strictReview: null, strictPhase: 'idle', strictError: null, capturePhase: 'idle', captureError: null, review: null, controlReview: null, cycleReview: null, cycles: null, today: null, todayError: null, schedules: null, schedulesError: null, catalog: null });
      void loadProfiles();
      return;
    }
    const p = pending;
    if (!p) return;
    let value: unknown;
    try { const text = typeof data === 'string' ? data : JSON.stringify(data); if (text === undefined || new TextEncoder().encode(text).length > (p.method === 'session.prepareCycle' || p.method === 'today.get' || p.method === 'session.summary' ? 65536 : 8192)) return; value = typeof data === 'string' ? JSON.parse(data) : data; } catch { return; }
    if (!record(value) || !exact(value, ['version', 'requestId', 'documentToken', 'error', 'result']) || value.version !== 1 || value.requestId !== p.id || value.documentToken !== p.token || p.token !== token) return;
    if (typeof value.error === 'string' && /^[A-Za-z][A-Za-z0-9]{0,63}$/.test(value.error) && value.result === null) {
      pending = null; clearTimeout(p.timer); p.reject(new Error(value.error));
    } else if (value.error === null && p.validate(value.result)) {
      pending = null; clearTimeout(p.timer); p.resolve(value.result);
    }
  };
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot,
    start() { if (active) return; active = true; transport?.addEventListener('message', onMessage); if (!transport) publish({ catalogError: 'BridgeUnavailable' }); },
    stop() { active = false; token = null; statusReady = false; if (readRetry) clearTimeout(readRetry); readRetry = null; transport?.removeEventListener('message', onMessage); cancelPending('DocumentChanged'); publish({ review: null, controlReview: null, cycleReview: null, strictReview: null, strictPhase: 'idle' }); },
    loadProfiles,
    loadSessionIntent,
    prepareStrict,
    confirmStrict,
    cancelStrictReview,
    saveStrictRecoveryCode,
    loadRecoveryInfo,
    async loadCycles(offset = 0) {
      publish({ cyclesLoading: true, cyclesError: null });
      try {
        const page = await request('cycles.list', { offset }, isCyclePage) as CyclePage;
        if (page.offset !== offset) throw new Error('InvalidResponse');
        if (snapshot.presetPhase === 'unknown' || snapshot.presetPhase === 'error') publish({ presetPhase: 'idle', presetError: null });
        publish({ cycles: page, cyclesLoading: false });
      } catch (error) { publish({ cyclesLoading: false, cyclesError: error instanceof Error ? error.message : 'InvalidResponse' }); }
    },
    async loadToday(startUtc: string, endUtc: string) {
      publish({ today: null, todayLoading: true, todayError: null });
      try {
        const result = await request('today.get', { startUtc, endUtc }, isTodaySummary) as TodaySummary;
        if (Date.parse(endUtc) <= Date.parse(startUtc) || result.timeline.some(span => Date.parse(span.startUtc) < Date.parse(startUtc) || Date.parse(span.endUtc) > Date.parse(endUtc))) throw new Error('InvalidResponse');
        publish({ today: result, todayLoading: false });
      } catch (error) { publish({ todayLoading: false, todayError: error instanceof Error ? error.message : 'InvalidResponse' }); }
    },
    async loadSchedules() {
      publish({ schedulesLoading: true, schedulesError: null });
      try { const result = await request('schedules.list', {}, isScheduleList) as ScheduleList; publish({ schedules: result, schedulesLoading: false, schedulePhase: ['unknown', 'error'].includes(snapshot.schedulePhase) ? 'idle' : snapshot.schedulePhase, scheduleError: null }); }
      catch (error) { publish({ schedulesLoading: false, schedulesError: error instanceof Error ? error.message : 'InvalidResponse' }); }
    },
    async loadPreferences() {
      publish({ preferencesLoading: true, preferencesError: null });
      // App asks for preferences on mount, before host.initialize has delivered the document token (BridgeUnavailable)
      // and while startup status/profile reads may still own the single request slot (RequestInProgress).
      for (let retry = 0; ; retry++) {
        try {
          const value = await request('preferences.get', {}, isUserPreferences) as UserPreferences;
          publish({ preferences: value, preferencesLoading: false, preferencesPhase: 'idle', preferencesError: null });
          return;
        } catch (error) {
          const code = error instanceof Error ? error.message : 'InvalidResponse';
          if (active && (code === 'RequestInProgress' || code === 'BridgeUnavailable') && retry < 10) {
            await new Promise<void>(resolve => setTimeout(resolve, 300));
            if (active) continue;
            return;
          }
          // A failed read is not a failed save: leave an unknown save outcome alone, clear a stale save error.
          publish({ preferencesLoading: false, preferencesError: code, preferencesPhase: snapshot.preferencesPhase === 'error' ? 'idle' : snapshot.preferencesPhase });
          return;
        }
      }
    },
    async savePreferences(value: UserPreferences): Promise<UserPreferences | null> {
      if (pending || !active) { publish({ preferencesPhase: 'error', preferencesError: 'PreferencesUnavailable' }); return null; }
      publish({ preferencesPhase: 'writing', preferencesError: null });
      try {
        const saved = await request('preferences.set', value, isUserPreferences) as UserPreferences;
        if (JSON.stringify(saved) !== JSON.stringify(value)) throw new Error('OutcomeUnknown');
        publish({ preferences: saved, preferencesPhase: 'idle', preferencesError: null });
        return saved;
      } catch (error) {
        const code = error instanceof Error ? error.message : 'OutcomeUnknown';
        publish({ preferencesPhase: code === 'OutcomeUnknown' ? 'unknown' : 'error', preferencesError: code });
        return null;
      }
    },
    loadSessionSummary,
    async saveSessionReflection(sessionId: string, reflection: string): Promise<string | null> {
      const current = snapshot.sessionSummary;
      if (!current || current.sessionId.toLowerCase() !== sessionId.toLowerCase()
        || reflection.length > 1000 || pending) return null;
      try {
        const saved = await request('session.reflection.save', {
          sessionId: sessionId.replaceAll('-', ''), reflection, confirmed: true,
        }, isSessionReflectionSaved) as SessionReflectionSaved;
        if (saved.sessionId.toLowerCase() !== sessionId.toLowerCase()) throw new Error('InvalidResponse');
        const summary = snapshot.sessionSummary;
        if (summary?.sessionId.toLowerCase() === sessionId.toLowerCase())
          publish({ sessionSummary: Object.freeze({ ...summary, reflection: saved.reflection }) });
        return saved.reflection;
      } catch {
        return null;
      }
    },
    async resolveCapture(sessionId: string, captureId: string, action: 'open' | 'dismiss') {
      const summary = snapshot.sessionSummary;
      if (pending || snapshot.capturePhase === 'writing' || snapshot.capturePhase === 'unknown' || !statusReady
        || !snapshot.catalog?.pilotEnabled || !latestStatus || latestStatus.strictMode
        || !['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(latestStatus.sessionState)
        || !summary || summary.sessionId.toLowerCase() !== sessionId.toLowerCase()
        || !summary.captures.some(capture => capture.id.toLowerCase() === captureId.toLowerCase() && capture.resolution === 'Pending' && (action !== 'open' || capture.openable))) {
        publish({ capturePhase: 'error', captureError: 'CaptureUnavailable' }); return null;
      }
      publish({ capturePhase: 'writing', captureError: null });
      try {
        const result = await request(action === 'open' ? 'capture.open' : 'capture.dismiss', {
          sessionId: sessionId.replaceAll('-', ''), captureId: captureId.replaceAll('-', ''),
          operationId: crypto.randomUUID().replaceAll('-', ''), confirmed: true,
        }, isCaptureActionResult) as CaptureActionResult;
        if (result.sessionId.toLowerCase() !== sessionId.toLowerCase() || result.captureId.toLowerCase() !== captureId.toLowerCase()
          || result.action !== (action === 'open' ? 'Opened' : 'Dismissed')) throw new Error('OutcomeUnknown');
        publish({ capturePhase: 'idle', captureError: null });
        await this.loadSessionSummary(sessionId);
        return result;
      } catch (error) {
        const code = error instanceof Error ? error.message : 'OutcomeUnknown';
        publish({ capturePhase: code === 'OutcomeUnknown' ? 'unknown' : 'error', captureError: code });
        return null;
      }
    },
    async saveSchedule(input: { scheduleId: string; expectedRevision: string | null; name: string; days: number; startMinute: number; timeZoneId: string; profileId: string; durationMinutes: number; enforcementEnabled: boolean; enabled: boolean }) {
      return mutateSchedule('schedule.save', { ...input, scheduleId: input.scheduleId.replaceAll('-', ''), profileId: input.profileId.replaceAll('-', '') });
    },
    async setScheduleEnabled(scheduleId: string, revision: string, enabled: boolean) {
      return mutateSchedule('schedule.enable', { scheduleId: scheduleId.replaceAll('-', ''), revision, enabled });
    },
    async deleteSchedule(scheduleId: string, revision: string) {
      return mutateSchedule('schedule.delete', { scheduleId: scheduleId.replaceAll('-', ''), revision });
    },
    async saveCyclePreset(presetId: string, revision: string | null, name: string, configuration: CycleConfiguration): Promise<CyclePresetMutation | null> {
      return mutateCyclePreset('cyclePreset.save', { presetId: presetId.replaceAll('-', ''), revision, name, configuration });
    },
    async duplicateCyclePreset(sourcePresetId: string, revision: string, name: string): Promise<CyclePresetMutation | null> {
      return mutateCyclePreset('cyclePreset.duplicate', { sourcePresetId: sourcePresetId.replaceAll('-', ''), revision, name });
    },
    async deleteCyclePreset(presetId: string, revision: string): Promise<CyclePresetMutation | null> {
      return mutateCyclePreset('cyclePreset.delete', { presetId: presetId.replaceAll('-', ''), revision });
    },
    async prepareCycle(profileId: string, input: CycleInput, enforcementEnabled: boolean) {
      if (['unknown', 'starting', 'preparing', 'started', 'controlling', 'controlled', 'actionReview'].includes(snapshot.phase)) return;
      if (!snapshot.catalog?.pilotEnabled || !guidD.test(profileId) || !isCycleInput(input)) { publish({ phase: 'error', error: 'InvalidInput' }); return; }
      publish({ phase: 'preparing', review: null, cycleReview: null, started: null, error: null });
      try {
        const review = await request('session.prepareCycle', { profileId: profileId.replaceAll('-', ''), presetId: input.presetId?.replaceAll('-', '') ?? null, configuration: input.configuration, enforcementEnabled }, isCycleReview) as CycleReview;
        const profileDefault = input.presetId
          ? snapshot.catalog?.items.find(item => item.id.toLowerCase() === profileId.toLowerCase())
          : undefined;
        const presetConfigurationKnown = !!input.configuration
          ? true
          : !!input.presetId && (!!snapshot.cycles?.items.some(preset => preset.id.toLowerCase() === input.presetId!.toLowerCase()
              && sameCycleConfiguration(preset.configuration, review.configuration))
            || profileDefault?.defaultCyclePresetId?.toLowerCase() === input.presetId.toLowerCase()
              && !!profileDefault.defaultCyclePresetConfiguration
              && sameCycleConfiguration(profileDefault.defaultCyclePresetConfiguration, review.configuration));
        if (review.profileId.toLowerCase() !== profileId.toLowerCase() || review.enforcementEnabled !== enforcementEnabled
          || input.configuration && !sameCycleConfiguration(review.configuration, input.configuration)
          || input.presetId && !presetConfigurationKnown) throw new Error('InvalidResponse');
        publish({ phase: 'review', cycleReview: review });
      } catch (error) { publish({ phase: 'error', error: error instanceof Error ? error.message : 'InvalidResponse', cycleReview: null }); }
    },
    async prepare(profileId: string, minutes: number, enforcementEnabled: boolean) {
      if (['unknown', 'starting', 'preparing', 'started', 'controlling', 'controlled', 'actionReview'].includes(snapshot.phase)) return;
      if (!snapshot.catalog?.pilotEnabled || !Number.isInteger(minutes) || minutes < 1 || minutes > 720 || !guidD.test(profileId)) { publish({ phase: 'error', error: 'InvalidInput' }); return; }
      publish({ phase: 'preparing', review: null, cycleReview: null, started: null, error: null });
      try {
        const review = await request('session.prepareNormal', { profileId: profileId.replaceAll('-', ''), durationSeconds: minutes * 60, enforcementEnabled }, isNormalReview) as NormalReview;
        if (review.profileId.toLowerCase() !== profileId.toLowerCase() || review.durationSeconds !== minutes * 60 || review.enforcementEnabled !== enforcementEnabled) throw new Error('InvalidResponse');
        publish({ phase: 'review', review });
      } catch (error) { publish({ phase: 'error', error: error instanceof Error ? error.message : 'InvalidResponse', review: null }); }
    },
    expireReview(preparationId: string) {
      const review = snapshot.review ?? snapshot.cycleReview;
      if (snapshot.phase !== 'review' || !review || review.preparationId !== preparationId
        || Date.parse(review.expiresUtc) > Date.now()) return;
      publish({ phase: 'error', review: null, cycleReview: null, error: 'PreparationUnavailable' });
    },
    cancelReview() { if (snapshot.phase === 'review' || snapshot.phase === 'actionReview') publish({ phase: 'idle', controlAction: null, controlReview: null, review: null, cycleReview: null, error: null }); },
    observeStatus(status: BridgeStatus | null, ready: boolean) {
      statusReady = ready;
      if (!ready) {
        if (snapshot.phase === 'review' || snapshot.phase === 'actionReview') publish({ phase: 'idle', controlAction: null, controlReview: null, review: null, cycleReview: null });
        if (snapshot.strictReview) cancelStrictReview();
        return;
      }
      latestStatus = status;
      if (snapshot.phase === 'unknown' && status && status !== uncertainStatus) publish({ phase: 'idle', controlAction: null, error: null });
      if (snapshot.phase === 'started' && status && status !== startingStatus) publish({ phase: 'idle', error: null });
      if (snapshot.strictPhase === 'unknown' && status && status !== strictStartingStatus) publish({ strictPhase: 'idle', strictError: null });
      if (snapshot.strictPhase === 'started' && status && status !== strictStartingStatus) publish({ strictPhase: 'idle', strictError: null });
      if (snapshot.strictReview && status && !terminalStates.includes(status.sessionState)) cancelStrictReview();
      if (snapshot.phase === 'controlled' && status && status !== controlStatus) publish({ phase: 'idle', controlAction: null, error: null });
      if (snapshot.phase === 'error' && snapshot.controlAction && status && status !== controlStatus) publish({ phase: 'idle', controlAction: null, error: null });
      if (snapshot.controlReview && !matchesControl(snapshot.controlReview, status)) publish({ phase: 'idle', controlAction: null, controlReview: null, error: null });
      if (snapshot.phase === 'review' && status && !['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState)) publish({ phase: 'idle', review: null, cycleReview: null });
    },
    reviewControl(action: 'stop' | 'skip') {
      if (!['idle', 'error'].includes(snapshot.phase) || snapshot.phase === 'error' && snapshot.controlAction || !canControl(latestStatus)) return;
      const status = latestStatus!;
      if (action === 'skip' && (status.mode !== 'FocusCycle' || !status.currentPhase || !['ShortBreak', 'LongBreak'].includes(status.currentPhase.kind))) return;
      publish({ phase: 'actionReview', controlAction: action, error: null, controlReview: Object.freeze({ action, sessionId: status.sessionId!, endUtc: status.endUtc!, phaseIndex: action === 'skip' ? status.currentPhase!.index : null, phaseEndUtc: action === 'skip' ? status.currentPhase!.endUtc : null }) });
    },
    async confirmControl() {
      const review = snapshot.controlReview;
      if (snapshot.phase !== 'actionReview' || !review || !matchesControl(review, latestStatus)) return;
      publish({ phase: 'controlling', controlReview: null, error: null });
      try {
        const params = { sessionId: review.sessionId.replaceAll('-', ''), operationId: crypto.randomUUID().replaceAll('-', ''), confirmed: true, ...(review.action === 'skip' ? { phaseIndex: review.phaseIndex } : {}) };
        const result = await request(review.action === 'stop' ? 'session.stopNormal' : 'session.skipBreak', params, isControlledSession) as ControlledSession;
        if (result.sessionId.toLowerCase() !== review.sessionId.toLowerCase() || result.action !== (review.action === 'stop' ? 'Stopped' : 'BreakSkipped')) throw new Error('OutcomeUnknown');
        controlStatus = latestStatus;
        publish({ phase: 'controlled' });
      } catch (error) {
        const code = error instanceof Error ? error.message : 'OutcomeUnknown';
        const unknown = ['OutcomeUnknown', 'DocumentChanged', 'InvalidResponse'].includes(code);
        if (unknown) uncertainStatus = latestStatus;
        controlStatus = latestStatus;
        publish({ phase: unknown ? 'unknown' : 'error', error: code });
      }
    },
    async confirm(objective = '') {
      const review = snapshot.review ?? snapshot.cycleReview;
      if (snapshot.phase !== 'review' || !review) return;
      if (Date.parse(review.expiresUtc) <= Date.now()) {
        publish({ phase: 'error', review: null, cycleReview: null, error: 'PreparationUnavailable' });
        return;
      }
      const method = snapshot.cycleReview ? 'session.startCycle' : 'session.startNormal';
      startingStatus = latestStatus;
      publish({ phase: 'starting', review: null, cycleReview: null, error: null });
      try {
        const started = await request(method, { preparationId: review.preparationId,
          operationId: crypto.randomUUID().replaceAll('-', ''), objective: objective.slice(0, 240).trim() }, isStartedSession) as StartedSession;
        if (Date.parse(started.startUtc) !== Date.parse(review.startUtc) || Date.parse(started.endUtc) !== Date.parse(review.endUtc)) throw new Error('OutcomeUnknown');
        publish({ phase: 'started', started });
      } catch (error) {
        const code = error instanceof Error ? error.message : 'OutcomeUnknown';
        const unknown = ['OutcomeUnknown', 'DocumentChanged', 'InvalidResponse'].includes(code);
        if (unknown) uncertainStatus = latestStatus;
        publish({ phase: unknown ? 'unknown' : 'error', error: code });
      }
    },
  };

  async function mutateCyclePreset(method: 'cyclePreset.save' | 'cyclePreset.duplicate' | 'cyclePreset.delete', params: object): Promise<CyclePresetMutation | null> {
    if (pending || snapshot.presetPhase === 'writing' || snapshot.presetPhase === 'unknown' || !active || !statusReady
      || !snapshot.catalog?.pilotEnabled || !latestStatus || !['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(latestStatus.sessionState)) {
      publish({ presetPhase: 'error', presetError: 'PresetEditorUnavailable' }); return null;
    }
    publish({ presetPhase: 'writing', presetError: null });
    try {
      const result = await request(method, { ...params, operationId: crypto.randomUUID().replaceAll('-', '') }, isCyclePresetMutation) as CyclePresetMutation;
      if (method === 'cyclePreset.delete' ? result.action !== 'Deleted' : result.action !== (method === 'cyclePreset.duplicate' ? 'Duplicated' : 'Saved')) throw new Error('OutcomeUnknown');
      publish({ presetPhase: 'idle', presetError: null });
      return result;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'OutcomeUnknown';
      publish({ presetPhase: code === 'OutcomeUnknown' ? 'unknown' : 'error', presetError: code });
      return null;
    }
  }

  async function mutateSchedule(method: 'schedule.save' | 'schedule.enable' | 'schedule.delete', params: object): Promise<ScheduleList | null> {
    if (pending || snapshot.schedulePhase === 'writing' || snapshot.schedulePhase === 'unknown' || !active || !statusReady
      || !snapshot.catalog?.pilotEnabled || snapshot.schedules?.editingEnabled !== true || !latestStatus
      || !['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(latestStatus.sessionState)) {
      publish({ schedulePhase: 'error', scheduleError: 'ScheduleEditorUnavailable' }); return null;
    }
    publish({ schedulePhase: 'writing', scheduleError: null });
    try {
      const result = await request(method, { ...params, operationId: crypto.randomUUID().replaceAll('-', '') }, isScheduleList) as ScheduleList;
      if (result.editingEnabled !== true) throw new Error('OutcomeUnknown');
      publish({ schedules: result, schedulesLoading: false, schedulePhase: 'idle', scheduleError: null });
      return result;
    } catch (error) {
      const code = error instanceof Error ? error.message : 'OutcomeUnknown';
      publish({ schedulePhase: code === 'OutcomeUnknown' ? 'unknown' : 'error', scheduleError: code });
      return null;
    }
  }
}
export type DashboardClient = ReturnType<typeof createDashboardClient>;
