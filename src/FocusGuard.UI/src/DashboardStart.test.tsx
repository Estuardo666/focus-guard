import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { DashboardStart } from './DashboardStart';
import { createDashboardClient, type DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import { isStrictReview } from './native/strict';
import es from './locales/es.json';

const profileId = '22222222-2222-4222-8222-222222222222';
const defaultCycleId = '77777777-7777-4777-8777-777777777777';
const token = '11111111111141118111111111111111';
const idle: ConnectionSnapshot = { phase: 'ready', updatedAt: 1, error: null, status: { serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0, sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } };
const catalog = { items: [{ id: profileId, name: 'My saved profile', applicationRuleCount: 2, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 1, accentHex: '#AABBCC' }], offset: 0, total: 1, pilotEnabled: true };
const review = { preparationId: '33333333333343338333333333333333', profileId, profileName: 'Snapshot profile', durationSeconds: 1500, enforcementEnabled: false, applicationRuleCount: 2, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 1, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:25:00Z', expiresUtc: '2040-01-01T12:02:00Z', extensionReady: false };
const strictReplay: SessionReplayIntent = { sessionId: '55555555-5555-4555-8555-555555555555', available: true, replayable: true, unavailableReason: null,
  originalProfileName: 'My saved profile', profile: catalog.items[0]!, durationMinutes: 25, mode: 'SimpleTimer', strict: true,
  enforcementEnabled: false, cyclePresetId: null, cycleConfiguration: null, objective: 'Finish the draft' };
const strictReplayReview = (() => {
  const start = Date.now() + 10_000;
  const end = start + 1_500_000;
  return { preparationId: '66666666666646668666666666666666', profileId, profileName: 'My saved profile', mode: 'SimpleTimer', strict: true,
    durationSeconds: 1500, enforcementEnabled: false, applicationRuleCount: 2, websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 1,
    blockedApplications: [], blockedWebsites: [], breakApplicationRuleCount: 0, breakWebsiteRuleCount: 0, breakApplications: [], breakWebsites: [], rulesTruncated: false,
    presetName: null, presetId: null, configuration: null, startUtc: new Date(start).toISOString(), endUtc: new Date(end).toISOString(),
    expiresUtc: new Date(start + 120_000).toISOString(), phases: [], partialFinalPhase: false, extensionReady: true, recoveryDelaySeconds: 900,
    recoveryCode: 'ABCDE-12345-FEDCB-67890-ABCDF-12345-FEDCB-67890', confirmationText: 'My saved profile',
    recoveryHelperPath: 'C:\\Program Files\\FocusGuard\\FocusGuard.Recovery.exe', recoveryHelperAvailable: true };
})();
let root: Root;
let container: HTMLDivElement;
let dashboard: DashboardClient;
const refresh = vi.fn(async () => undefined);
function fixture() {
  const target = new EventTarget();
  const sent: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(message) { sent.push(message as Record<string, unknown>); } };
  dashboard = createDashboardClient(transport); dashboard.start();
  const reply = (result: unknown) => { const request = sent.at(-1)!; target.dispatchEvent(new MessageEvent('message', { data: { version: 1, requestId: request.requestId, documentToken: request.documentToken, error: null, result } })); };
  target.dispatchEvent(new MessageEvent('message', { data: { type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' } }));
  return { sent, reply };
}
const render = async (connection = idle, replay?: SessionReplayIntent) => act(async () => root.render(<DashboardStart dashboard={dashboard} connection={connection} language="es" t={key => es[key]} refresh={refresh} replay={replay ?? null}/>));
const button = (text: string) => Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent === text)!;
// The strict review panel is lazy and the cycle fixture is imported inside tests; transforming
// them on first use under a busy parallel run used to eat each test's 5 s budget.
beforeAll(async () => { await Promise.all([import('./StrictReviewPanel'), import('./native/cycle-fixture')]); }, 30_000);
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  container = document.createElement('div'); document.body.append(container); root = createRoot(container); refresh.mockClear();
});
afterEach(async () => { await act(async () => root.unmount()); dashboard.stop(); container.remove(); vi.useRealTimers(); });
it('requires explicit review then confirmation and shows the backend snapshot and exact end', async () => {
  const f = fixture(); f.reply(catalog); await render();
  expect(container.textContent).toContain('My saved profile');
  expect(container.querySelector<HTMLInputElement>('input[type=checkbox]')!.checked).toBe(false);
  expect(container.querySelector<HTMLElement>('.profile-rule-summary')?.style.getPropertyValue('--profile-accent')).toBe('#AABBCC');
  expect(container.querySelector('.primary-action')?.getAttribute('style')).toBeNull();
  const objective = container.querySelector<HTMLTextAreaElement>('.session-objective-input textarea')!;
  await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!; setter.call(objective, 'Read chapter four'); objective.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('session.prepareNormal');
  expect(f.sent.filter(m => m.method === 'session.startNormal')).toHaveLength(0);
  await act(async () => f.reply(review));
  expect(container.textContent).toContain('Snapshot profile');
  expect(container.textContent).toContain('Read chapter four');
  expect(container.textContent).toContain('Fin exacto confirmado por el servicio');
  expect(container.textContent).toContain(new Intl.DateTimeFormat('es-EC', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(review.endUtc)));
  expect(button('Confirmar e iniciar').disabled).toBe(false);
  await act(async () => button('Confirmar e iniciar').click());
  expect(f.sent.filter(m => m.method === 'session.startNormal')).toHaveLength(1);
  expect(f.sent.find(m => m.method === 'session.startNormal')?.params).toMatchObject({ objective: 'Read chapter four' });
  await act(async () => f.reply({ sessionId: '44444444-4444-4444-8444-444444444444', startUtc: review.startUtc, endUtc: review.endUtc }));
  expect(refresh).toHaveBeenCalledTimes(1);
  expect(button('Revisar sesión').disabled).toBe(true);
});
it('removes an expired normal review and explains that it must be prepared again', async () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2040-01-01T12:00:00Z'));
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('session.prepareNormal');
  await act(async () => f.reply(review));
  expect(button('Confirmar e iniciar').disabled).toBe(false);

  await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });

  expect(container.querySelector('#review-title')).toBeNull();
  expect(container.textContent).toContain(es.reviewUnavailable);
  const expiryAlert = container.querySelector('.notice[role="alert"]')!;
  const startForm = container.querySelector('form')!;
  expect(expiryAlert.compareDocumentPosition(startForm) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(document.activeElement).toBe(button(es.reviewSession));
  expect(f.sent.filter(message => message.method === 'session.startNormal')).toHaveLength(0);
});
it('refuses confirmation when the review expires immediately before the click', async () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2040-01-01T12:00:00Z'));
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await act(async () => f.reply(review));
  expect(button('Confirmar e iniciar').disabled).toBe(false);

  vi.setSystemTime(new Date(review.expiresUtc));
  await act(async () => { await dashboard.confirm(); });

  expect(f.sent.filter(message => message.method === 'session.startNormal')).toHaveLength(0);
  expect(container.querySelector('#review-title')).toBeNull();
  expect(container.textContent).toContain(es.reviewUnavailable);
  const expiryAlert = container.querySelector('.notice[role="alert"]')!;
  const startForm = container.querySelector('form')!;
  expect(expiryAlert.compareDocumentPosition(startForm) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
});
it('keeps the primary review action beside the title and explains read-only mode there', async () => {
  const f = fixture(); f.reply({ ...catalog, pilotEnabled: false }); await render();
  const action = button(es.reviewSession);
  expect(action.closest('.start-heading-controls')).not.toBeNull();
  expect(action.getAttribute('form')).toBe('focusguard-start-session');
  expect(action.getAttribute('aria-describedby')).toBe('start-review-hint');
  expect(container.querySelector('#start-review-hint')?.textContent).toBe(es.pilotRequired);
  expect(container.querySelector('#start-review-hint')?.tagName).toBe('P');
  expect(container.querySelector('#start-title')?.nextElementSibling?.textContent).toBe(es.startReadOnlyDetail);
  expect(container.querySelector('#focusguard-start-session .session-type')?.hasAttribute('disabled')).toBe(true);
  expect(container.querySelectorAll('#focusguard-start-session :disabled').length).toBeGreaterThan(0);
  expect(container.querySelectorAll('#focusguard-start-session button[type="submit"]')).toHaveLength(0);
});
it('submits the associated session form from the header review action', async () => {
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => button(es.reviewSession).click());
  expect(f.sent.at(-1)?.method).toBe('session.prepareNormal');
  expect(f.sent.at(-1)?.params).toMatchObject({ profileId: profileId.replaceAll('-', ''), durationSeconds: 1500, enforcementEnabled: false });
});
it('disables start without pilot, while stale, and during pending strict recovery', async () => {
  const f = fixture(); f.reply({ ...catalog, pilotEnabled: false }); await render();
  expect(button('Revisar sesión').disabled).toBe(true);
  expect(container.textContent).toContain(es.pilotRequired);
  const loading = dashboard.loadProfiles(); await act(async () => f.reply(catalog)); await loading;
  await render({ ...idle, phase: 'stale' }); expect(button('Revisar sesión').disabled).toBe(true);
  await render({ ...idle, status: { ...idle.status!, strictMode: true, sessionState: 'RecoveryPending' } }); expect(button('Revisar sesión').disabled).toBe(true);
  expect(f.sent.filter(m => m.method === 'session.prepareNormal')).toHaveLength(0);
});
it('shows the accepted local objective during an active session without exposing start controls', async () => {
  const f = fixture(); f.reply(catalog);
  const active = { ...idle, status: { ...idle.status!, sessionState: 'Active', sessionId: strictReplay.sessionId,
    profileId, strictMode: true, remainingSeconds: 1200 } };
  await render(active);
  expect(f.sent.at(-1)?.method).toBe('session.intent.get');
  await act(async () => f.reply(strictReplay));
  expect(container.textContent).toContain('Finish the draft');
  expect(container.textContent).toContain(es.sessionObjectiveLabel);
  expect(document.documentElement.style.getPropertyValue('--profile-accent')).toBe('#AABBCC');
  expect(document.documentElement.style.getPropertyValue('--accent')).toBe('');
  expect(container.querySelector('form')).toBeNull();
  expect(container.querySelector<HTMLElement>('.active-profile')?.style.getPropertyValue('--profile-accent')).toBe('#AABBCC');
  expect(f.sent.some(message => message.method === 'session.prepareNormal' || message.method === 'session.strict.prepare'
    || message.method === 'session.startNormal' || message.method === 'session.strict.activate')).toBe(false);
});
it('shows save-work and browser warnings for explicitly enabled blocking and allows cancelling review', async () => {
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => container.querySelector<HTMLInputElement>('.enforcement-toggle input[type=checkbox]')!.click());
  expect(container.textContent).toContain('Guarda tu trabajo');
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.params).toMatchObject({ enforcementEnabled: true });
  await act(async () => f.reply({ ...review, enforcementEnabled: true }));
  expect(container.textContent).toContain('El bloqueo web todavía no está confirmado');
  await act(async () => button('Volver').click());
  expect(dashboard.getSnapshot().review).toBeNull();
  expect(f.sent.filter(m => m.method === 'session.startNormal')).toHaveLength(0);
});
it('empty saved catalog offers actual configuration instead of creating fake profiles', async () => {
  const f = fixture(); f.reply({ ...catalog, items: [], total: 0 }); await render();
  expect(container.textContent).toContain('Crea uno en FocusGuard clásico');
  expect(container.querySelector('form')).toBeNull();
});

