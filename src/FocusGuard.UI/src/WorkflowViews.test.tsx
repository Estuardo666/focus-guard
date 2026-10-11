import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { WorkflowViews } from './WorkflowViews';
import { createDashboardClient, type DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import en from './locales/en.json';

const token = '11111111111141118111111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
const catalog = { items: [{ id: profileId, name: 'Saved profile', applicationRuleCount: 0, websiteRuleCount: 0, blockedApplicationCount: 0, blockedWebsiteCount: 0 }], offset: 0, total: 1, pilotEnabled: true };
const connection: ConnectionSnapshot = { phase: 'ready', updatedAt: 1, error: null, status: { serviceReady: true, extensionReady: true,
  sessionState: 'Inactive', remainingSeconds: 0, sessionId: null, profileId: null, startUtc: null, endUtc: null,
  strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } };

let root: Root;
let container: HTMLDivElement;
let dashboard: DashboardClient;

function fixture() {
  const target = new EventTarget();
  const messages: Record<string, unknown>[] = [];
  const transport: WebViewTransport = {
    addEventListener: target.addEventListener.bind(target),
    removeEventListener: target.removeEventListener.bind(target),
    postMessage(message) { messages.push(message as Record<string, unknown>); },
  };
  dashboard = createDashboardClient(transport);
  dashboard.start();
  const emit = (value: unknown) => target.dispatchEvent(new MessageEvent('message', { data: value }));
  const initialize = () => emit({ type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard',
    description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  const reply = (result: unknown, error: string | null = null, request = messages.at(-1)!) => emit({ version: 1,
    requestId: request.requestId, documentToken: request.documentToken, error, result });
  return { messages, initialize, reply };
}

const render = () => act(async () => root.render(<WorkflowViews dashboard={dashboard} connection={connection}
  language="en" t={key => en[key]} view="activity"/>));
const latestRequest = (messages: Record<string, unknown>[]) => messages.at(-1)!;
const dayRange = (year: number, month: number, day: number) => ({
  startUtc: new Date(year, month - 1, day).toISOString(),
  endUtc: new Date(year, month - 1, day + 1).toISOString(),
});
const summary = (focusSeconds: number) => ({ focusSeconds, completedSessions: focusSeconds > 0 ? 1 : 0, timeline: [], timelineTruncated: false });

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date(2026, 9, 6, 12));
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  dashboard.stop(); container.remove(); vi.useRealTimers();
});

it('loads one local calendar day at a time and never labels stale results as another date', async () => {
  const f = fixture();
  await act(async () => f.initialize());
  expect(latestRequest(f.messages).method).toBe('profiles.list');
  await act(async () => f.reply(catalog));
  await render();

  const todayRequest = latestRequest(f.messages);
  expect(todayRequest.method).toBe('today.get');
  expect(todayRequest.params).toEqual(dayRange(2026, 10, 6));
  await act(async () => f.reply(summary(1800), null, todayRequest));
  expect(container.querySelector('.activity-day-navigation h3')?.textContent).toContain('Today');
  expect(container.textContent).toContain('30 min');

  const previous = container.querySelector<HTMLButtonElement>('button[aria-label="Previous day"]')!;
  await act(async () => previous.click());
  const yesterdayRequest = latestRequest(f.messages);
  expect(yesterdayRequest.method).toBe('today.get');
  expect(yesterdayRequest.params).toEqual(dayRange(2026, 10, 5));
  expect(container.querySelector('.activity-day-navigation h3')?.textContent).not.toContain('Today');
  expect(container.textContent).not.toContain('30 min');
  expect(container.textContent).toContain(en.activityLoading);

  await act(async () => f.reply(summary(900), null, yesterdayRequest));
  expect(container.textContent).toContain('15 min');
  const next = container.querySelector<HTMLButtonElement>('button[aria-label="Next day"]')!;
  expect(next.disabled).toBe(false);
  await act(async () => next.click());
  const nextRequest = latestRequest(f.messages);
  expect(nextRequest.method).toBe('today.get');
  expect(nextRequest.params).toEqual(dayRange(2026, 10, 6));
  await act(async () => f.reply(summary(1200), null, nextRequest));
  expect(container.textContent).toContain('20 min');
  expect(container.querySelector<HTMLButtonElement>('button[aria-label="Next day"]')!.disabled).toBe(true);

  await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Previous day"]')!.click());
  const failedRequest = latestRequest(f.messages);
  await act(async () => f.reply(null, 'ServiceUnavailable', failedRequest));
  expect(container.querySelector('[role="alert"]')?.textContent).toContain(en.activityUnavailable);
  expect(container.textContent).not.toContain('20 min');
});

it('labels a positive sub-minute total as less than one minute', async () => {
  const f = fixture();
  await act(async () => f.initialize());
  await act(async () => f.reply(catalog));
  await render();
  const request = latestRequest(f.messages);
  await act(async () => f.reply(summary(55), null, request));
  expect(container.querySelector('.today-summary article')?.textContent).toContain(en.lessThanOneMinute);
});

it('groups a session\'s phases into one card, shows the day strip and opens the summary as a sheet', async () => {
  const f = fixture();
  await act(async () => f.initialize());
  await act(async () => f.reply(catalog));
  await render();
  const session = '33333333-3333-4333-8333-333333333333', other = '44444444-4444-4444-8444-444444444444';
  const at = (h: number, m: number) => new Date(2026, 9, 6, h, m).toISOString();
  const span = (sessionId: string, kind: string, a: string, b: string, completed = true) => ({ sessionId, profileName: 'Saved profile', kind, startUtc: a, endUtc: b, completed });
  await act(async () => f.reply({ focusSeconds: 3000, completedSessions: 1, timelineTruncated: false, timeline: [
    span(session, 'Focus', at(9, 0), at(9, 25)), span(session, 'ShortBreak', at(9, 25), at(9, 30)), span(session, 'Focus', at(9, 30), at(9, 55)),
    span(other, 'Focus', at(11, 0), at(11, 20), false),
  ] }, null, latestRequest(f.messages)));
  const cards = container.querySelectorAll('.activity-session');
  expect(cards).toHaveLength(2);
  expect(cards[0]!.getAttribute('data-completed')).toBe('true');
  expect(cards[0]!.textContent).toContain('50 min');
  expect(cards[0]!.querySelectorAll('.phase-bar > span')).toHaveLength(3);
  expect(cards[1]!.textContent).toContain(en.sessionEndedEarly);
  expect(container.querySelectorAll('.day-strip-span')).toHaveLength(4);
  await act(async () => cards[0]!.querySelector<HTMLButtonElement>('.history-open')!.click());
  expect(latestRequest(f.messages).method).toBe('session.summary');
  expect(container.querySelector('dialog.history-sheet')?.getAttribute('aria-label')).toBe(en.sessionSummary);
});
