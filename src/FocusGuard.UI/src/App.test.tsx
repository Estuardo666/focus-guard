import { act } from 'react';
import { createRoot } from 'react-dom/client';
import type { Root } from 'react-dom/client';
import { App, protectionFactFor, type StatusClient } from './App';
import { createDashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import type { UserPreferences } from './native/preferences';
import es from './locales/es.json';
import en from './locales/en.json';
import { formatRemaining } from './formatting';

const active: ConnectionSnapshot = {
  phase: 'ready', updatedAt: Date.parse('2026-10-04T12:00:00Z'), error: null,
  status: { serviceReady: true, extensionReady: true, sessionState: 'Active', remainingSeconds: 1500,
    sessionId: '11111111-1111-4111-8111-111111111111', profileId: '22222222-2222-4222-8222-222222222222',
    startUtc: '2026-10-04T12:00:00Z', endUtc: '2026-10-04T12:25:00Z', strictMode: false, enforcementConnected: true, mode: 'SimpleTimer' },
};

let root: Root;
let container: HTMLDivElement;
function fixture(initial: ConnectionSnapshot) {
  let snapshot = initial;
  const listeners = new Set<() => void>();
  const refresh = vi.fn(async () => undefined);
  const client: StatusClient = {
    subscribe(listener) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getSnapshot: () => snapshot, start: vi.fn(), stop: vi.fn(), refresh,
  };
  return { client, refresh, update(next: ConnectionSnapshot) { snapshot = next; for (const listener of listeners) listener(); } };
}
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  window.matchMedia = vi.fn().mockImplementation(() => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }));
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

it('keeps keyboard skip navigation before the sidebar and targets focusable main content', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  const shell = container.querySelector<HTMLElement>('.app-shell');
  const skip = container.querySelector<HTMLAnchorElement>('.skip-link');
  const main = container.querySelector<HTMLElement>('#main');

  expect(shell?.firstElementChild).toBe(skip);
  expect(skip?.getAttribute('href')).toBe('#main');
  expect(main?.tabIndex).toBe(-1);
  await act(async () => skip?.focus());
  expect(document.activeElement).toBe(skip);
});

it('renders only authoritative remaining time and keeps last reading marked stale', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.textContent).toContain('25:00');
  expect(container.textContent).toContain(es.unknown);
  expect(container.querySelector('.activation-pending')).toBeNull();
  await act(async () => test.update({ ...active, phase: 'stale', error: 'ServiceUnavailable' }));
  expect(container.textContent).toContain('Estado desactualizado');
  expect(container.textContent).toContain('25:00');
  expect(container.textContent).not.toContain('Confirmada');
  expect(container.textContent).not.toContain('Desconocido');
  expect(container.querySelectorAll('.fact')).toHaveLength(0);
});

it('keeps current active-channel health beside the live session and hides stale health facts', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  const overview = container.querySelector<HTMLElement>('.session-overview-active');
  expect(overview?.querySelector('.session-surface')).not.toBeNull();
  const facts = overview?.querySelector<HTMLElement>('.facts');
  expect(facts?.querySelectorAll('.fact')).toHaveLength(3);
  expect(facts?.textContent).toContain(es.connected);
  expect(facts?.textContent).toContain(es.available);
  expect(facts?.textContent).toContain(es.unknown);
  const protectionHeading = facts?.querySelector<HTMLElement>('[data-fact="protection"] h3');
  expect(protectionHeading?.getAttribute('aria-live')).toBe('polite');
  expect(protectionHeading?.getAttribute('aria-atomic')).toBe('true');

  await act(async () => test.update({ ...active, phase: 'stale', error: 'ServiceUnavailable' }));
  expect(container.querySelector('.session-overview-active')).toBeNull();
  expect(container.querySelector('.facts')).toBeNull();
  expect(container.textContent).toContain(es.stale);
  await act(async () => test.update({ ...active, status: { ...active.status!, sessionState: 'Expired', remainingSeconds: 0 } }));
  const terminalProtection = container.querySelector<HTMLElement>('[data-fact="protection"] h3');
  expect(terminalProtection?.getAttribute('aria-live')).toBeNull();
});

