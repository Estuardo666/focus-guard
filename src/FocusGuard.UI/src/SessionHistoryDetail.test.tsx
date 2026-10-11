import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { SessionHistoryDetail } from './SessionHistoryDetail';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import type { SessionSummary } from './native/history';
import type { ConnectionSnapshot } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import en from './locales/en.json';

const sessionId = '11111111-1111-4111-8111-111111111111';
const summary: SessionSummary = {
  sessionId, outcome: 'Completed', profileName: 'Focus', plannedMinutes: 25, focusedMinutes: 25,
  blockedAttempts: 2, streakDays: 0, endedAtUtc: '2026-10-06T18:00:00Z', objective: 'Read chapter four',
  reflection: 'Finished the draft.', captures: [], capturesTruncated: false,
};
const connection: ConnectionSnapshot = { phase: 'ready', updatedAt: 1, error: null, status: {
  serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0,
  sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false,
  enforcementConnected: false, mode: 'SimpleTimer',
} };

let root: Root;
let container: HTMLDivElement;
let dashboard: DashboardClient;
let saveReflection: ReturnType<typeof vi.fn>;
let snapshot: DashboardSnapshot;
const listeners = new Set<() => void>();

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  listeners.clear();
  snapshot = { sessionSummary: summary, summaryLoading: false, summaryError: null, sessionReplay: null,
    replayLoading: false, replayError: null, capturePhase: 'idle', captureError: null, catalog: { items: [], offset: 0, total: 0, pilotEnabled: true } } as unknown as DashboardSnapshot;
  saveReflection = vi.fn(async (_id: string, reflection: string) => {
    snapshot = { ...snapshot, sessionSummary: { ...summary, reflection } } as DashboardSnapshot;
    for (const listener of listeners) listener();
    return reflection;
  });
  dashboard = {
    subscribe(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); },
    getSnapshot: () => snapshot,
    loadSessionSummary: vi.fn(async () => summary),
    loadSessionIntent: vi.fn(async () => null),
    saveSessionReflection: saveReflection,
  } as unknown as DashboardClient;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

it('shows the local objective and saves an optional reflection back to the journal', async () => {
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  expect(container.querySelector('.history-detail-heading span')?.textContent).toBe(sessionId.slice(0, 8));
  expect(container.querySelector('.history-detail-heading span')?.getAttribute('title')).toBe(sessionId);
  expect(container.querySelector('.session-objective')?.textContent).toContain(summary.objective);
  expect(container.querySelector('.summary-facts')?.textContent).not.toContain(en.completedFocusCycles);
  expect(container.textContent).not.toContain(en.completedFocusCycles);
  const reflection = container.querySelector<HTMLTextAreaElement>('textarea')!;
  expect(reflection.value).toBe(summary.reflection);
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
    setter.call(reflection, 'The next step is ready.');
    reflection.dispatchEvent(new Event('input', { bubbles: true }));
  });
  const save = Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === en.saveReflection)!;
  expect(save.disabled).toBe(false);
  expect(container.querySelector('.reflection-saved')?.textContent).toBe('');
  await act(async () => save.click());
  expect(saveReflection).toHaveBeenCalledWith(sessionId, 'The next step is ready.');
  expect(container.querySelector('.reflection-saved')?.textContent).toBe(en.reflectionSaved);
  expect(reflection.value).toBe('The next step is ready.');
  expect(Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(button => button.textContent === en.saveReflection)?.disabled).toBe(true);
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
    setter.call(reflection, 'Edited again.');
    reflection.dispatchEvent(new Event('input', { bubbles: true }));
  });
  expect(container.querySelector('.reflection-saved')?.textContent).toBe('');
});

it('labels a completed sub-minute focus session without rounding it to zero', async () => {
  snapshot = { ...snapshot, sessionSummary: { ...summary, plannedMinutes: 1, focusedMinutes: 0 } } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  const focusedTime = Array.from(container.querySelectorAll('.summary-facts article'))
    .find(article => article.textContent?.includes(en.focusedTime));
  expect(focusedTime?.textContent).toContain(en.lessThanOneMinute);
});

it('shows cycle history counts only when the service has authoritative metrics', async () => {
  snapshot = { ...snapshot, sessionSummary: { ...summary, cycleMetrics: { completedFocusCycles: 2, completedBreaks: 1 } } } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));

  const facts = container.querySelector('.summary-facts')!;
  expect(facts.textContent).toContain(en.completedFocusCycles);
  expect(facts.textContent).toContain(en.completedBreaks);
  expect(Array.from(facts.querySelectorAll('article')).some(article => article.textContent?.includes(en.completedFocusCycles) && article.textContent.includes('2'))).toBe(true);
  expect(Array.from(facts.querySelectorAll('article')).some(article => article.textContent?.includes(en.completedBreaks) && article.textContent.includes('1'))).toBe(true);
});

