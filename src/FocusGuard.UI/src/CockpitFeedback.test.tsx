import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { App, type StatusClient } from './App';
import { BlockedStack } from './BlockedStack';
import { createDashboardClient } from './native/dashboard';
import type { ProfileDetail, ProfilesClient, ProfileSnapshot } from './native/profiles';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import es from './locales/es.json';

const active: ConnectionSnapshot = {
  phase: 'ready', updatedAt: Date.parse('2026-10-04T12:00:00Z'), error: null,
  status: { serviceReady: true, extensionReady: false, sessionState: 'Active', remainingSeconds: 600,
    sessionId: '11111111-1111-4111-8111-111111111111', profileId: '22222222-2222-4222-8222-222222222222',
    startUtc: '2026-10-04T12:00:00Z', endUtc: '2026-10-04T12:25:00Z', strictMode: false, enforcementConnected: true, mode: 'SimpleTimer' },
};
let root: Root;
let container: HTMLDivElement;
function statusClient(initial: ConnectionSnapshot) {
  let snapshot = initial;
  const listeners = new Set<() => void>();
  const client: StatusClient = { subscribe(listener) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot, start: vi.fn(), stop: vi.fn(), refresh: vi.fn(async () => undefined) };
  return { client, update(next: ConnectionSnapshot) { snapshot = next; for (const listener of listeners) listener(); } };
}
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  window.matchMedia = vi.fn().mockImplementation(() => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }));
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); });

it('counts the session down every second between polls and freezes an unconfirmed reading', async () => {
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout', 'performance'] });
  const test = statusClient(active);
  await act(async () => root.render(<App client={test.client}/>));
  const timer = () => container.querySelector('.session-orb .timer')?.textContent;
  expect(timer()).toBe('10:00');
  await act(async () => { vi.advanceTimersByTime(1000); });
  expect(timer()).toBe('09:59');
  await act(async () => { vi.advanceTimersByTime(2000); });
  expect(timer()).toBe('09:57');
  // A new authoritative reading resets the base instead of drifting.
  await act(async () => test.update({ ...active, status: { ...active.status!, remainingSeconds: 590 } }));
  expect(timer()).toBe('09:50');
  await act(async () => test.update({ ...active, phase: 'stale', error: 'ServiceUnavailable', status: { ...active.status!, remainingSeconds: 580 } }));
  await act(async () => { vi.advanceTimersByTime(3000); });
  expect(timer()).toBe('09:40');
});

it('shows channel state with an icon and keeps appearance controls out of the dashboard header', async () => {
  const target = new EventTarget();
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage() {} };
  const dashboard = createDashboardClient(transport);
  const test = statusClient(active);
  await act(async () => root.render(<App client={test.client} dashboard={dashboard}/>));
  expect(container.querySelector('.page-header .ambience')).toBeNull();
  expect(container.querySelector('.page-header #language')).toBeNull();
  expect(container.querySelector('.page-header .icon-button')).toBeNull();
  const service = container.querySelector<HTMLElement>('[data-fact="service"]')!;
  const extension = container.querySelector<HTMLElement>('[data-fact="extension"]')!;
  expect(service.dataset.ok).toBe('true');
  expect(extension.dataset.ok).toBe('false');
  expect(service.querySelector('.fact-icon svg')).not.toBeNull();
  expect(service.querySelector('h3')?.textContent).toBe(es.connected);
});

function profilesStub(detail: ProfileDetail | null) {
  const snapshot: ProfileSnapshot = { detail, phase: 'idle', error: null };
  return { subscribe: () => () => undefined, getSnapshot: () => snapshot, load: vi.fn(async () => undefined) } as unknown as ProfilesClient;
}
const rule = (index: number, action: 'Block' | 'Allow' = 'Block') => ({ id: `33333333-3333-4333-8333-${String(index).padStart(12, '0')}`, action, matchKind: 'FileName' as const, value: `App${index}.exe` });

it('stacks up to five blocked apps, counts the rest as +N and states blocked websites', async () => {
  const profileId = '22222222-2222-4222-8222-222222222222';
  const detail = { id: profileId, applications: [rule(1), rule(2, 'Allow'), rule(3), rule(4), rule(5), rule(6)] } as unknown as ProfileDetail;
  await act(async () => root.render(<BlockedStack profiles={profilesStub(detail)} profileId={profileId} blockedApps={9} blockedSites={14} t={key => es[key]}/>));
  expect(container.querySelectorAll('.stack-avatar:not(.stack-more)')).toHaveLength(5);
  expect(container.querySelector('.stack-more')?.textContent).toBe('+4');
  expect(container.querySelector('.app-stack')?.getAttribute('aria-label')).toBe('9 apps bloqueadas');
  expect(container.querySelector('.sites-blocked')?.textContent).toBe('14 sitios bloqueados');
});

it('loads the profile rules for icons when they are not loaded yet and hides when nothing is blocked', async () => {
  const profiles = profilesStub(null);
  await act(async () => root.render(<BlockedStack profiles={profiles} profileId="22222222-2222-4222-8222-222222222222" blockedApps={2} blockedSites={0} t={key => es[key]}/>));
  expect(profiles.load).toHaveBeenCalledWith('22222222-2222-4222-8222-222222222222');
  expect(container.querySelector('.stack-more')?.textContent).toBe('+2');
  await act(async () => root.render(<BlockedStack profiles={profiles} profileId="22222222-2222-4222-8222-222222222222" blockedApps={0} blockedSites={0} t={key => es[key]}/>));
  expect(container.querySelector('.blocked-stack')).toBeNull();
});
