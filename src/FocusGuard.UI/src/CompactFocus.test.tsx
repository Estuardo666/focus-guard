import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { CompactFocus } from './CompactFocus';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient } from './native/dashboard';
import type { WindowControlSnapshot, WindowControlsClient } from './native/window-controls';
import type { SessionReplayIntent } from './native/replay';
import en from './locales/en.json';

const sessionId = '11111111-1111-4111-8111-111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
const intent: SessionReplayIntent = {
  sessionId, available: true, replayable: true, unavailableReason: null, originalProfileName: 'Deep Work',
  profile: { id: profileId, name: 'Deep Work', applicationRuleCount: 2, websiteRuleCount: 1,
    blockedApplicationCount: 2, blockedWebsiteCount: 1 }, durationMinutes: 25, mode: 'SimpleTimer',
  strict: false, enforcementEnabled: true, cyclePresetId: null, cycleConfiguration: null, objective: 'Write the design review',
};
const status = {
  serviceReady: true as const, extensionReady: true, sessionState: 'Active', remainingSeconds: 300,
  sessionId, profileId, startUtc: '2026-10-06T12:00:00Z', endUtc: '2026-10-06T12:25:00Z',
  strictMode: false, enforcementConnected: true, mode: 'SimpleTimer' as const,
};
const windowState: WindowControlSnapshot = { compact: true, alwaysOnTop: false, detailsExpanded: true, phase: 'ready', error: null };

let root: Root;
let container: HTMLDivElement;
let dashboard: Pick<DashboardClient, 'subscribe' | 'getSnapshot' | 'loadSessionIntent'>;
let setCompactMode: ReturnType<typeof vi.fn>;
let setAlwaysOnTop: ReturnType<typeof vi.fn>;
let setDetailsExpanded: ReturnType<typeof vi.fn>;

function fixture(connection: ConnectionSnapshot, compactState = windowState) {
  const state = { sessionReplay: intent, catalog: null, replayLoading: false };
  dashboard = {
    subscribe: () => () => undefined,
    getSnapshot: () => state,
    loadSessionIntent: vi.fn(async () => intent),
  } as unknown as Pick<DashboardClient, 'subscribe' | 'getSnapshot' | 'loadSessionIntent'>;
  setCompactMode = vi.fn(async () => true);
  setAlwaysOnTop = vi.fn(async () => true);
  setDetailsExpanded = vi.fn(async () => true);
  const windowControls = { setCompactMode, setAlwaysOnTop, setDetailsExpanded } as unknown as Pick<WindowControlsClient, 'setCompactMode' | 'setAlwaysOnTop' | 'setDetailsExpanded'>;
  return { connection, dashboard, windowControls, windowState: compactState, language: 'en' as const,
    t: (key: keyof typeof en) => en[key], refresh: vi.fn(async () => undefined), expandButtonRef: { current: null } };
}

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

it('shows authoritative time, phase, profile and exact session end with no session controls', async () => {
  const props = fixture({ phase: 'ready', status, updatedAt: Date.parse('2026-10-06T12:20:00Z'), error: null });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(document.activeElement).toBe(props.expandButtonRef.current);
  expect(container.querySelector('.compact-timer')?.textContent).toBe('00:05:00');
  expect(container.querySelector('.compact-session-context')?.textContent).toContain('Focus');
  expect(container.querySelector('.compact-session-context')?.textContent).toContain('Deep Work');
  expect(container.textContent).toContain('Session ends');
  expect(container.textContent).toContain('Closing this window does not stop the session.');
  expect(Array.from(container.querySelectorAll('button')).map(button => button.textContent)).toContain('Open full view');
  expect(container.textContent).toContain('Write the design review');
  expect(container.textContent).not.toContain('Start session');

  const open = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Open full view')!;
  await act(async () => open.click());
  expect(setCompactMode).toHaveBeenCalledWith(false);
  const hideDetails = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Hide details')!;
  await act(async () => hideDetails.click());
  expect(setDetailsExpanded).toHaveBeenCalledWith(false);
  const topmost = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Always on top')!;
  expect(topmost.getAttribute('aria-pressed')).toBe('false');
  await act(async () => topmost.click());
  expect(setAlwaysOnTop).toHaveBeenCalledWith(true);
});

