import { parseHostInitialization, type WebViewTransport } from './protocol';
export type AppIcon = Readonly<{ width: number; height: number; bgra: string }>;
export type CatalogApplication = Readonly<{ id: string; name: string; path: string; icon: AppIcon | null }>;
export type ApplicationPage = Readonly<{ items: readonly CatalogApplication[]; offset: number; total: number; query: string }>;
export type ApplicationsSnapshot = Readonly<{ page: ApplicationPage | null; loading: boolean; error: string | null }>;
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const integer = (v: unknown): v is number => Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= 2000;
export function decodeIcon(v: unknown): Uint8ClampedArray | null {
  if (!record(v) || !exact(v, ['width', 'height', 'bgra']) || v.width !== 32 || v.height !== 32 || typeof v.bgra !== 'string' || v.bgra.length !== 5464 || !/^[A-Za-z0-9+/]+={0,2}$/.test(v.bgra)) return null;
  try { const bytes = atob(v.bgra); if (bytes.length !== 4096) return null; const rgba = new Uint8ClampedArray(4096); for (let i = 0; i < bytes.length; i += 4) { rgba[i] = bytes.charCodeAt(i + 2); rgba[i + 1] = bytes.charCodeAt(i + 1); rgba[i + 2] = bytes.charCodeAt(i); rgba[i + 3] = bytes.charCodeAt(i + 3); } return rgba; } catch { return null; }
}
export function isApplicationPage(v: unknown): v is ApplicationPage {
  if (!record(v) || !exact(v, ['items', 'offset', 'total', 'query']) || !integer(v.offset) || !integer(v.total) || typeof v.query !== 'string' || v.query.length > 80 || !Array.isArray(v.items) || v.items.length > 6 || v.items.length > Math.max(0, v.total - v.offset)) return false;
  const ids = new Set<string>();
  return v.items.every((app: unknown) => record(app) && exact(app, ['id', 'name', 'path', 'icon']) && typeof app.id === 'string' && /^(?!0{32})[0-9a-f]{32}$/i.test(app.id) && !ids.has(app.id) && !!ids.add(app.id) && typeof app.name === 'string' && app.name.trim().length > 0 && app.name.length <= 160 && typeof app.path === 'string' && /^[A-Za-z]:[\\/]/.test(app.path) && app.path.length <= 1024 && /\.exe$/i.test(app.path) && (app.icon === null || decodeIcon(app.icon) !== null));
}
export function createApplicationsClient(provided?: WebViewTransport | null) {
  const transport = provided === undefined ? (globalThis as typeof globalThis & { chrome?: { webview?: WebViewTransport } }).chrome?.webview ?? null : provided;
  let active = false; let token: string | null = null; let generation = 0;
  let snapshot: ApplicationsSnapshot = Object.freeze({ page: null, loading: false, error: null });
  let pending: { id: string; token: string; resolve(value: ApplicationPage): void; reject(error: Error): void; timer: ReturnType<typeof setTimeout> } | null = null;
  const listeners = new Set<() => void>();
  const publish = (patch: Partial<ApplicationsSnapshot>) => { snapshot = Object.freeze({ ...snapshot, ...patch }); for (const listener of listeners) listener(); };
  const cancel = () => { if (pending) { const p = pending; pending = null; clearTimeout(p.timer); p.reject(new Error('DocumentChanged')); } };
  const onMessage = (event: Event) => {
    if (!active) return; const data: unknown = (event as MessageEvent<unknown>).data;
    const init = parseHostInitialization(data);
    if (init) { if (token !== init.documentToken) { generation++; cancel(); token = init.documentToken; publish({ page: null, loading: false }); } return; }
    const p = pending; if (!p) return;
    let v: unknown; try { const json = typeof data === 'string' ? data : JSON.stringify(data); if (json === undefined || new TextEncoder().encode(json).length > 65536) return; v = typeof data === 'string' ? JSON.parse(json) : data; } catch { return; }
    if (!record(v) || !exact(v, ['version', 'requestId', 'documentToken', 'error', 'result']) || v.version !== 1 || v.requestId !== p.id || v.documentToken !== p.token || p.token !== token) return;
    if (typeof v.error === 'string' && /^[A-Za-z][A-Za-z0-9]{0,63}$/.test(v.error) && v.result === null) { pending = null; clearTimeout(p.timer); p.reject(new Error(v.error)); }
    else if (v.error === null && isApplicationPage(v.result)) { pending = null; clearTimeout(p.timer); p.resolve(v.result); }
  };
  const request = (query: string, offset: number, refresh: boolean): Promise<ApplicationPage> => {
    if (!active || !transport || !token) return Promise.reject(new Error('CatalogUnavailable'));
    if (pending) return Promise.reject(new Error('RequestInProgress'));
    return new Promise((resolve, reject) => {
      const p = { id: crypto.randomUUID().replaceAll('-', ''), token: token!, resolve, reject, timer: setTimeout(() => { if (pending === p) { pending = null; reject(new Error('RequestTimeout')); } }, 4000) };
      pending = p; try { transport.postMessage({ version: 1, requestId: p.id, documentToken: p.token, method: 'applications.list', params: { query, offset, refresh } }); } catch { pending = null; clearTimeout(p.timer); reject(new Error('CatalogUnavailable')); }
    });
  };
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }, getSnapshot: () => snapshot,
    start() { if (active) return; active = true; transport?.addEventListener('message', onMessage); },
    stop() { active = false; token = null; generation++; transport?.removeEventListener('message', onMessage); cancel(); },
    async search(query: string, offset = 0, refresh = false) {
      const mine = ++generation; if (query.length > 80 || !integer(offset)) return;
      publish({ loading: true, error: null });
      try {
        let page: ApplicationPage;
        for (let attempt = 0; ; attempt++) { try { page = await request(query.trim(), offset, refresh); break; } catch (error) { if (!(error instanceof Error) || error.message !== 'RequestInProgress' || attempt >= 5) throw error; await new Promise(resolve => setTimeout(resolve, 500)); if (mine !== generation) return; } }
        if (mine !== generation) return;
        if (page.query !== query.trim() || page.offset !== offset) throw new Error('InvalidResponse');
        publish({ page, loading: false });
      } catch (error) { if (mine === generation) publish({ loading: false, error: error instanceof Error ? error.message : 'CatalogUnavailable' }); }
    }
  };
}
export type ApplicationsClient = ReturnType<typeof createApplicationsClient>;
