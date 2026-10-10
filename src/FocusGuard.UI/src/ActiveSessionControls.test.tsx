import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ActiveSessionControls } from './ActiveSessionControls';
import { createDashboardClient, type DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import es from './locales/es.json';

const id = '22222222-2222-4222-8222-222222222222';
const connection: ConnectionSnapshot = { phase: 'ready', updatedAt: 1, error: null, status: { serviceReady: true, extensionReady: true, sessionState: 'Active', sessionId: id, profileId: id, remainingSeconds: 180, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:03:00Z', strictMode: false, enforcementConnected: true, mode: 'FocusCycle', currentPhase: { index: 1, kind: 'ShortBreak', focusCycleNumber: 1, remainingSeconds: 60, startUtc: '2040-01-01T12:01:00Z', endUtc: '2040-01-01T12:02:00Z' } } };
let root: Root, container: HTMLDivElement, dashboard: DashboardClient;
const refresh = vi.fn(async () => undefined);
function fixture(pilot = true) {
  const target = new EventTarget(), sent: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(value) { sent.push(value as Record<string, unknown>); } };
  dashboard = createDashboardClient(transport); dashboard.start();
  const emit = (data: unknown) => target.dispatchEvent(new MessageEvent('message', { data }));
  const reply = (result: unknown) => { const request = sent.at(-1)!; emit({ version: 1, requestId: request.requestId, documentToken: request.documentToken, error: null, result }); };
  emit({ type: 'host.initialize', version: 1, documentToken: '11111111111141118111111111111111', title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  reply({ items: [], offset: 0, total: 0, pilotEnabled: pilot });
  return { sent, reply };
}
const render = async (value = connection) => act(async () => root.render(<ActiveSessionControls dashboard={dashboard} connection={value} language="es" t={key => es[key]} refresh={refresh}/>));
const confirmButton = () => container.querySelector<HTMLButtonElement>('dialog .review-actions .primary-action')!;
const button = (text: string) => Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(element => element.textContent === text)!;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  container = document.createElement('div'); document.body.append(container); root = createRoot(container); refresh.mockClear();
});
afterEach(async () => { await act(async () => root.unmount()); dashboard.stop(); container.remove(); });
it('stop requires distinct review, supports Escape and restores the trigger', async () => {
  const f = fixture(); await render(); const stop = button(es.stopSession);
  await act(async () => stop.click());
  expect(container.querySelector('dialog')?.textContent).toContain(es.stopConsequences);
  expect(f.sent.filter(m => m.method === 'session.stopNormal')).toHaveLength(0);
  await act(async () => container.querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true })));
  expect(container.querySelector('dialog')).toBeNull(); expect(document.activeElement).toBe(stop);
  await act(async () => stop.click()); expect(confirmButton().textContent).toBe(es.stopSession); await act(async () => confirmButton().click());
  expect(f.sent.filter(m => m.method === 'session.stopNormal')).toHaveLength(1);
  expect(stop.disabled).toBe(true);
  await act(async () => f.reply({ sessionId: id, action: 'Stopped' })); expect(refresh).toHaveBeenCalledTimes(1);
});
it('skip shows current break and deadline consequences; confirmation sends exact phase', async () => {
  const f = fixture(); await render(); await act(async () => button(es.skipBreak).click());
  expect(container.querySelector('dialog')?.textContent).toContain(es.skipConsequences);
  expect(container.querySelector('dialog')?.textContent).toContain(es.currentBreakEnd);
  expect(confirmButton().textContent).toBe(es.skipBreak);
  await act(async () => confirmButton().click());
  expect(f.sent.at(-1)?.method).toBe('session.skipBreak');
  expect(f.sent.at(-1)?.params).toMatchObject({ phaseIndex: 1, confirmed: true, sessionId: id.replaceAll('-', '') });
});
it('readonly, strict, stale and focus phase expose reasons and prevent unavailable actions', async () => {
  fixture(false); await render(); expect(button(es.stopSession).disabled).toBe(true); expect(container.textContent).toContain(es.controlPilotRequired);
  dashboard.stop(); fixture(); await render({ ...connection, status: { ...connection.status!, strictMode: true } });
  expect(button(es.stopSession).disabled).toBe(true); expect(container.textContent).toContain(es.controlStrict);
  await render({ ...connection, phase: 'stale' }); expect(button(es.skipBreak).disabled).toBe(true);
  await render({ ...connection, status: { ...connection.status!, currentPhase: { ...connection.status!.currentPhase!, kind: 'Focus' } } });
  expect(button(es.skipBreak).disabled).toBe(true); expect(button(es.stopSession).disabled).toBe(false);
});
it('a changed phase cancels review without sending a command', async () => {
  const f = fixture(); await render(); await act(async () => button(es.skipBreak).click());
  await render({ ...connection, status: { ...connection.status!, currentPhase: { ...connection.status!.currentPhase!, index: 2, kind: 'Focus' } } });
  expect(container.querySelector('dialog')).toBeNull(); expect(f.sent.filter(m => m.method === 'session.skipBreak')).toHaveLength(0);
});

it('does not show inactive session controls for an uncertain start operation', async () => {
  vi.useFakeTimers();
  try {
    const f = fixture();
    const inactive: ConnectionSnapshot = { ...connection, status: { serviceReady: true, extensionReady: true, sessionState: 'Inactive', sessionId: null, profileId: null, remainingSeconds: 0, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } };
    await render(inactive);
    await act(async () => {
      const preparing = dashboard.prepare(id, 1, false);
      f.reply({ preparationId: '33333333333343338333333333333333', profileId: id, profileName: 'Saved', durationSeconds: 60, enforcementEnabled: false, applicationRuleCount: 0, websiteRuleCount: 0, blockedApplicationCount: 0, blockedWebsiteCount: 0, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:01:00Z', expiresUtc: '2040-01-01T12:01:00Z', extensionReady: true });
      await preparing;
    });
    await act(async () => { const confirming = dashboard.confirm(); await vi.advanceTimersByTimeAsync(4001); await confirming; });
    expect(dashboard.getSnapshot().phase).toBe('unknown');
    expect(container.querySelector('.session-controls')).toBeNull();
  } finally { vi.useRealTimers(); }
});