it('restores a strict history entry only as a draft and prepares a fresh strict review', async () => {
  expect(isStrictReview(strictReplayReview)).toBe(true);
  const f = fixture(); await render(idle, strictReplay);
  expect(f.sent.at(-1)?.method).toBe('profiles.list');
  await act(async () => f.reply(catalog));
  await render(idle, strictReplay);
  expect(dashboard.getSnapshot().catalog).toEqual(catalog);
  expect(container.querySelector<HTMLButtonElement>('.primary-action')?.disabled).toBe(false);
  expect(container.querySelector<HTMLInputElement>('.strict-mode-option input')?.checked).toBe(true);
  expect(container.querySelector<HTMLTextAreaElement>('.session-objective-input textarea')?.value).toBe('Finish the draft');
  expect(container.textContent).toContain(es.repeatStrictDraftReview);
  await act(async () => {
    container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await new Promise(resolve => window.setTimeout(resolve, 50));
  });
  expect({ phase: dashboard.getSnapshot().strictPhase, error: dashboard.getSnapshot().strictError }).toEqual({ phase: 'preparing', error: null });
  expect(f.sent.at(-1)?.method).toBe('session.strict.prepare');
  expect(f.sent.at(-1)?.params).toMatchObject({ profileId: profileId.replaceAll('-', ''), mode: 'SimpleTimer', durationSeconds: 1500, enforcementEnabled: false });
  expect(f.sent.filter(message => message.method === 'session.prepareNormal' || message.method === 'session.startNormal' || message.method === 'session.strict.activate')).toHaveLength(0);
  await act(async () => {
    f.reply(strictReplayReview);
  });
  expect(dashboard.getSnapshot().strictPhase).toBe('review');
  await vi.waitFor(async () => {
    await act(async () => { await new Promise(resolve => window.setTimeout(resolve, 0)); });
    expect(container.querySelector('dialog')?.textContent).toContain(strictReplayReview.recoveryCode);
  }, { timeout: 5000 });
  expect(container.textContent).toContain(es.strictReview);
});