it('keeps the compact companion hidden unless the native window is in compact mode', async () => {
  const fullView = fixture({ phase: 'ready', status, updatedAt: Date.now(), error: null }, { ...windowState, compact: false });
  await act(async () => root.render(<CompactFocus {...fullView}/>));
  expect(container.querySelector('.compact-focus')?.hasAttribute('hidden')).toBe(true);

  await act(async () => root.render(<CompactFocus {...fixture({ phase: 'ready', status, updatedAt: Date.now(), error: null })}/>));
  expect(container.querySelector('.compact-focus')?.hasAttribute('hidden')).toBe(false);
});

it('keeps the glance-critical fields and protection warning when details are collapsed', async () => {
  const cycleStatus = { ...status, mode: 'FocusCycle' as const, remainingSeconds: 900, enforcementConnected: false,
    currentPhase: { index: 1, kind: 'ShortBreak' as const, focusCycleNumber: 1, remainingSeconds: 45,
      startUtc: '2026-10-06T12:20:00Z', endUtc: '2026-10-06T12:20:45Z' }, nextPhase: null, finalEndUtc: status.endUtc };
  const props = fixture({ phase: 'ready', status: cycleStatus, updatedAt: Date.now(), error: null }, { ...windowState, detailsExpanded: false });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(container.querySelector('.compact-details')?.hasAttribute('hidden')).toBe(true);
  expect(container.querySelector('.compact-timer')?.textContent).toBe('00:00:45');
  expect(container.querySelector('.compact-session-context')?.textContent).toContain('Short break');
  expect(container.textContent).toContain(en.compactProtectionWarning);
  expect(container.querySelector('.compact-expand')).not.toBeNull();
});

it('uses current cycle-phase time and keeps an unconfirmed requested block visible', async () => {
  const cycleStatus = { ...status, mode: 'FocusCycle' as const, remainingSeconds: 900, enforcementConnected: false,
    currentPhase: { index: 1, kind: 'ShortBreak' as const, focusCycleNumber: 1, remainingSeconds: 45,
      startUtc: '2026-10-06T12:20:00Z', endUtc: '2026-10-06T12:20:45Z' },
    nextPhase: null, finalEndUtc: status.endUtc };
  const props = fixture({ phase: 'ready', status: cycleStatus, updatedAt: Date.now(), error: null });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(container.querySelector('.compact-timer')?.textContent).toBe('00:00:45');
  expect(container.querySelector('.compact-session-context')?.textContent).toContain('Short break');
  expect(container.textContent).toContain(en.compactProtectionWarning);
});

it('marks a stale reading and exposes refresh without relabeling it as live', async () => {
  const props = fixture({ phase: 'stale', status, updatedAt: Date.parse('2026-10-06T12:00:00Z'), error: 'ServiceUnavailable' });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(container.textContent).toContain(en.staleDetail);
  expect(container.querySelector('.compact-timer')?.textContent).toBe('00:05:00');
  const refresh = Array.from(container.querySelectorAll('button')).find(button => button.textContent === 'Refresh status')!;
  await act(async () => refresh.click());
  expect(props.refresh).toHaveBeenCalledOnce();
});

it('labels a disconnected compact session as unavailable instead of unknown', async () => {
  const props = fixture({ phase: 'unavailable', status: null, updatedAt: null, error: 'ServiceUnavailable' });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(container.querySelector('.compact-session-empty h1')?.textContent).toBe(en.statusUnavailableTitle);
  expect(container.querySelector('.compact-session-empty p')?.textContent).toBe(en.compactUnavailableSummary);
  expect(container.querySelector('.compact-session-empty')?.textContent).not.toContain(en.unknown);
  expect(container.querySelector('.compact-alert')?.textContent).toContain(en.unavailableDetail);
});

it('keeps strict recovery actionable in the compact presentation', async () => {
  const strictStatus = { ...status, strictMode: true, sessionState: 'RecoveryPending', remainingSeconds: 0, enforcementConnected: false };
  const props = fixture({ phase: 'ready', status: strictStatus, updatedAt: Date.now(), error: null });
  await act(async () => root.render(<CompactFocus {...props}/>));
  expect(container.textContent).toContain(en.RecoveryPending);
  expect(container.textContent).toContain(en.compactRecoveryPending);
  expect(container.querySelector('.compact-timer')).toBeNull();
  expect(container.textContent).not.toContain('00:00:00');
});