it('distinguishes explicitly unrequested enforcement from missing confirmation', () => {
  const status = active.status!;
  const intent = (sessionId: string, enforcementEnabled: boolean | null): SessionReplayIntent => ({
    sessionId, available: true, replayable: false, unavailableReason: null, originalProfileName: null, profile: null,
    durationMinutes: null, mode: null, strict: null, enforcementEnabled, cyclePresetId: null,
    cycleConfiguration: null, objective: null,
  });
  expect(protectionFactFor(status, true, intent(status.sessionId!, false))).toEqual({
    value: 'enforcementNotRequested', detail: 'enforcementNotRequestedDetail', ok: false,
  });
  expect(protectionFactFor({ ...status, enforcementConnected: false }, true, intent(status.sessionId!, true))).toEqual({
    value: 'notConfirmed', detail: 'protectionDetail', ok: false,
  });
  expect(protectionFactFor(status, true, intent(status.sessionId!, true))).toEqual({
    value: 'confirmed', detail: 'protectionDetail', ok: true,
  });
  expect(protectionFactFor(status, true, intent(status.sessionId!, null))).toEqual({
    value: 'unknown', detail: 'enforcementIntentUnknownDetail', ok: false,
  });
  expect(protectionFactFor({ ...status, enforcementConnected: false }, true,
    intent('33333333-3333-4333-8333-333333333333', false))).toEqual({
    value: 'unknown', detail: 'enforcementIntentUnknownDetail', ok: false,
  });
  expect(protectionFactFor({ ...status, enforcementConnected: true }, true,
    intent('33333333-3333-4333-8333-333333333333', false)).value).toBe('unknown');
  expect(protectionFactFor(status, false, intent(status.sessionId!, false)).value).toBe('unknown');
  expect(protectionFactFor({ ...status, sessionState: 'Expired' }, true, intent(status.sessionId!, true)).value).toBe('notRequested');
});

it('renders the matching active-session intent in the protection fact', async () => {
  const test = fixture(active);
  const target = new EventTarget();
  const sent: Record<string, unknown>[] = [];
  const transport: WebViewTransport = {
    addEventListener: target.addEventListener.bind(target),
    removeEventListener: target.removeEventListener.bind(target),
    postMessage(message) { sent.push(message as Record<string, unknown>); },
  };
  const dashboard = createDashboardClient(transport);
  dashboard.start();
  target.dispatchEvent(new MessageEvent('message', { data: {
    type: 'host.initialize', version: 1, documentToken: '11111111111141118111111111111111',
    title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable',
  } }));
  const reply = (result: unknown) => {
    const request = sent.at(-1)!;
    target.dispatchEvent(new MessageEvent('message', { data: {
      version: 1, requestId: request.requestId, documentToken: request.documentToken, error: null, result,
    } }));
  };
  reply({ items: [], offset: 0, total: 0, pilotEnabled: false });
  await new Promise(resolve => setTimeout(resolve, 0));
  const sessionId = active.status!.sessionId!;
  const intent: SessionReplayIntent = { sessionId, available: true, replayable: false, unavailableReason: null,
    originalProfileName: null, profile: null, durationMinutes: null, mode: null, strict: null,
    enforcementEnabled: false, cyclePresetId: null, cycleConfiguration: null, objective: null };
  const pending = dashboard.loadSessionIntent(sessionId);
  reply(intent);
  expect(await pending).toEqual(intent);
  expect(dashboard.getSnapshot().sessionReplay).toEqual(intent);

  await act(async () => root.render(<App client={test.client} dashboard={dashboard}/>));
  const protection = Array.from(container.querySelectorAll<HTMLElement>('.fact'))
    .find(fact => fact.querySelector('.fact-title')?.textContent?.includes(es.protection));
  expect(protection?.querySelector('h3')?.textContent).toBe(es.enforcementNotRequested);
  expect(protection?.querySelector('p')?.textContent).toBe(es.enforcementNotRequestedDetail);
});