it('reloads a valid page when deleting profiles empties the last page', async () => {
  const f = fixture(); f.reply({ ...catalog, total: 9 }); await render();
  const lastPage = dashboard.loadProfiles(8);
  await act(async () => f.reply({ ...catalog, offset: 8, total: 9 })); await lastPage;
  expect(dashboard.getSnapshot().catalog?.offset).toBe(8);
  const reloading = dashboard.loadProfiles(8);
  await act(async () => f.reply({ ...catalog, items: [], offset: 8, total: 5 })); await reloading;
  expect(f.sent.at(-1)?.params).toEqual({ offset: 0 });
  await act(async () => f.reply({ ...catalog, total: 5 }));
  expect(container.textContent).toContain('My saved profile');
});

it('cycle selection loads saved presets, reviews service phases and requires separate confirmation', async () => {
  const { cycleReview, configuration } = await import('./native/cycle-fixture');
  const f = fixture(); f.reply({ ...catalog, items: [{ ...catalog.items[0]!, defaultCyclePresetId: defaultCycleId,
    defaultCyclePresetName: 'Saved preset', defaultCyclePresetConfiguration: configuration }] }); await render();
  const radio = container.querySelectorAll<HTMLInputElement>('input[type=radio]')[1]!;
  await act(async () => radio.click());
  expect(f.sent.at(-1)?.method).toBe('cycles.list');
  await act(async () => f.reply({ items: [{ id: profileId, name: 'Other preset', isBuiltIn: false, configuration }], offset: 0, total: 9 }));
  expect(container.textContent).toContain('Saved preset');
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('session.prepareCycle');
  expect(f.sent.at(-1)?.params).toMatchObject({ presetId: defaultCycleId.replaceAll('-', ''), configuration: null, enforcementEnabled: false });
  expect(container.textContent).toContain(`${configuration.focusSeconds / 60} ${es.focusMinutes}`);
  expect(f.sent.filter(m => m.method === 'session.startCycle')).toHaveLength(0);
  await act(async () => f.reply({ ...cycleReview, presetName: 'Saved preset', breakApplicationRuleCount: 1,
    breakWebsiteRuleCount: 1, breakApplications: ['game.exe'], breakWebsites: ['games.example'] }));
  expect(container.querySelectorAll('.cycle-schedule li')).toHaveLength(3);
  expect(container.textContent).toContain(es.partialFinalPhase);
  expect(container.textContent).toContain('Reviewed cycle profile');
  const breakDisclosure = container.querySelector<HTMLDetailsElement>('.normal-review .strict-break-rules')!;
  expect(breakDisclosure.open).toBe(false);
  expect(breakDisclosure.querySelector('summary')?.textContent).toContain(es.breakRuleSummary);
  await act(async () => { breakDisclosure.open = true; breakDisclosure.dispatchEvent(new Event('toggle')); });
  expect(breakDisclosure.textContent).toContain('game.exe');
  expect(breakDisclosure.textContent).toContain('games.example');
  expect(breakDisclosure.textContent).toContain(es.strictBreakExceptionMeaning);
  await act(async () => button(es.confirmNormal).click());
  expect(f.sent.filter(m => m.method === 'session.startCycle')).toHaveLength(1);
  await act(async () => f.reply({ sessionId: profileId, startUtc: cycleReview.startUtc, endUtc: cycleReview.endUtc }));
  expect(refresh).toHaveBeenCalledTimes(1);
});

