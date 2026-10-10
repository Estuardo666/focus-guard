import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ScheduleEditor } from './ScheduleEditor';
import { createDashboardClient, type DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import en from './locales/en.json';
import es from './locales/es.json';

const token = '11111111111141118111111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
const profile = { id: profileId, name: 'Saved profile', applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 0 };
const catalog = { items: [profile], offset: 0, total: 1, pilotEnabled: true };
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

const latestRequest = (messages: Record<string, unknown>[]) => messages.at(-1)!;

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  dashboard.stop();
  container.remove();
});

it.each([false, true])('explains read-only mode separately from unsupported schedule editing (pilot=%s)', async pilotEnabled => {
  const f = fixture();
  await act(async () => f.initialize());
  await act(async () => f.reply({ ...catalog, pilotEnabled }));
  let load!: Promise<void>;
  await act(async () => { load = dashboard.loadSchedules(); });
  await act(async () => { f.reply({ items: [], editingEnabled: false, timeZoneId: 'SA Pacific Standard Time' }); await load; });
  await act(async () => root.render(<ScheduleEditor dashboard={dashboard} connection={connection} language="en" t={key => en[key]}/>));
  expect(container.textContent).toContain(pilotEnabled ? en.scheduleReadOnlyHost : en.editorPilotRequired);
  expect(container.textContent).not.toContain(pilotEnabled ? en.editorPilotRequired : en.scheduleReadOnlyHost);
  expect([...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === en.newSchedule)?.disabled).toBe(true);
  expect(f.messages.some(message => message.method === 'schedule.save')).toBe(false);
});