it('loads the current intent while the user is on Connection and updates its health fact', async () => {
  const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
    sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false } };
  const test = fixture(inactive);
  const listeners = new Set<() => void>();
  let dashboardState = { catalog: null, preferences: null, sessionReplay: null, replayLoading: false, replayError: null } as unknown as DashboardSnapshot;
  const sessionIntent = (sessionId: string): SessionReplayIntent => ({ sessionId, available: true, replayable: false,
    unavailableReason: null, originalProfileName: null, profile: null, durationMinutes: null, mode: null, strict: null,
    enforcementEnabled: false, cyclePresetId: null, cycleConfiguration: null, objective: null });
  const dashboard = {
    subscribe(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); },
    getSnapshot: () => dashboardState,
    start: vi.fn(), stop: vi.fn(), loadPreferences: vi.fn(async () => null), loadProfiles: vi.fn(async () => null),
    observeStatus: vi.fn(),
    loadSessionIntent: vi.fn(async (sessionId: string) => {
      const intent = sessionIntent(sessionId);
      dashboardState = { ...dashboardState, sessionReplay: intent, replayLoading: false } as DashboardSnapshot;
      for (const listener of listeners) listener();
      return intent;
    }),
  } as unknown as DashboardClient;
  await act(async () => root.render(<App client={test.client} dashboard={dashboard}/>));
  const connectionTab = Array.from(container.querySelectorAll<HTMLButtonElement>('[role="tab"]'))
    .find(button => button.textContent?.includes(es.connection))!;
  await act(async () => { connectionTab.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 })); connectionTab.click(); });
  expect(container.querySelector('.connection-intro')).not.toBeNull();
  await act(async () => test.update(active));
  expect(dashboard.loadSessionIntent).toHaveBeenCalledTimes(1);
  expect(dashboard.loadSessionIntent).toHaveBeenCalledWith(active.status!.sessionId);
  const protection = Array.from(container.querySelectorAll<HTMLElement>('.fact'))
    .find(fact => fact.querySelector('.fact-title')?.textContent?.includes(es.protection));
  expect(protection?.querySelector('h3')?.textContent).toBe(es.enforcementNotRequested);
  expect(protection?.querySelector('h3')?.getAttribute('aria-live')).toBe('polite');
});

it('applies the stored reduced-motion preference to the document root', async () => {
  const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
    sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false } };
  const test = fixture(inactive);
  const preferences: UserPreferences = { uiLanguage: 'en', theme: 'Dark', reduceMotion: true, sessionEnd: 'None',
    cyclePhaseNotifications: false, enforcementEnabled: false, lastDurationMinutes: 25, lastSessionMode: 'SimpleTimer', lastCyclePresetId: null };
  const dashboardState = { catalog: null, preferences, sessionReplay: null, replayLoading: false } as unknown as DashboardSnapshot;
  const dashboard = {
    subscribe: () => () => undefined,
    getSnapshot: () => dashboardState,
    start: vi.fn(), stop: vi.fn(), loadPreferences: vi.fn(async () => preferences),
    loadProfiles: vi.fn(async () => null), loadSessionIntent: vi.fn(async () => null), observeStatus: vi.fn(),
  } as unknown as DashboardClient;
  await act(async () => { root.render(<App client={test.client} dashboard={dashboard}/>); await Promise.resolve(); });
  expect(document.documentElement.dataset.reduceMotion).toBe('true');
  expect(document.documentElement.lang).toBe('en');
});

it('explains that blocking has not started while requested channels are still acknowledging', async () => {
  const pending = fixture({ ...active, status: { ...active.status!, sessionState: 'Activating', remainingSeconds: 59, enforcementConnected: false } });
  await act(async () => root.render(<App client={pending.client}/>));
  const notice = container.querySelector<HTMLElement>('.activation-pending');
  expect(notice?.getAttribute('role')).toBe('status');
  expect(notice?.textContent).toBe(es.activationPendingDetail);
  expect(container.querySelector('.session-state')?.textContent).toBe(es.Activating);
  await act(async () => pending.update(active));
  expect(container.querySelector('.activation-pending')).toBeNull();
});