it('custom cycle keeps count and total termination exclusive and returns focus on cancellation', async () => {
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => container.querySelectorAll<HTMLInputElement>('input[type=radio]')[1]!.click());
  const { configuration, cycleReview } = await import('./native/cycle-fixture');
  await act(async () => f.reply({ items: [{ id: profileId, name: 'Saved', isBuiltIn: false, configuration }], offset: 0, total: 1 }));
  const details = container.querySelector<HTMLDetailsElement>('.cycle-fields details')!;
  await act(async () => { details.open = true; details.dispatchEvent(new Event('toggle')); });
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.params).toMatchObject({ presetId: null, configuration: { terminationMode: 'CycleCount', cycleCount: 4, totalDurationSeconds: null } });
  const config = { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4, terminationMode: 'CycleCount' as const, cycleCount: 4, totalDurationSeconds: null };
  let cursor = Date.parse(cycleReview.startUtc);
  const phases = Array.from({ length: 7 }, (_, index) => { const startUtc = new Date(cursor).toISOString(); cursor += (index % 2 === 0 ? 1500 : 300) * 1000; return { index, kind: index % 2 === 0 ? 'Focus' as const : 'ShortBreak' as const, focusCycleNumber: Math.floor(index / 2) + 1, startUtc, endUtc: new Date(cursor).toISOString() }; });
  await act(async () => f.reply({ ...cycleReview, configuration: config, phases, endUtc: new Date(cursor).toISOString(), partialFinalPhase: false }));
  expect(document.activeElement?.id).toBe('review-title');
  await act(async () => button(es.cancel).click());
  expect(document.activeElement).toBe(button(es.reviewSession));
  expect(f.sent.filter(m => m.method === 'session.startCycle')).toHaveLength(0);
});


