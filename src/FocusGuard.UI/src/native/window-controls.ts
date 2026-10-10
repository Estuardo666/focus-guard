import {
  BRIDGE_PROTOCOL_VERSION,
  MAXIMUM_RESPONSE_BYTES,
  parseHostInitialization,
  parseHostWindowState,
  type HostWindowPresentation,
  type WebViewTransport,
} from './protocol';

export type WindowControlSnapshot = Readonly<{
  compact: boolean;
  alwaysOnTop: boolean;
  detailsExpanded: boolean;
  phase: 'waiting' | 'ready' | 'writing' | 'error';
  error: string | null;
}>;

export type WindowControlsClient = ReturnType<typeof createWindowControlsClient>;
type Method = 'window.compact.set' | 'window.alwaysOnTop.set' | 'window.compactDetails.set';
type Listener = (event: Event) => void;
type PendingRequest = {
  id: string;
  token: string;
  timer: ReturnType<typeof setTimeout>;
  resolve(value: boolean): void;
};

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const isPresentation = (value: unknown): value is HostWindowPresentation =>
  isRecord(value) && Object.keys(value).length === 3
  && Object.hasOwn(value, 'compact') && typeof value.compact === 'boolean'
  && Object.hasOwn(value, 'alwaysOnTop') && typeof value.alwaysOnTop === 'boolean'
  && Object.hasOwn(value, 'detailsExpanded') && typeof value.detailsExpanded === 'boolean';

function defaultTransport(): WebViewTransport | null {
  const hostGlobal = globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } };
  return hostGlobal.chrome?.webview ?? null;
}

function secureRequestId(): string {
  if (!globalThis.crypto) throw new Error('Secure randomness unavailable');
  return globalThis.crypto.randomUUID().replaceAll('-', '').toLowerCase();
}

export function createWindowControlsClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? defaultTransport() : provided;
  const listeners = new Set<() => void>();
  let snapshot: WindowControlSnapshot = Object.freeze({ compact: false, alwaysOnTop: false, detailsExpanded: true, phase: 'waiting', error: null });
  let active = false;
  let token: string | null = null;
  let pending: PendingRequest | null = null;

  const publish = (next: WindowControlSnapshot) => {
    if (snapshot.compact === next.compact && snapshot.alwaysOnTop === next.alwaysOnTop
      && snapshot.detailsExpanded === next.detailsExpanded
      && snapshot.phase === next.phase && snapshot.error === next.error) return;
    snapshot = Object.freeze(next);
    for (const listener of listeners) listener();
  };

  const finish = (request: PendingRequest, result: HostWindowPresentation | null, error: string | null) => {
    if (pending !== request) return;
    pending = null;
    clearTimeout(request.timer);
    if (result) publish({ ...result, phase: 'ready', error: null });
    else publish({ ...snapshot, phase: 'error', error: error ?? 'WindowUnavailable' });
    request.resolve(result !== null);
  };

  const cancelPending = () => {
    if (!pending) return;
    const request = pending;
    pending = null;
    clearTimeout(request.timer);
    request.resolve(false);
  };

  const onMessage: Listener = event => {
    if (!active) return;
    const data: unknown = (event as MessageEvent<unknown>).data;
    const initialization = parseHostInitialization(data);
    if (initialization) {
      if (token === initialization.documentToken) return;
      cancelPending();
      token = initialization.documentToken;
      publish({ compact: false, alwaysOnTop: false, detailsExpanded: true, phase: 'waiting', error: null });
      return;
    }

    const state = parseHostWindowState(data);
    if (state && state.documentToken === token) {
      publish({ compact: state.compact, alwaysOnTop: state.alwaysOnTop, detailsExpanded: state.detailsExpanded,
        phase: pending ? 'writing' : 'ready', error: null });
      return;
    }

    if (!pending) return;
    let value: unknown;
    try {
      const json = typeof data === 'string' ? data : JSON.stringify(data);
      if (typeof json !== 'string' || new TextEncoder().encode(json).byteLength > MAXIMUM_RESPONSE_BYTES) return;
      value = typeof data === 'string' ? JSON.parse(json) : data;
    } catch {
      return;
    }
    const request = pending;
    if (!isRecord(value) || Object.keys(value).length !== 5
      || value.version !== BRIDGE_PROTOCOL_VERSION || value.requestId !== request.id
      || value.documentToken !== request.token || request.token !== token
      || !Object.hasOwn(value, 'error') || !Object.hasOwn(value, 'result')) return;
    if (value.error === null && isPresentation(value.result)) {
      finish(request, value.result, null);
    } else if (typeof value.error === 'string' && /^[A-Za-z][A-Za-z0-9]{0,63}$/.test(value.error)
      && value.result === null) {
      finish(request, null, value.error);
    }
  };

  const set = (method: Method, enabled: boolean): Promise<boolean> => {
    if (!active || !transport || !token || snapshot.phase === 'waiting') {
      publish({ ...snapshot, phase: 'error', error: 'BridgeUnavailable' });
      return Promise.resolve(false);
    }
    if (pending) return Promise.resolve(false);

    let id: string;
    try { id = secureRequestId(); }
    catch {
      publish({ ...snapshot, phase: 'error', error: 'BridgeUnavailable' });
      return Promise.resolve(false);
    }

    publish({ ...snapshot, phase: 'writing', error: null });
    return new Promise(resolve => {
      let request: PendingRequest | null = null;
      const timer = setTimeout(() => { if (request) finish(request, null, 'RequestTimeout'); }, 4000);
      request = {
        id,
        token: token!,
        resolve,
        timer,
      };
      pending = request;
      try {
        transport.postMessage({ version: BRIDGE_PROTOCOL_VERSION, requestId: id,
          documentToken: token, method, params: { enabled } });
      } catch {
        finish(request, null, 'BridgeUnavailable');
      }
    });
  };

  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot,
    start() {
      if (active) return;
      active = true;
      if (transport) transport.addEventListener('message', onMessage);
      else publish({ ...snapshot, phase: 'error', error: 'BridgeUnavailable' });
    },
    stop() {
      active = false;
      token = null;
      transport?.removeEventListener('message', onMessage);
      cancelPending();
      publish({ compact: false, alwaysOnTop: false, detailsExpanded: true, phase: 'waiting', error: null });
    },
    setCompactMode(enabled: boolean) { return set('window.compact.set', enabled); },
    setAlwaysOnTop(enabled: boolean) { return set('window.alwaysOnTop.set', enabled); },
    setDetailsExpanded(enabled: boolean) { return set('window.compactDetails.set', enabled); },
  };
}