it('does not turn an unavailable backend into idle or fake zero time', async () => {
  const test = fixture({ phase: 'unavailable', status: null, updatedAt: null, error: 'NoBridge' });
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.textContent).toContain('Servicio no disponible');
  expect(container.querySelector('.idle-title')?.textContent).toBe(es.statusUnavailableTitle);
  expect(container.querySelector('.session-state')).toBeNull();
  expect(container.querySelector('.session-guidance')?.textContent).toBe(es.unavailableDetail);
  expect(container.querySelector('.notice')).toBeNull();
  expect(container.querySelectorAll('.fact')).toHaveLength(0);
  expect(container.textContent).not.toContain('Sin sesión activa');
  expect(container.textContent).not.toContain('00:00:00');
});

it('does not present pending recovery as an inactive session', async () => {
  const test = fixture({ ...active, status: { ...active.status!, sessionState: 'RecoveryPending', strictMode: true, remainingSeconds: 0, enforcementConnected: false } });
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.textContent).toContain('Recuperación pendiente');
  expect(container.textContent).toContain('Modo estricto');
  expect(container.textContent).not.toContain('Sin sesión activa');
});

it('identifies an existing focus cycle without exposing cycle activation commands', async () => {
  const test = fixture({ ...active, status: { ...active.status!, mode: 'FocusCycle' } });
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.textContent).toContain('Ciclo de enfoque');
  expect(container.textContent).toContain('25:00');
});

it('language selection and refresh are meaningful without exposing mutation buttons', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.querySelector('.rail-caption')?.textContent).toBe('Solo lectura');
  const language = container.querySelector<HTMLSelectElement>('#language')!;
  await act(async () => { language.value = 'en'; language.dispatchEvent(new Event('change', { bubbles: true })); });
  expect(document.documentElement.lang).toBe('en');
  expect(container.querySelector('.rail-caption')?.textContent).toBe('Read-only');
  expect(container.textContent).toContain('Session in progress');
  const button = Array.from(container.querySelectorAll('button')).find(item => item.textContent === 'Refresh status')!;
  await act(async () => button.click());
  expect(test.refresh).toHaveBeenCalledTimes(1);
  expect(container.textContent).not.toContain('Start session');
});

it('documents current React features and explains which rules stay blocked during breaks', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  const helpTab = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent?.includes('Ayuda'))!;
  await act(async () => { helpTab.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 })); helpTab.click(); });
  expect(container.textContent).toContain(es.helpManageDetail);
  expect(container.textContent).toContain(es.helpBreaksDetail);
  expect(container.textContent).not.toContain('siguen disponibles en FocusGuard clásico');
});

it('uses a localized native destination selector for narrow windows', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  const select = container.querySelector<HTMLSelectElement>('#mobile-navigation');
  expect(select).not.toBeNull();
  expect(container.querySelector('label[for="mobile-navigation"]')?.textContent).toBe(es.mobileNavigation);
  expect(container.querySelector('[role="tablist"]')?.getAttribute('aria-label')).toBe(es.mainNavigation);
  expect(Array.from(select!.options).map(option => option.textContent)).toEqual([
    es.summary, es.profiles, es.activity, es.schedules, es.preferences, es.connection, es.help,
  ]);
  expect(select!.querySelector<HTMLOptionElement>('option[value="profiles"]')?.disabled).toBe(true);
  await act(async () => { select!.value = 'help'; select!.dispatchEvent(new Event('change', { bubbles: true })); });
  expect(container.querySelector('.help-list')).not.toBeNull();
  expect(container.textContent).toContain(es.helpManageDetail);
});
it('uses one accessible vector icon family for navigation and theme controls', async () => {
  const test = fixture(active);
  await act(async () => root.render(<App client={test.client}/>));
  const tabs = Array.from(container.querySelectorAll<HTMLElement>('[role="tab"]'));
  expect(tabs.map(tab => tab.textContent?.trim())).toEqual([
    es.summary, es.profiles, es.activity, es.schedules, es.preferences, es.connection, es.help,
  ]);
  expect(tabs.every(tab => tab.querySelector('svg.nav-icon[aria-hidden="true"][focusable="false"]'))).toBe(true);
  const themeButton = container.querySelector<HTMLButtonElement>('.icon-button');
  expect(themeButton?.getAttribute('aria-label')).toContain(es.appearance);
  expect(themeButton?.querySelector('svg.theme-icon[aria-hidden="true"][focusable="false"]')).not.toBeNull();
});
it('translations have matching keys and formatting supports service durations up to thirty days', () => {
  expect(Object.keys(es).sort()).toEqual(Object.keys(en).sort());
  expect(formatRemaining(30 * 86400)).toBe('30d 00:00:00');
  expect(formatRemaining(60)).toBe('00:01:00');
});

