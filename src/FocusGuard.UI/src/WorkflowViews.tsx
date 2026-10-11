import { lazy, Suspense, useEffect, useRef, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from 'react';
import { ArrowLeftIcon, ArrowRightIcon, CheckCircledIcon, ChevronRightIcon, ReloadIcon, TimerIcon } from '@radix-ui/react-icons';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import type { TodaySpan } from './native/workflows';
import type es from './locales/es.json';
import { SessionHistoryDetailLazy } from './SessionHistoryDetailLazy';
import { formatFocusSeconds } from './focus-time';
import { exitDialog } from './ZenControls';
const ScheduleEditor = lazy(() => import('./ScheduleEditor').then(module => ({ default: module.ScheduleEditor })));

type Copy = (key: keyof typeof es) => string;
type SessionGroup = { sessionId: string; profileName: string; spans: TodaySpan[]; start: number; end: number; focusSeconds: number; completed: boolean };

function localDateKey(date: Date): string {
  return `${date.getFullYear().toString().padStart(4, '0')}-${(date.getMonth() + 1).toString().padStart(2, '0')}-${date.getDate().toString().padStart(2, '0')}`;
}

function dateFromLocalKey(key: string): Date {
  const [year, month, day] = key.split('-').map(Number);
  return new Date(year!, month! - 1, day!);
}

function shiftLocalDate(key: string, days: number): string {
  const date = dateFromLocalKey(key);
  date.setDate(date.getDate() + days);
  return localDateKey(date);
}

function localDayRangeUtc(key: string): Readonly<{ startUtc: string; endUtc: string }> {
  const start = dateFromLocalKey(key);
  const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 1);
  return { startUtc: start.toISOString(), endUtc: end.toISOString() };
}

/** One entry per session (its focus and break phases together), in the order the service listed them. */
function groupSessions(timeline: readonly TodaySpan[]): SessionGroup[] {
  const groups = new Map<string, SessionGroup>();
  for (const span of timeline) {
    const start = Date.parse(span.startUtc), end = Date.parse(span.endUtc);
    const group = groups.get(span.sessionId) ?? { sessionId: span.sessionId, profileName: span.profileName, spans: [], start, end, focusSeconds: 0, completed: true };
    group.spans.push(span);
    group.start = Math.min(group.start, start); group.end = Math.max(group.end, end);
    if (span.kind === 'Focus') group.focusSeconds += Math.floor((end - start) / 1000);
    group.completed &&= span.completed;
    groups.set(span.sessionId, group);
  }
  return [...groups.values()];
}