it('explains why the review action is disabled when the duration or the cycle values are invalid', async () => {
  const f = fixture(); f.reply(catalog); await render();
  const minutes = container.querySelector<HTMLInputElement>('input[type=number]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(minutes, '0'); minutes.dispatchEvent(new Event('input', { bubbles: true })); });
  expect(button(es.reviewSession).disabled).toBe(true);
  expect(container.querySelector('#start-review-hint')?.textContent).toBe(es.startInvalidDuration);
  expect(button(es.reviewSession).getAttribute('aria-describedby')).toBe('start-review-hint');
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(minutes, '25'); minutes.dispatchEvent(new Event('input', { bubbles: true })); });
  expect(button(es.reviewSession).disabled).toBe(false);
  expect(container.querySelector('#start-review-hint')).toBeNull();
});
it('sends the duration chosen on the focus dial to the service when preparing the review', async () => {
  const f = fixture(); f.reply(catalog); await render();
  const slider = container.querySelector<HTMLElement>('[role=slider]')!;
  expect(slider.getAttribute('aria-valuenow')).toBe('25');
  await act(async () => button(`50 ${es.minutesShort}`).click());
  expect(container.querySelector<HTMLInputElement>('input[type=number]')!.value).toBe('50');
  expect(slider.getAttribute('aria-valuenow')).toBe('50');
  expect(button(`50 ${es.minutesShort}`).getAttribute('aria-pressed')).toBe('true');
  await act(async () => { slider.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageUp', bubbles: true })); });
  await act(async () => button(es.reviewSession).click());
  expect(f.sent.at(-1)?.method).toBe('session.prepareNormal');
  expect(f.sent.at(-1)?.params).toMatchObject({ durationSeconds: 55 * 60 });
});
it('disables the dial and quick durations while the session cannot be started', async () => {
  const f = fixture(); f.reply({ ...catalog, pilotEnabled: false }); await render();
  expect(container.querySelector('[role=slider]')?.getAttribute('aria-disabled')).toBe('true');
  expect(button(`25 ${es.minutesShort}`).disabled).toBe(true);
});
it('offers a five-minute start first so a procrastinating person can begin with the smallest commitment', async () => {
  const f = fixture(); f.reply(catalog); await render();
  const chips = Array.from(container.querySelectorAll<HTMLButtonElement>('.quick-durations button')).map(chip => chip.textContent);
  expect(chips[0]).toBe(`5 ${es.minutesShort}`);
  await act(async () => button(`5 ${es.minutesShort}`).click());
  await act(async () => button(es.reviewSession).click());
  expect(f.sent.at(-1)?.params).toMatchObject({ durationSeconds: 300 });
});
it('picks the profile from cards that show what each profile blocks', async () => {
  const second = { ...catalog.items[0]!, id: '88888888-8888-4888-8888-888888888888', name: 'Reading', applicationRuleCount: 4, websiteRuleCount: 9, blockedApplicationCount: 4, blockedWebsiteCount: 9 };
  const f = fixture(); f.reply({ ...catalog, items: [catalog.items[0]!, second], total: 2 }); await render();
  const cards = Array.from(container.querySelectorAll<HTMLLabelElement>('.choice-cards .choice-card'));
  expect(cards.map(card => card.querySelector('.choice-card-name')?.textContent)).toEqual(['My saved profile', 'Reading']);
  expect(cards[1]!.querySelector('.choice-card-meta')?.textContent).toBe('49');
  expect(container.querySelector('[role=radiogroup][aria-labelledby=setup-profile-label]')).not.toBeNull();
  await act(async () => cards[1]!.querySelector<HTMLInputElement>('input')!.click());
  await act(async () => button(es.reviewSession).click());
  expect(f.sent.at(-1)?.params).toMatchObject({ profileId: second.id.replaceAll('-', '') });
});
it('keeps blocking and strict mode side by side with their consequences behind info tips', async () => {
  const f = fixture(); f.reply(catalog); await render();
  const toggles = container.querySelector('.setup-toggles')!;
  expect(toggles.querySelectorAll('input[type=checkbox]')).toHaveLength(2);
  expect(container.querySelector<HTMLLabelElement>('label[for=setup-strict]')?.textContent).toBe(es.strictMode);
  const strictTip = toggles.querySelector('.strict-mode-option [role=tooltip]')!;
  expect(strictTip.textContent).toBe(es.strictModeDetail.replace('{minutes}', '15'));
  await act(async () => toggles.querySelector<HTMLButtonElement>('.strict-mode-option .info-tip-trigger')!.click());
  expect(container.querySelector<HTMLInputElement>('#setup-strict')!.checked).toBe(false);
});
it('builds a custom cycle with steppers and sends the configuration they show', async () => {
  const f = fixture(); f.reply(catalog); await render();
  await act(async () => container.querySelectorAll<HTMLInputElement>('input[type=radio]')[1]!.click());
  const { configuration } = await import('./native/cycle-fixture');
  await act(async () => f.reply({ items: [{ id: profileId, name: 'Saved', isBuiltIn: false, configuration }], offset: 0, total: 1 }));
  expect(container.querySelectorAll('.cycle-cards .choice-card')).toHaveLength(1);
  const details = container.querySelector<HTMLDetailsElement>('.cycle-custom')!;
  await act(async () => { details.open = true; details.dispatchEvent(new Event('toggle')); });
  const focusTile = container.querySelector('.cycle-tile[data-tone=focus]')!;
  await act(async () => focusTile.querySelectorAll<HTMLButtonElement>('.stepper-button')[1]!.click());
  expect(container.querySelector('.cycle-custom .cycle-total')?.textContent).toBe('2 h 15 min');
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.params).toMatchObject({ presetId: null, configuration: { focusSeconds: 1800, shortBreakSeconds: 300, cycleCount: 4 } });
});

const renderNoReview = async () => act(async () => root.render(<DashboardStart dashboard={dashboard} connection={idle} language="es" t={key => es[key]} refresh={refresh} replay={null} reviewBeforeStart={false}/>));
it('starts a normal session straight after the service prepares it when the review step is off', async () => {
  const f = fixture(); f.reply(catalog); await renderNoReview();
  expect(button(es.startNow)).toBeDefined();
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('session.prepareNormal');
  await act(async () => f.reply(review));
  expect(f.sent.filter(m => m.method === 'session.startNormal')).toHaveLength(1);
  expect(f.sent.find(m => m.method === 'session.startNormal')?.params).toMatchObject({ preparationId: review.preparationId });
  expect(container.querySelector('.normal-review')).toBeNull();
});
it('still shows the review when it carries a warning, and always for strict sessions', async () => {
  const f = fixture(); f.reply(catalog); await renderNoReview();
  await act(async () => container.querySelector<HTMLInputElement>('#setup-blocking')!.click());
  await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await act(async () => f.reply({ ...review, enforcementEnabled: true, extensionReady: false }));
  expect(f.sent.filter(m => m.method === 'session.startNormal')).toHaveLength(0);
  expect(container.querySelector('.normal-review')).not.toBeNull();
  await act(async () => button(es.cancel).click());
  await act(async () => container.querySelector<HTMLInputElement>('#setup-strict')!.click());
  expect(button(es.reviewStrictSession)).toBeDefined();
  expect(Array.from(container.querySelectorAll('button')).some(b => b.textContent === es.startNow)).toBe(false);
});
