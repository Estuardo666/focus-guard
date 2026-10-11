import { BRIDGE_PROTOCOL_VERSION, parseHostInitialization, utcMilliseconds, type WebViewTransport } from './protocol';

export type PhaseKind = 'Focus' | 'ShortBreak';
export type ToastPayload =
  | Readonly<{ kind: 'sessionEnd'; sessionId: string }>
  | Readonly<{ kind: 'phase'; endedKind: PhaseKind | null; endedCycle: number | null; startedKind: PhaseKind | null; startedCycle: number | null; startedEndUtc: string | null }>;
export type Surface =
  | Readonly<{ surface: 'tray' }>
  | Readonly<{ surface: 'toast'; payload: ToastPayload }>
  | Readonly<{ surface: 'summary'; sessionId: string; dimmed: boolean }>
  | Readonly<{ surface: 'block'; target: string; startUtc: string; endUtc: string; strict: boolean }>;
export type AppSection = 'summary' | 'profiles' | 'activity' | 'schedules' | 'preferences' | 'connection' | 'help';
export type ShellSnapshot = Readonly<{
  /** "unknown" until the host speaks: a popup announces its surface before host.initialize, the main window never does. */
  mode: 'unknown' | 'main' | 'surface';
  surface: Surface | null;
  navigation: Readonly<{ section: AppSection; sequence: number }> | null;
}>;
export type ShellClient = ReturnType<typeof createShellClient>;

const sections: readonly AppSection[] = ['summary', 'profiles', 'activity', 'schedules', 'preferences', 'connection', 'help'];
const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const guid = (value: unknown): value is string => typeof value === 'string' && /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
const smallInt = (value: unknown) => value === null || Number.isSafeInteger(value) && (value as number) >= 0 && (value as number) <= 1000;
const phaseKind = (value: unknown) => value === null || value === 'Focus' || value === 'ShortBreak';
const date = (value: unknown) => typeof value === 'string' && utcMilliseconds(value) !== null;

/** Validates a host.surface message field by field; anything unexpected is ignored rather than rendered. */
export function parseHostSurface(value: unknown): (Surface & { documentToken: string }) | null {
  if (!isRecord(value) || !exact(value, ['type', 'version', 'documentToken', 'surface', 'payload']) || value.type !== 'host.surface'
    || value.version !== BRIDGE_PROTOCOL_VERSION || typeof value.documentToken !== 'string' || !/^[a-f0-9]{32}$/.test(value.documentToken)
    || !isRecord(value.payload)) return null;
  const token = value.documentToken;
  const payload = value.payload;
  switch (value.surface) {
    case 'tray': return exact(payload, []) ? { surface: 'tray', documentToken: token } : null;
    case 'summary': return exact(payload, ['sessionId', 'dimmed']) && guid(payload.sessionId) && typeof payload.dimmed === 'boolean'
      ? { surface: 'summary', sessionId: payload.sessionId, dimmed: payload.dimmed, documentToken: token } : null;
    case 'block': return exact(payload, ['target', 'startUtc', 'endUtc', 'strict']) && typeof payload.target === 'string'
      && payload.target.length > 0 && payload.target.length <= 260 && date(payload.startUtc) && date(payload.endUtc) && typeof payload.strict === 'boolean'
      ? { surface: 'block', target: payload.target, startUtc: payload.startUtc as string, endUtc: payload.endUtc as string, strict: payload.strict, documentToken: token } : null;
    case 'toast':
      if (payload.kind === 'sessionEnd' && exact(payload, ['kind', 'sessionId']) && guid(payload.sessionId))
        return { surface: 'toast', payload: { kind: 'sessionEnd', sessionId: payload.sessionId }, documentToken: token };
      if (payload.kind === 'phase' && exact(payload, ['kind', 'endedKind', 'endedCycle', 'startedKind', 'startedCycle', 'startedEndUtc'])
        && phaseKind(payload.endedKind) && phaseKind(payload.startedKind) && smallInt(payload.endedCycle) && smallInt(payload.startedCycle)
        && (payload.startedEndUtc === null || date(payload.startedEndUtc)) && (payload.endedKind !== null || payload.startedKind !== null))
        return { surface: 'toast', documentToken: token, payload: { kind: 'phase', endedKind: payload.endedKind as PhaseKind | null, endedCycle: payload.endedCycle as number | null,
          startedKind: payload.startedKind as PhaseKind | null, startedCycle: payload.startedCycle as number | null, startedEndUtc: payload.startedEndUtc as string | null } };
      return null;
    default: return null;
  }
}