export function WorkflowViews({ dashboard, connection, language, t, view, onRepeat }: {
  dashboard: DashboardClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy;
  view: 'activity' | 'schedules'; onRepeat?(replay: SessionReplayIntent): void;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [selectedDate, setSelectedDate] = useState(() => localDateKey(new Date()));
  const todayDate = localDateKey(new Date());
  useEffect(() => {
    if (view === 'schedules') { void dashboard.loadSchedules(); return; }
    const range = localDayRangeUtc(selectedDate);
    void dashboard.loadToday(range.startUtc, range.endUtc);
  }, [dashboard, view, selectedDate]);
  const locale = language === 'es' ? 'es-EC' : 'en-US';
  const time = (value: string | number) => new Intl.DateTimeFormat(locale, { timeStyle: 'short' }).format(new Date(value));
  const focusTime = (seconds: number) => formatFocusSeconds(seconds, t);
  const dayLabel = selectedDate === todayDate
    ? t('activityToday').replace('{date}', new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long' }).format(dateFromLocalKey(selectedDate)))
    : new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(dateFromLocalKey(selectedDate));
  const reload = () => { const range = localDayRangeUtc(selectedDate); void dashboard.loadToday(range.startUtc, range.endUtc); };
  const move = (days: number) => { setSelectedSessionId(null); setSelectedDate(date => shiftLocalDate(date, days)); };
  if (view === 'activity') {
    const sessions = state.today ? groupSessions(state.today.timeline) : [];
    return <section className="workflow-view activity-view" aria-label={t('activityTitle')}>
      <div className="activity-day-navigation" role="group" aria-label={t('activityDayNavigation')}>
        <button type="button" className="icon-action" aria-label={t('activityPreviousDay')} disabled={state.todayLoading} onClick={() => move(-1)}><ArrowLeftIcon aria-hidden="true" focusable="false"/></button>
        <h3 aria-live="polite">{dayLabel}</h3>
        <div className="activity-day-actions">
          <button type="button" className="icon-action" aria-label={t('activityNextDay')} disabled={state.todayLoading || selectedDate >= todayDate} onClick={() => move(1)}><ArrowRightIcon aria-hidden="true" focusable="false"/></button>
          <button type="button" className="icon-action activity-refresh" disabled={state.todayLoading} title={t('activityRefresh')} onClick={reload}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{state.todayLoading ? t('refreshing') : t('activityRefresh')}</span></button>
        </div>
      </div>
      {state.todayLoading && <p role="status" className="activity-loading">{t('activityLoading')}</p>}
      {state.todayError && <p className="notice" role="alert">{t('activityUnavailable')} <button type="button" disabled={state.todayLoading} onClick={reload}>{t('refresh')}</button></p>}
      {state.today && !state.todayLoading && !state.todayError && <>
        <div className="today-summary">
          <article className="today-focus"><span><TimerIcon aria-hidden="true" focusable="false"/>{t('focusTimeOnDay')}</span><strong>{focusTime(state.today.focusSeconds)}</strong></article>
          <article><span><CheckCircledIcon aria-hidden="true" focusable="false"/>{t('completedToday')}</span><strong>{state.today.completedSessions}</strong></article>
        </div>
        {sessions.length > 0 && <DayStrip sessions={sessions} time={time} label={t('activityTimeline')}/>}
        <section className="activity-timeline" aria-labelledby="timeline-heading">
          <h3 id="timeline-heading">{t('activitySessions')}</h3>
          {sessions.length === 0 ? <p className="activity-empty">{t('activityEmptyDay')}</p> : <ol>{sessions.map(session => <li key={session.sessionId} className="activity-session" data-completed={session.completed}>
            <span className="choice-card-icon" aria-hidden="true">{session.profileName.slice(0, 1).toUpperCase()}</span>
            <div className="activity-session-copy">
              <div className="timeline-title"><strong>{session.profileName}</strong><span className={`badge ${session.completed ? 'badge-done' : ''}`}>{session.completed ? t('sessionCompleted') : t('sessionEndedEarly')}</span></div>
              <p>{time(session.start)}–{time(session.end)} · {focusTime(session.focusSeconds)}</p>
              <PhaseBar spans={session.spans} t={t}/>
            </div>
            <button type="button" className="history-open" onClick={() => { setSelectedSessionId(session.sessionId); void dashboard.loadSessionSummary(session.sessionId); }}>{t('viewSessionSummary')}<ChevronRightIcon aria-hidden="true" focusable="false"/></button>
          </li>)}</ol>}
          {state.today.timelineTruncated && <p className="start-note">{t('activityTruncated')}</p>}
        </section>
      </>}
      {selectedSessionId && <Sheet close={() => setSelectedSessionId(null)} label={t('sessionSummary')}><Suspense fallback={<p role="status">{t('summaryLoading')}</p>}><SessionHistoryDetailLazy dashboard={dashboard} connection={connection} sessionId={selectedSessionId} language={language} t={t} close={() => exitDialog(document.querySelector<HTMLDialogElement>('dialog.history-sheet'), () => setSelectedSessionId(null))} onRepeat={onRepeat ?? (() => undefined)}/></Suspense></Sheet>}
    </section>;
  }

  return <Suspense fallback={<p role="status">{t('schedulesLoading')}</p>}><ScheduleEditor dashboard={dashboard} connection={connection} language={language} t={t}/></Suspense>;
}

/** The day at a glance: each session as a block on an hour ruler, its focus and break phases in their own colours. */
function DayStrip({ sessions, time, label }: { sessions: readonly SessionGroup[]; time(value: number): string; label: string }) {
  const hour = 3600_000;
  const first = Math.min(...sessions.map(session => session.start)), last = Math.max(...sessions.map(session => session.end));
  let from = Math.floor(first / hour) * hour - hour, to = Math.ceil(last / hour) * hour + hour;
  if (to - from < 6 * hour) { const pad = (6 * hour - (to - from)) / 2; from -= Math.ceil(pad / hour) * hour; to = from + 6 * hour; }
  const span = to - from;
  const step = span > 12 * hour ? 3 : span > 6 * hour ? 2 : 1;
  const ticks = Array.from({ length: Math.floor(span / (step * hour)) + 1 }, (_, index) => from + index * step * hour);
  const at = (value: number) => `${((value - from) / span) * 100}%`;
  return <figure className="day-strip" aria-label={label}>
    <div className="day-strip-track">{sessions.flatMap(session => session.spans.map((phase, index) => {
      const start = Date.parse(phase.startUtc), end = Date.parse(phase.endUtc);
      return <span key={`${session.sessionId}:${index}`} className={`day-strip-span ${phase.kind.toLowerCase()}`} style={{ left: at(start), width: `max(3px, ${((end - start) / span) * 100}%)` } as CSSProperties} title={`${session.profileName} · ${time(start)}–${time(end)}`}/>;
    }))}</div>
    <div className="day-strip-ticks" aria-hidden="true">{ticks.map(tick => <span key={tick} style={{ left: at(tick) }}>{time(tick)}</span>)}</div>
  </figure>;
}

/** A session's phases as a small proportional bar: focus in the accent, breaks lighter. */
function PhaseBar({ spans, t }: { spans: readonly TodaySpan[]; t: Copy }) {
  const total = spans.reduce((sum, span) => sum + Date.parse(span.endUtc) - Date.parse(span.startUtc), 0) || 1;
  return <span className="phase-bar" aria-label={spans.map(span => t(span.kind)).join(' · ')} role="img">{spans.map((span, index) => <span key={index} className={span.kind.toLowerCase()} style={{ flexGrow: (Date.parse(span.endUtc) - Date.parse(span.startUtc)) / total }}/>)}</span>;
}

/** A side sheet over the page; Escape or the backdrop closes it, and focus returns to what opened it. */
function Sheet({ close, label, children }: { close(): void; label: string; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = dialog.current, opener = document.activeElement as HTMLElement | null;
    if (element && !element.open && typeof element.showModal === 'function') element.showModal();
    return () => { element?.close?.(); if (opener?.isConnected) opener.focus(); };
  }, []);
  const leave = () => exitDialog(dialog.current, close);
  return <dialog ref={dialog} className="history-sheet" aria-label={label} onCancel={event => { event.preventDefault(); leave(); }} onClick={event => { if (event.target === event.currentTarget) leave(); }}>
    <div className="history-sheet-body">{children}</div>
  </dialog>;
}
