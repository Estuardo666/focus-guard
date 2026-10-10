import { parseHostInitialization, type WebViewTransport } from './protocol';
import { decodeIcon, type AppIcon } from './applications';

export type RuleAction = 'Allow' | 'Block';
export type MatchKind = 'ExactPath' | 'Folder' | 'Sha256' | 'FileName';
export type RulePresentation = Readonly<{ name: string; path: string | null; icon: AppIcon | null; resolution: 'ExactMatch' | 'FileNameMatch' | 'Unresolved' | 'Ambiguous' | 'Folder' | 'Hash' }>;
export type ApplicationRule = Readonly<{ id: string; action: RuleAction; matchKind: MatchKind; value: string; presentation?: RulePresentation | null }>;
export type WebsiteRule = Readonly<{ id: string; action: RuleAction; pattern: string }>;
export type BreakApplicationRule = Readonly<{ id: string; matchKind: MatchKind; value: string }>;
export type BreakWebsiteRule = Readonly<{ id: string; pattern: string }>;
export type ProfileDetail = Readonly<{ id: string; name: string; description: string | null; accentHex: string | null; revision: string; applications: readonly ApplicationRule[]; applicationOffset: number; applicationTotal: number; websites: readonly WebsiteRule[]; websiteOffset: number; websiteTotal: number; editingEnabled: boolean; defaultCyclePresetId: string | null; defaultCyclePresetName: string | null; cycleSettingsRevision: string; breakApplications: readonly BreakApplicationRule[]; breakApplicationOffset: number; breakApplicationTotal: number; breakWebsites: readonly BreakWebsiteRule[]; breakWebsiteOffset: number; breakWebsiteTotal: number; cycleSettingsEditingEnabled: boolean }>;
type Mutation = Readonly<{ profileId: string | null; revision: string | null }>;
export type ProfileMethod = 'profile.create' | 'profile.rename' | 'profile.accent' | 'profile.duplicate' | 'profile.delete' | 'profile.application.add' | 'profile.application.addFromCatalog' | 'profile.application.remove' | 'profile.website.add' | 'profile.website.remove' | 'profile.break.application.add' | 'profile.break.application.remove' | 'profile.break.website.add' | 'profile.break.website.remove' | 'profile.cycle.defaultPreset.set';
export type ProfileSnapshot = Readonly<{ detail: ProfileDetail | null; phase: 'idle' | 'loading' | 'writing' | 'unknown' | 'error'; error: string | null }>;
type Pending = { id: string; token: string; write: boolean; resolve(value: unknown): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> };
const guid = /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const revision = /^[0-9A-F]{64}$/;
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0 && (value as number) <= 10000;
const action = (value: unknown) => value === 'Allow' || value === 'Block';
const text = (value: unknown, maximum: number): value is string => typeof value === 'string' && value.length > 0 && value.length <= maximum;
export function isRulePresentation(value: unknown): value is RulePresentation {
  if (!record(value) || !exact(value, ['name', 'path', 'icon', 'resolution']) || !text(value.name, 65536) || !['ExactMatch', 'FileNameMatch', 'Unresolved', 'Ambiguous', 'Folder', 'Hash'].includes(String(value.resolution))) return false;
  if (value.resolution === 'ExactMatch' || value.resolution === 'FileNameMatch')
    return value.name.length <= 160 && text(value.path, 1024) && /^[A-Za-z]:[\\/]/.test(value.path) && /\.exe$/i.test(value.path) && (value.icon === null || decodeIcon(value.icon) !== null);
  return value.path === null && value.icon === null;
}
export function isProfileDetail(value: unknown): value is ProfileDetail {
  if (!record(value) || !exact(value, ['id', 'name', 'description', 'accentHex', 'revision', 'applications', 'applicationOffset', 'applicationTotal', 'websites', 'websiteOffset', 'websiteTotal', 'editingEnabled', 'defaultCyclePresetId', 'defaultCyclePresetName', 'cycleSettingsRevision', 'breakApplications', 'breakApplicationOffset', 'breakApplicationTotal', 'breakWebsites', 'breakWebsiteOffset', 'breakWebsiteTotal', 'cycleSettingsEditingEnabled'])) return false;
  if (!text(value.id, 36) || !guid.test(value.id) || !text(value.name, 80) || !text(value.revision, 64) || !revision.test(value.revision)
    || !text(value.cycleSettingsRevision, 64) || !revision.test(value.cycleSettingsRevision)
    || typeof value.editingEnabled !== 'boolean' || typeof value.cycleSettingsEditingEnabled !== 'boolean'
    || !(value.defaultCyclePresetId === null || typeof value.defaultCyclePresetId === 'string' && guid.test(value.defaultCyclePresetId))
    || !(value.defaultCyclePresetName === null || text(value.defaultCyclePresetName, 80))
    || (value.defaultCyclePresetId === null) !== (value.defaultCyclePresetName === null)
    || !(value.description === null || typeof value.description === 'string' && value.description.length <= 65536)
    || !(value.accentHex === null || typeof value.accentHex === 'string' && /^#[0-9a-f]{6}$/i.test(value.accentHex))) return false;
  if (!count(value.applicationOffset) || !count(value.applicationTotal) || !count(value.websiteOffset) || !count(value.websiteTotal)
    || !count(value.breakApplicationOffset) || !count(value.breakApplicationTotal) || !count(value.breakWebsiteOffset) || !count(value.breakWebsiteTotal)
    || !Array.isArray(value.applications) || !Array.isArray(value.websites) || !Array.isArray(value.breakApplications) || !Array.isArray(value.breakWebsites)
    || value.applications.length > 6 || value.websites.length > 6 || value.breakApplications.length > 6 || value.breakWebsites.length > 6
    || value.applications.length > Math.max(0, value.applicationTotal - value.applicationOffset)
    || value.websites.length > Math.max(0, value.websiteTotal - value.websiteOffset)
    || value.breakApplications.length > Math.max(0, value.breakApplicationTotal - value.breakApplicationOffset)
    || value.breakWebsites.length > Math.max(0, value.breakWebsiteTotal - value.breakWebsiteOffset)) return false;
  const ids = new Set<string>();
  const validId = (id: unknown) => text(id, 36) && guid.test(id) && !ids.has(id.toLowerCase()) && !!ids.add(id.toLowerCase());
  const profileRulesValid = value.applications.every((rule: unknown) => record(rule) && (exact(rule, ['id', 'action', 'matchKind', 'value']) || exact(rule, ['id', 'action', 'matchKind', 'value', 'presentation'])) && (!Object.hasOwn(rule, 'presentation') || rule.presentation === null || isRulePresentation(rule.presentation)) && validId(rule.id) && action(rule.action) && ['ExactPath', 'Folder', 'Sha256', 'FileName'].includes(String(rule.matchKind)) && text(rule.value, 65536))
    && value.websites.every((rule: unknown) => record(rule) && exact(rule, ['id', 'action', 'pattern']) && validId(rule.id) && action(rule.action) && text(rule.pattern, 65536));
  const breakIds = new Set<string>();
  const validBreakId = (id: unknown) => text(id, 36) && guid.test(id) && !breakIds.has(id.toLowerCase()) && !!breakIds.add(id.toLowerCase());
  return profileRulesValid
    && value.breakApplications.every((rule: unknown) => record(rule) && exact(rule, ['id', 'matchKind', 'value']) && validBreakId(rule.id) && ['ExactPath', 'Folder', 'Sha256', 'FileName'].includes(String(rule.matchKind)) && text(rule.value, 1024))
    && value.breakWebsites.every((rule: unknown) => record(rule) && exact(rule, ['id', 'pattern']) && validBreakId(rule.id) && text(rule.pattern, 1024));
}
function isMutation(value: unknown): value is Mutation {
  return record(value) && exact(value, ['profileId', 'revision']) && (value.profileId === null && value.revision === null || typeof value.profileId === 'string' && guid.test(value.profileId) && typeof value.revision === 'string' && revision.test(value.revision));
}
export function createProfilesClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? (globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } }).chrome?.webview ?? null : provided;
  let active = false; let token: string | null = null; let pending: Pending | null = null;
  let snapshot: ProfileSnapshot = Object.freeze({ detail: null, phase: 'idle', error: null });
  const listeners = new Set<() => void>();
  const publish = (patch: Partial<ProfileSnapshot>) => { snapshot = Object.freeze({ ...snapshot, ...patch }); for (const listener of listeners) listener(); };
  const cancel = () => { if (pending) { const item = pending; pending = null; clearTimeout(item.timer); item.reject(new Error(item.write ? 'OutcomeUnknown' : 'DocumentChanged')); } };
  const request = (method: string, params: object, write: boolean): Promise<unknown> => {
    if (!active || !token || !transport) return Promise.reject(new Error('BridgeUnavailable'));
    if (pending) return Promise.reject(new Error('RequestInProgress'));
    const id = crypto.randomUUID().replaceAll('-', '');
    return new Promise((resolve, reject) => {
      const item: Pending = { id, token: token!, write, resolve, reject, timer: setTimeout(() => { if (pending === item) { pending = null; reject(new Error(write ? 'OutcomeUnknown' : 'RequestTimeout')); } }, 4000) };
      pending = item;
      try { transport.postMessage({ version: 1, requestId: id, documentToken: token, method, params }); }
      catch { pending = null; clearTimeout(item.timer); reject(new Error(write ? 'OutcomeUnknown' : 'BridgeUnavailable')); }
    });
  };
  const onMessage = (event: Event) => {
    const data: unknown = (event as MessageEvent<unknown>).data;
    if (!active) return;
    const init = parseHostInitialization(data);
    if (init) { if (token !== init.documentToken) { cancel(); token = init.documentToken; publish({ detail: null }); } return; }
    const item = pending;
    if (!item) return;
    let value: unknown;
    try { const json = typeof data === 'string' ? data : JSON.stringify(data); if (json === undefined || new TextEncoder().encode(json).length > 65536) return; value = typeof data === 'string' ? JSON.parse(json) : data; } catch { return; }
    if (!record(value) || !exact(value, ['version', 'requestId', 'documentToken', 'error', 'result']) || value.version !== 1 || value.requestId !== item.id || value.documentToken !== item.token || item.token !== token) return;
    if (typeof value.error === 'string' && /^[A-Za-z][A-Za-z0-9]{0,63}$/.test(value.error) && value.result === null) { pending = null; clearTimeout(item.timer); item.reject(new Error(value.error)); }
    else if (value.error === null && (item.write ? isMutation(value.result) : isProfileDetail(value.result))) { pending = null; clearTimeout(item.timer); item.resolve(value.result); }
  };
  async function load(id: string, applicationOffset = 0, websiteOffset = 0, breakApplicationOffset = 0, breakWebsiteOffset = 0): Promise<void> {
    if (snapshot.phase === 'writing' || !guid.test(id)) return;
    const uncertain = snapshot.phase === 'unknown';
    publish({ phase: 'loading', error: null });
    try {
      let result: unknown;
      for (let attempt = 0; ; attempt++) {
        try { result = await request('profile.get', { profileId: id.replaceAll('-', ''), applicationOffset, websiteOffset, breakApplicationOffset, breakWebsiteOffset }, false); break; }
        catch (error) { if (!(error instanceof Error) || error.message !== 'RequestInProgress' || attempt >= 3) throw error; await new Promise(resolve => setTimeout(resolve, 500)); }
      }
      if (!isProfileDetail(result) || result.id.toLowerCase() !== id.toLowerCase() || result.applicationOffset !== applicationOffset || result.websiteOffset !== websiteOffset || result.breakApplicationOffset !== breakApplicationOffset || result.breakWebsiteOffset !== breakWebsiteOffset) throw new Error('InvalidResponse');
      publish({ detail: result, phase: 'idle', error: null });
    } catch (error) { const code = error instanceof Error ? error.message : 'InvalidResponse'; publish({ phase: uncertain ? 'unknown' : 'error', error: code, ...(code === 'ProfileMissing' ? { detail: null } : {}) }); }
  }
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }, getSnapshot: () => snapshot,
    start() { if (active) return; active = true; transport?.addEventListener('message', onMessage); },
    stop() { active = false; token = null; transport?.removeEventListener('message', onMessage); cancel(); },
    clear() { if (snapshot.phase !== 'writing') publish(snapshot.phase === 'unknown' ? { detail: null } : { phase: 'idle', detail: null, error: null }); },
    load,
    async mutate(method: ProfileMethod, values: Record<string, unknown>): Promise<Mutation | null> {
      if (snapshot.phase === 'writing' || snapshot.phase === 'unknown' || snapshot.phase === 'loading') return null;
      const detail = snapshot.detail;
      const cycleSettingsMutation = method.startsWith('profile.break.') || method === 'profile.cycle.defaultPreset.set';
      if (method !== 'profile.create' && (cycleSettingsMutation ? !detail?.cycleSettingsEditingEnabled : !detail?.editingEnabled)) { publish({ phase: 'error', error: 'Unauthorized' }); return null; }
      const operationId = crypto.randomUUID().replaceAll('-', '');
      const params = method === 'profile.create' ? { ...values, operationId }
        : cycleSettingsMutation ? { ...values, profileId: detail!.id.replaceAll('-', ''), cycleSettingsRevision: detail!.cycleSettingsRevision, operationId }
          : { ...values, profileId: detail!.id.replaceAll('-', ''), revision: detail!.revision, operationId };
      publish({ phase: 'writing', error: null });
      try {
        const result = await request(method, params, true);
        if (!isMutation(result) || (method === 'profile.delete' ? result.profileId !== null : result.profileId === null)
            || !['profile.create', 'profile.duplicate', 'profile.delete'].includes(method) && result.profileId?.toLowerCase() !== detail?.id.toLowerCase()) throw new Error('OutcomeUnknown');
        publish({ phase: 'loading', error: null }); return result;
      } catch (error) { const code = error instanceof Error ? error.message : 'OutcomeUnknown'; publish({ phase: code === 'OutcomeUnknown' ? 'unknown' : 'error', error: code }); return null; }
    },
    acknowledgeReload() { if (snapshot.phase !== 'writing') publish({ phase: 'idle', error: null }); },
  };
}
export type ProfilesClient = ReturnType<typeof createProfilesClient>;
