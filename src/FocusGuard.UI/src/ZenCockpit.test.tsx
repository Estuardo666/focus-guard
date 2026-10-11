import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { App, type StatusClient } from './App';
import type { ConnectionSnapshot } from './native/protocol';
import es from './locales/es.json';

const sessionId = '11111111-1111-4111-8111-111111111111';
const active: ConnectionSnapshot = {
  phase: 'ready', updatedAt: Date.parse('2026-10-04T12:00:00Z'), error: null,
  status: { serviceReady: true, extensionReady: true, sessionState: 'Active', remainingSeconds: 600, sessionId,
    profileId: '22222222-2222-4222-8222-222222222222', startUtc: '2026-10-04T12:00:00Z', endUtc: '2026-10-04T12:25:00Z',
    strictMode: false, enforcementConnected: true, mode: 'SimpleTimer' },
};
const idle: ConnectionSnapshot = { ...active, status: { serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0, sessionId: null,
  profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } };

let root: Root;
let container: HTMLDivElement;
function fixture(initial: ConnectionSnapshot) {
  let snapshot = initial;
  const listeners = new Set<() => void>();
  const client: StatusClient = { subscribe(listener) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot, start: vi.fn(), stop: vi.fn(), refresh: vi.fn(async () => undefined) };
  return { client, update(next: ConnectionSnapshot) { snapshot = next; for (const listener of listeners) listener(); } };
}
const shell = () => container.querySelector<HTMLElement>('.app-shell')!;
const button = (text: string) => [...container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === text);
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  window.matchMedia = vi.fn().mockImplementation(() => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }));
  localStorage.clear();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

it('opens the immersive cabin only for a confirmed running session', async () => {
  const test = fixture(idle);
  await act(async () => root.render(<App client={test.client}/>));
  expect(shell().dataset.immersive).toBe('false');
  await act(async () => test.update(active));
  expect(shell().dataset.immersive).toBe('true');
  // The orb ring reflects the service's remaining fraction (600 of 1500 seconds).
  expect(container.querySelector('.session-orb .timer')?.textContent).toBe('10:00');
  expect(container.querySelector('.cabin-breathe')?.textContent).toBe(es.breathe);
  await act(async () => test.update({ ...active, phase: 'stale', error: 'ServiceUnavailable' }));
  expect(shell().dataset.immersive).toBe('false');
});

it('leaves and re-enters the cabin without changing the session', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  await act(async () => button(es.immersiveExit)!.click());
  expect(shell().dataset.immersive).toBe('false');
  expect(button(es.immersiveEnter)).toBeDefined();
  await act(async () => button(es.immersiveEnter)!.click());
  expect(shell().dataset.immersive).toBe('true');
  await act(async () => { window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); });
  expect(shell().dataset.immersive).toBe('false');
  expect(test.client.refresh).not.toHaveBeenCalled();
  // A new session opens the cabin again.
  await act(async () => test.update({ ...active, status: { ...active.status!, sessionId: '33333333-3333-4333-8333-333333333333' } }));
  expect(shell().dataset.immersive).toBe('true');
});

it('keeps strict warnings and the recovery path visible inside the cabin', async () => {
  const test = fixture({ ...active, status: { ...active.status!, strictMode: true } });
  await act(async () => root.render(<App client={test.client}/>));
  expect(shell().dataset.immersive).toBe('true');
  expect(container.querySelector('.badge.strict')?.textContent).toBe(es.strict);
  expect(container.querySelector('.strict-recovery-inline')?.textContent).toContain('FocusGuard.Recovery.exe');
});

it('switches and remembers the ambience palette', async () => {
  const test = fixture(idle);
  await act(async () => root.render(<App client={test.client}/>));
  expect(document.documentElement.dataset.palette).toBe('aurora');
  const ocean = container.querySelector<HTMLButtonElement>('.ambience-swatch[data-swatch=ocean]')!;
  expect(ocean.getAttribute('aria-label')).toBe(es.paletteOcean);
  await act(async () => ocean.click());
  expect(document.documentElement.dataset.palette).toBe('ocean');
  expect(ocean.getAttribute('aria-pressed')).toBe('true');
  expect(localStorage.getItem('focusguard.palette')).toBe('ocean');
});