it('shows authoritative current and next cycle phase without advancing it locally', async () => {
  const cycle = { ...active, status: { ...active.status!, mode: 'FocusCycle' as const,
    currentPhase: { index: 1, kind: 'ShortBreak' as const, focusCycleNumber: 1, remainingSeconds: 42, startUtc: '2026-10-04T12:00:00Z', endUtc: '2026-10-04T12:01:00Z' },
    nextPhase: { index: 2, kind: 'Focus' as const, focusCycleNumber: 2, remainingSeconds: 60, startUtc: '2026-10-04T12:01:00Z', endUtc: '2026-10-04T12:02:00Z' }, finalEndUtc: active.status!.endUtc } };
  const f = fixture(cycle);
  await act(async () => root.render(<App client={f.client}/>));
  expect(container.querySelector('.active-phase')?.textContent).toContain(es.ShortBreak);
  expect(container.querySelector('.active-phase')?.textContent).toContain('00:42');
  expect(container.querySelector('.active-phase')?.textContent).toContain(es.nextPhase);
  await act(async () => f.update({ ...cycle, phase: 'stale' }));
  expect(container.querySelector('.active-phase')?.textContent).toContain('00:42');
});

it('returns the page to its header when a new session starts from a scrolled review form', async () => {
  const originalDescriptor = Object.getOwnPropertyDescriptor(document, 'scrollingElement');
  const scrollingElement = { scrollTop: 0 } as HTMLElement;
  Object.defineProperty(document, 'scrollingElement', { configurable: true, value: scrollingElement });
  try {
    const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
      sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false } };
    const test = fixture(inactive);
    await act(async () => root.render(<App client={test.client}/>));
    scrollingElement.scrollTop = 360;
    await act(async () => test.update({ ...active, status: { ...active.status!, sessionState: 'Activating', remainingSeconds: 60,
      sessionId: '33333333-3333-4333-8333-333333333333' } }));
    expect(scrollingElement.scrollTop).toBe(0);
  } finally {
    if (originalDescriptor) Object.defineProperty(document, 'scrollingElement', originalDescriptor);
    else Reflect.deleteProperty(document, 'scrollingElement');
  }
});
it('warns once when a restarted service drops a running normal session', async () => {
  const running: ConnectionSnapshot = { ...active, status: { ...active.status!, endUtc: '2099-01-01T00:00:00Z' } };
  const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
    sessionId: null, profileId: null, startUtc: null, endUtc: null, enforcementConnected: false } };
  const test = fixture(running);
  await act(async () => root.render(<App client={test.client}/>));
  await act(async () => test.update({ ...running, phase: 'stale', error: 'ServiceUnavailable' }));
  expect(container.querySelector('.interruption-notice')).toBeNull();
  await act(async () => test.update(inactive));
  const notice = container.querySelector<HTMLElement>('.interruption-notice');
  expect(notice?.getAttribute('role')).toBe('alert');
  expect(notice?.textContent).toContain(es.interruptionNoticeTitle);
  expect(notice?.textContent).toContain(es.interruptionNoticeBody);
  const close = [...notice!.querySelectorAll('button')].find(button => button.textContent === es.close);
  await act(async () => close?.click());
  expect(container.querySelector('.interruption-notice')).toBeNull();
  await act(async () => test.update({ ...inactive, updatedAt: (inactive.updatedAt ?? 0) + 1000 }));
  expect(container.querySelector('.interruption-notice')).toBeNull();
});