it.each([
  { language: 'en' as const, copy: en },
  { language: 'es' as const, copy: es },
])('shows an explanatory empty state after a successful empty schedule read in $language', async ({ language, copy }) => {
  const f = fixture();
  await act(async () => f.initialize());
  const profilesRequest = latestRequest(f.messages);
  await act(async () => f.reply(catalog, null, profilesRequest));

  let load!: Promise<void>;
  await act(async () => { load = dashboard.loadSchedules(); });
  const schedulesRequest = latestRequest(f.messages);
  await act(async () => {
    f.reply({ items: [], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' }, null, schedulesRequest);
    await load;
  });

  await act(async () => root.render(<ScheduleEditor dashboard={dashboard} connection={connection} language={language} t={key => copy[key]}/>));
  const empty = container.querySelector<HTMLElement>('.schedule-empty-state');
  expect(empty?.getAttribute('role')).toBe('status');
  expect(empty?.querySelector('h3')?.textContent).toBe(copy.schedulesEmpty);
  expect(empty?.textContent).toContain(copy.schedulesEmptyDetail);
  expect(container.querySelector('[role="alert"]')).toBeNull();
});

it.each([
  { language: 'en' as const, copy: en },
  { language: 'es' as const, copy: es },
])('shows a failed schedule read instead of stale or empty data in $language', async ({ language, copy }) => {
  const f = fixture();
  await act(async () => f.initialize());
  const profilesRequest = latestRequest(f.messages);
  await act(async () => f.reply(catalog, null, profilesRequest));

  const staleSchedule = { id: '33333333-3333-4333-8333-333333333333', name: 'Old schedule', days: 62, startMinute: 540,
    timeZoneId: 'SA Pacific Standard Time', enabled: true, profileName: profile.name, durationMinutes: 30,
    enforcementEnabled: false, nextStartUtc: null, lastOccurrenceUtc: null, lastResult: '', profileId,
    applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationRuleCount: 1, blockedWebsiteRuleCount: 0, revision: 'A'.repeat(64) };
  let load!: Promise<void>;
  await act(async () => { load = dashboard.loadSchedules(); });
  const schedulesRequest = latestRequest(f.messages);
  await act(async () => {
    f.reply({ items: [staleSchedule], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' }, null, schedulesRequest);
    await load;
  });

  let refresh!: Promise<void>;
  await act(async () => { refresh = dashboard.loadSchedules(); });
  const refreshRequest = latestRequest(f.messages);
  await act(async () => {
    f.reply(null, 'ServiceUnavailable', refreshRequest);
    await refresh;
  });

  await act(async () => root.render(<ScheduleEditor dashboard={dashboard} connection={connection} language={language} t={key => copy[key]}/>));
  expect(container.querySelector('[role="alert"]')?.textContent).toContain(copy.schedulesUnavailable);
  expect(container.querySelector('.schedule-empty-state')).toBeNull();
  expect(container.querySelector('.schedule-card')).toBeNull();
  expect([...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === copy.newSchedule)?.disabled).toBe(true);
  expect([...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === copy.refreshSchedules)?.disabled).toBe(false);
  expect(f.messages.some(message => message.method === 'schedule.save')).toBe(false);
});

it('keeps schedule actions outside the scroll region and associated with the form', async () => {
  const f = fixture();
  await act(async () => f.initialize());
  const profilesRequest = latestRequest(f.messages);
  expect(profilesRequest.method).toBe('profiles.list');
  await act(async () => f.reply(catalog, null, profilesRequest));

  let schedulesLoad!: Promise<void>;
  await act(async () => { schedulesLoad = dashboard.loadSchedules(); });
  const schedulesRequest = latestRequest(f.messages);
  expect(schedulesRequest.method).toBe('schedules.list');
  await act(async () => {
    f.reply({ items: [], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' }, null, schedulesRequest);
    await schedulesLoad;
  });

  await act(async () => root.render(<ScheduleEditor dashboard={dashboard} connection={connection} language="en" t={key => en[key]}/>));
  const newSchedule = [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.trim() === en.newSchedule)!;
  expect(newSchedule.disabled).toBe(false);
  await act(async () => newSchedule.click());

  const dialog = container.querySelector<HTMLDialogElement>('dialog.schedule-review')!;
  const scrollBody = dialog.querySelector<HTMLElement>('.schedule-dialog-body')!;
  const form = scrollBody.querySelector<HTMLFormElement>('#schedule-entry-form')!;
  const footer = dialog.querySelector<HTMLElement>('.schedule-dialog-actions')!;
  const review = footer.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  expect(dialog.open).toBe(true);
  expect(scrollBody.getAttribute('role')).toBe('region');
  expect(scrollBody.tabIndex).toBe(0);
  expect(scrollBody.contains(form)).toBe(true);
  expect(scrollBody.contains(footer)).toBe(false);
  expect(review.form).toBe(form);
  expect(review.textContent).toBe(en.reviewSchedule);

  await act(async () => footer.querySelector<HTMLButtonElement>('button[type="button"]')!.click());
  expect(container.querySelector('dialog.schedule-review')).toBeNull();
  expect(f.messages.some(message => message.method === 'schedule.save')).toBe(false);
});

it('labels row actions and explains how one-minute schedules keep their scheduled end', async () => {
  const f = fixture();
  await act(async () => f.initialize());
  await act(async () => f.reply(catalog, null, latestRequest(f.messages)));
  let load!: Promise<void>;
  await act(async () => { load = dashboard.loadSchedules(); });
  const item = { id: '33333333-3333-4333-8333-333333333333', name: 'Morning', days: 62, startMinute: 540, timeZoneId: 'SA Pacific Standard Time', enabled: true,
    profileName: profile.name, durationMinutes: 50, enforcementEnabled: false, nextStartUtc: '2026-10-02T10:00:00Z', nextEndUtc: '2026-10-02T10:50:00Z', timeZoneIanaId: 'America/Bogota', lastOccurrenceUtc: null, lastResult: '',
    profileId, applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationRuleCount: 1, blockedWebsiteRuleCount: 0, revision: 'A'.repeat(64) };
  await act(async () => { f.reply({ items: [item], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' }, null, latestRequest(f.messages)); await load; });
  await act(async () => root.render(<ScheduleEditor dashboard={dashboard} connection={connection} language="en" t={key => en[key]}/>));
  expect(container.textContent).toContain(en.scheduleNextEnd);
  expect(container.textContent).toContain(new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'America/Bogota' }).format(new Date(item.nextEndUtc)));
  const press = async (label: string) => act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.trim() === label)!.click());
  const confirm = () => container.querySelector<HTMLButtonElement>('dialog .schedule-dialog-actions button:last-child')!.textContent;

  await press(en.deleteSchedule);
  expect(confirm()).toBe(en.deleteSchedule);
  await act(async () => container.querySelector<HTMLButtonElement>('dialog .schedule-dialog-actions button')!.click());
  await press(en.schedulePause);
  expect(confirm()).toBe(en.schedulePause);
  await act(async () => container.querySelector<HTMLButtonElement>('dialog .schedule-dialog-actions button')!.click());

  await press(en.newSchedule);
  const duration = container.querySelector<HTMLInputElement>('dialog input[type="number"]')!;
  expect(duration.min).toBe('1');
  expect(container.querySelector('#schedule-minimum-duration')?.textContent).toBe(en.scheduleMinimumDuration);
  expect(duration.getAttribute('aria-describedby')).toBe('schedule-minimum-duration');
  const setValue = (input: HTMLInputElement, value: string) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  await act(async () => { setValue(container.querySelector<HTMLInputElement>('dialog .schedule-form input')!, 'Morning'); setValue(duration, '1'); });
  await act(async () => container.querySelector<HTMLButtonElement>('dialog .schedule-dialog-actions button[type="submit"]')!.click());
  expect(container.querySelector('dialog .schedule-dialog-body')?.textContent).toContain(en.scheduleReviewDetail);
  expect(en.scheduleReviewDetail).toContain('only the remaining time');
  expect(es.scheduleReviewDetail).toContain('solo el tiempo restante');
  expect(f.messages.some(message => message.method === 'schedule.save')).toBe(false);
});