function defaultTransport(): WebViewTransport | null {
  const hostGlobal = globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } };
  return hostGlobal.chrome?.webview ?? null;
}

/**
 * The page's side of the desktop shell: which surface this document is (main window, tray panel, notice, summary or
 * block screen), navigation requests from the tray, and the few shell commands a popup may send.
 */
export function createShellClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? defaultTransport() : provided;
  const listeners = new Set<() => void>();
  let snapshot: ShellSnapshot = Object.freeze({ mode: transport ? 'unknown' : 'main', surface: null, navigation: null });
  let token: string | null = null;
  let announced: (Surface & { documentToken: string }) | null = null;
  let sequence = 0;
  let active = false;
  const pending = new Map<string, (enabled: boolean | null) => void>();
  const publish = (patch: Partial<ShellSnapshot>) => { snapshot = Object.freeze({ ...snapshot, ...patch }); for (const listener of listeners) listener(); };

  const onMessage = (event: Event) => {
    if (!active) return;
    const data: unknown = (event as MessageEvent<unknown>).data;
    if (isRecord(data) && exact(data, ['version', 'requestId', 'documentToken', 'error', 'result']) && typeof data.requestId === 'string'
      && pending.has(data.requestId) && data.documentToken === token) {
      const resolve = pending.get(data.requestId)!;
      pending.delete(data.requestId);
      const result = data.result;
      resolve(isRecord(result) && typeof result.startupEnabled === 'boolean' ? result.startupEnabled : null);
      return;
    }
    const surface = parseHostSurface(data);
    if (surface) { announced = surface; return; }
    const initialization = parseHostInitialization(data);
    if (initialization) {
      token = initialization.documentToken;
      if (announced && announced.documentToken === token) {
        const { documentToken: _ignored, ...rest } = announced;
        publish({ mode: 'surface', surface: rest as Surface });
      } else if (snapshot.mode === 'unknown') publish({ mode: 'main' });
      return;
    }
    if (isRecord(data) && exact(data, ['type', 'version', 'documentToken', 'section']) && data.type === 'host.navigate'
      && data.version === BRIDGE_PROTOCOL_VERSION && data.documentToken === token && sections.includes(data.section as AppSection))
      publish({ navigation: { section: data.section as AppSection, sequence: ++sequence } });
  };

  const send = (method: 'app.open' | 'app.compact' | 'app.quit' | 'surface.close' | 'surface.ready' | 'app.startup.get' | 'app.startup.set',
    params: Record<string, string | boolean>, requestId?: string): boolean => {
    if (!transport || !token || !globalThis.crypto) return false;
    try {
      transport.postMessage({ version: BRIDGE_PROTOCOL_VERSION, requestId: requestId ?? globalThis.crypto.randomUUID().replaceAll('-', ''), documentToken: token, method, params });
      return true;
    } catch { return false; }
  };

  /** Reads (or sets) whether FocusGuard opens at sign-in; null when the host cannot tell. */
  const startup = (enabled?: boolean): Promise<boolean | null> => {
    if (!transport || !token || !globalThis.crypto) return Promise.resolve(null);
    const id = globalThis.crypto.randomUUID().replaceAll('-', '').toLowerCase();
    return new Promise(resolve => {
      const timer = setTimeout(() => { pending.delete(id); resolve(null); }, 4000);
      pending.set(id, value => { clearTimeout(timer); resolve(value); });
      const sent = enabled === undefined ? send('app.startup.get', {}, id) : send('app.startup.set', { enabled }, id);
      if (!sent) { clearTimeout(timer); pending.delete(id); resolve(null); }
    });
  };

  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot,
    start() {
      if (active) return;
      active = true;
      transport?.addEventListener('message', onMessage);
      // Outside the desktop host there is nothing to wait for.
      if (!transport) publish({ mode: 'main' });
    },
    stop() { active = false; transport?.removeEventListener('message', onMessage); },
    open(section: AppSection) { return send('app.open', { section }); },
    openCompact() { return send('app.compact', {}); },
    quit() { return send('app.quit', {}); },
    close() { return send('surface.close', {}); },
    /** Tells the host the popup is painted with the person's theme, so it can move on screen without a flash. */
    ready() { return send('surface.ready', {}); },
    startup,
  };
}