it('does not report an interruption for stops, expiries or an already idle service', async () => {
  const running: ConnectionSnapshot = { ...active, status: { ...active.status!, endUtc: '2099-01-01T00:00:00Z' } };
  const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
    sessionId: null, profileId: null, startUtc: null, endUtc: null, enforcementConnected: false } };
  const test = fixture(inactive);
  await act(async () => root.render(<App client={test.client}/>));
  expect(container.querySelector('.interruption-notice')).toBeNull();
  await act(async () => test.update(running));
  await act(async () => test.update({ ...running, status: { ...running.status!, sessionState: 'Stopped', remainingSeconds: 0 } }));
  await act(async () => test.update(inactive));
  expect(container.querySelector('.interruption-notice')).toBeNull();
  await act(async () => test.update(active));
  await act(async () => test.update(inactive));
  expect(container.querySelector('.interruption-notice')).toBeNull();
});

it('says plainly that blocking was released once a strict session has ended', async () => {
  const strictActive: ConnectionSnapshot = { ...active, status: { ...active.status!, strictMode: true } };
  const test = fixture(strictActive);
  await act(async () => root.render(<App client={test.client}/>));
  const badge = () => container.querySelector<HTMLElement>('.session-heading .badge');
  expect(badge()?.textContent).toBe(es.strict);
  expect(badge()?.classList.contains('strict')).toBe(true);
  expect(container.textContent).not.toContain(es.strictEndedDetail);

  await act(async () => test.update({ ...strictActive, status: { ...strictActive.status!, sessionState: 'Expired', remainingSeconds: 0, enforcementConnected: false } }));
  expect(badge()?.textContent).toBe(es.strictEndedBadge);
  expect(badge()?.classList.contains('strict')).toBe(false);
  expect(container.textContent).toContain(es.strictEndedDetail);
  expect(container.querySelector('.strict-recovery-inline')).toBeNull();
});

it('returns the page to its header when the current session expires', async () => {
  const originalDescriptor = Object.getOwnPropertyDescriptor(document, 'scrollingElement');
  const scrollingElement = { scrollTop: 0 } as HTMLElement;
  Object.defineProperty(document, 'scrollingElement', { configurable: true, value: scrollingElement });
  try {
    const test = fixture(active);
    await act(async () => root.render(<App client={test.client}/>));
    scrollingElement.scrollTop = 360;
    await act(async () => test.update({ ...active, status: { ...active.status!, sessionState: 'Expired', remainingSeconds: 0, enforcementConnected: false } }));
    expect(scrollingElement.scrollTop).toBe(0);
  } finally {
    if (originalDescriptor) Object.defineProperty(document, 'scrollingElement', originalDescriptor);
    else Reflect.deleteProperty(document, 'scrollingElement');
  }
});

it('clears a restored WebView scroll offset on first render of the inactive session page', async () => {
  const originalDescriptor = Object.getOwnPropertyDescriptor(document, 'scrollingElement');
  const scrollingElement = { scrollTop: 360 } as HTMLElement;
  Object.defineProperty(document, 'scrollingElement', { configurable: true, value: scrollingElement });
  try {
    const inactive: ConnectionSnapshot = { ...active, status: { ...active.status!, sessionState: 'Inactive', remainingSeconds: 0,
      sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false } };
    const test = fixture(inactive);
    await act(async () => root.render(<App client={test.client}/>));
    expect(container.querySelector('.page-header')).not.toBeNull();
    expect(scrollingElement.scrollTop).toBe(0);
  } finally {
    if (originalDescriptor) Object.defineProperty(document, 'scrollingElement', originalDescriptor);
    else Reflect.deleteProperty(document, 'scrollingElement');
  }
});