it('shows completed focus cycles and breaks when the service has cycle history metrics', async () => {
  snapshot = { ...snapshot, sessionSummary: { ...summary, cycleMetrics: { completedFocusCycles: 2, completedBreaks: 1 } } } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));

  const facts = container.querySelector('.summary-facts')!;
  expect(facts.textContent).toContain(en.completedFocusCycles);
  expect(facts.textContent).toContain(en.completedBreaks);
  expect(Array.from(facts.querySelectorAll('article')).some(article => article.textContent?.includes(en.completedFocusCycles) && article.textContent.includes('2'))).toBe(true);
  expect(Array.from(facts.querySelectorAll('article')).some(article => article.textContent?.includes(en.completedBreaks) && article.textContent.includes('1'))).toBe(true);
});

it('explains that legacy consent is unknown and does not offer replay', async () => {
  snapshot = { ...snapshot, sessionReplay: {
    sessionId, available: true, replayable: false, unavailableReason: 'EnforcementConsentUnknown',
    originalProfileName: 'Focus', profile: null, durationMinutes: 25, mode: 'SimpleTimer', strict: false,
    enforcementEnabled: null, cyclePresetId: null, cycleConfiguration: null, objective: ''
  } as SessionReplayIntent } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));

  expect(container.textContent).toContain(en.replayConsentUnknown);
  expect(Array.from(container.querySelectorAll<HTMLButtonElement>('button')).some(button => button.textContent === en.repeatWithReview)).toBe(false);
});

it('keeps capture actions available after a strict session has ended even though the status still reports strict', async () => {
  const withCapture = { ...summary, captures: [{ id: '22222222-2222-4222-8222-222222222222', text: 'Reply to Ana', sourceLabel: 'Browser', capturedAtUtc: '2026-10-06T17:30:00Z', openable: false }] };
  snapshot = { ...snapshot, sessionSummary: withCapture } as unknown as DashboardSnapshot;
  const ended: ConnectionSnapshot = { ...connection, status: { ...connection.status!, sessionState: 'Expired', strictMode: true } };
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={ended} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  const dismiss = Array.from(container.querySelectorAll<HTMLButtonElement>('.capture-actions button')).find(button => button.textContent === en.captureDismiss)!;
  expect(dismiss.disabled).toBe(false);
  expect(container.textContent).not.toContain(en.historyControlsStrict);

  const active: ConnectionSnapshot = { ...connection, status: { ...connection.status!, sessionState: 'Active', strictMode: true, remainingSeconds: 60 } };
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={active} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  expect(Array.from(container.querySelectorAll<HTMLButtonElement>('.capture-actions button')).every(button => button.disabled)).toBe(true);
  expect(container.textContent).toContain(en.historyControlsStrict);
});

it('celebrates a completed session with badges earned only from recorded facts', async () => {
  snapshot = { ...snapshot, sessionSummary: { ...summary, streakDays: 3, blockedAttempts: 4, focusedMinutes: 50, plannedMinutes: 50 } } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  expect(container.querySelector('#history-detail-title')?.textContent).toBe(en.achievementCompletedTitle);
  const badges = Array.from(container.querySelectorAll('.achievement-badges li')).map(item => item.textContent);
  expect(badges).toEqual([en.badgeComplete, en.badgeStreak.replace('{days}', '3'), en.badgeShield.replace('{count}', '4'), en.badgeDeep, en.badgeIntent]);
  expect(container.querySelector('.achievement-burst')).not.toBeNull();
});

it('does not hand out completion badges for a session ended early and shows the share of the plan reached', async () => {
  snapshot = { ...snapshot, sessionSummary: { ...summary, outcome: 'Stopped', focusedMinutes: 10, plannedMinutes: 40, blockedAttempts: 0, objective: '', streakDays: 6 } } as DashboardSnapshot;
  await act(async () => root.render(<SessionHistoryDetail dashboard={dashboard} connection={connection} sessionId={sessionId}
    language="en" t={key => en[key]} close={vi.fn()} onRepeat={vi.fn()}/>));
  expect(container.querySelector('#history-detail-title')?.textContent).toBe(en.achievementEndedTitle);
  expect(container.querySelector('.achievement-subtitle')?.textContent).toBe(en.achievementEndedBody.replace('{percent}', '25'));
  expect(container.querySelector('.achievement-badges')).toBeNull();
  expect(container.querySelector('.achievement-burst')).toBeNull();
  expect(container.querySelector('.fact-streak')).toBeNull();
});
