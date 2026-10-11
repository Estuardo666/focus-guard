import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { SurfaceApp } from './Surfaces';
import type { Surface, ShellClient } from './native/shell';
import type { DashboardClient } from './native/dashboard';
import type { NativeStatusClient } from './native/client';
import type { ConnectionSnapshot } from './native/protocol';
import en from './locales/en.json';

vi.mock('./FocusOrb', () => ({ FocusOrb: ({ children }: { children?: unknown }) => <div data-testid="orb">{children as never}</div> }));

const sessionId = '11111111-1111-4111-8111-111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
let root: Root;
let container: HTMLDivElement;
let shell: { open: ReturnType<typeof vi.fn>; openCompact: ReturnType<typeof vi.fn>; quit: ReturnType<typeof vi.fn>; close: ReturnType<typeof vi.fn>; ready: ReturnType<typeof vi.fn> };

const idle: ConnectionSnapshot = { phase: 'ready', updatedAt: 1, error: null, status: {
  serviceReady: true, extensionReady: true, sessionState: 'None', remainingSeconds: 0, sessionId: null, profileId: null,
  startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } as never };
const active: ConnectionSnapshot = { ...idle, status: { ...idle.status!, sessionState: 'Active', remainingSeconds: 600, sessionId, profileId,
  startUtc: '2026-10-10T09:00:00Z', endUtc: '2026-10-10T09:25:00Z', strictMode: true } };

function render(surface: Surface, connection: ConnectionSnapshot = idle) {
  shell = { open: vi.fn(() => true), openCompact: vi.fn(() => true), quit: vi.fn(() => true), close: vi.fn(() => true), ready: vi.fn(() => true) };
  const state = { preferences: { uiLanguage: 'en', reduceMotion: true, theme: 'Dark' }, catalog: { items: [{ id: profileId, name: 'Deep Work',
    blockedWebsiteCount: 3, blockedApplicationCount: 2 }] }, sessionSummary: null };
  const dashboard = {
    subscribe: () => () => undefined,
    getSnapshot: () => state,
    loadPreferences: vi.fn(async () => undefined),
    loadProfiles: vi.fn(async () => undefined),
    loadSessionSummary: vi.fn(async () => undefined),
  } as unknown as DashboardClient;
  const client = { subscribe: () => () => undefined, getSnapshot: () => connection } as unknown as NativeStatusClient;
  act(() => root.render(<SurfaceApp surface={surface} shell={shell as unknown as ShellClient} client={client} dashboard={dashboard}/>));
}

const button = (name: string) => [...container.querySelectorAll('button')].find(item => item.textContent?.includes(name) || item.getAttribute('aria-label') === name);

beforeEach(async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
  vi.setSystemTime(new Date('2026-10-10T09:15:00Z'));
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

it('tray panel offers open, compact, preferences and quit, and says quitting keeps the session', async () => {
  render({ surface: 'tray' }, active);
  await act(async () => { await Promise.resolve(); });
  expect(container.querySelector('.tray-timer')?.textContent).toBe('10:00');
  expect(container.textContent).toContain('Deep Work');
  expect(container.textContent).toContain(en.trayQuitKeeps);
  button(en.trayOpen)!.click();
  button(en.compactMode)!.click();
  button(en.preferences)!.click();
  button(en.trayQuit)!.click();
  expect(shell.open.mock.calls).toEqual([['summary'], ['preferences']]);
  expect(shell.openCompact).toHaveBeenCalledOnce();
  expect(shell.quit).toHaveBeenCalledOnce();
});

it('stays empty until the preferences are applied, then tells the host it is painted', async () => {
  render({ surface: 'tray' });
  expect(container.textContent).toBe('');
  await act(async () => { await Promise.resolve(); });
  expect(document.documentElement.dataset.theme).toBe('dark');
  expect(container.querySelector('.tray-panel')).not.toBeNull();
  // jsdom paces animation frames with the (faked) interval timer.
  act(() => { vi.advanceTimersByTime(100); });
  expect(shell.ready).toHaveBeenCalledOnce();
});

it('idle tray panel starts from the main window and omits the keep-running warning', async () => {
  render({ surface: 'tray' });
  await act(async () => { await Promise.resolve(); });
  expect(container.textContent).not.toContain(en.trayQuitKeeps);
  button(en.trayStart)!.click();
  expect(shell.open).toHaveBeenCalledWith('summary');
});

it('phase notice is a polite status that leaves on its own', async () => {
  render({ surface: 'toast', payload: { kind: 'phase', endedKind: 'Focus', endedCycle: 1, startedKind: 'ShortBreak', startedCycle: null, startedEndUtc: '2026-10-10T09:30:00Z' } });
  await act(async () => { await Promise.resolve(); });
  const notice = container.querySelector('[role="status"]');
  expect(notice?.getAttribute('aria-live')).toBe('polite');
  expect(notice?.textContent).toContain(en.phaseNoticeBreakTitle);
  act(() => { vi.advanceTimersByTime(9000); });
  expect(shell.close).toHaveBeenCalledOnce();
});

it('block screen names the app, shows the end time and strict note, and returns to work on request', async () => {
  render({ surface: 'block', target: 'C:\\Games\\Steam.exe', startUtc: '2026-10-10T09:00:00Z', endUtc: '2026-10-10T09:25:00Z', strict: true });
  await act(async () => { await Promise.resolve(); });
  expect(container.querySelector('h1')?.textContent).toBe(en.blockTitle.replace('{app}', 'Steam'));
  expect(container.querySelector('.timer')?.textContent).toBe('10:00');
  expect(container.textContent).toContain(en.blockStrict);
  button(en.blockBack)!.click();
  expect(shell.close).toHaveBeenCalledOnce();
});

it('block screen closes itself when the session time is over', async () => {
  render({ surface: 'block', target: 'Discord.exe', startUtc: '2026-10-10T09:00:00Z', endUtc: '2026-10-10T09:15:02Z', strict: false });
  await act(async () => { await Promise.resolve(); });
  expect(shell.close).not.toHaveBeenCalled();
  act(() => { vi.advanceTimersByTime(3000); });
  expect(shell.close).toHaveBeenCalled();
});
