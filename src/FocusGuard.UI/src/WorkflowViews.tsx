import { lazy, Suspense, useEffect, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import type es from './locales/es.json';
import { SessionHistoryDetailLazy } from './SessionHistoryDetailLazy';
import { formatFocusSeconds } from './focus-time';
const ScheduleEditor = lazy(() => import('./ScheduleEditor').then(module => ({ default: module.ScheduleEditor })));

type Copy = (key: keyof typeof es) => string;

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
  const time = (value: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(value));
  const focusTime = (seconds: number) => formatFocusSeconds(seconds, t);
  const dayLabel = selectedDate === todayDate
    ? t('activityToday').replace('{date}', new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'full' }).format(dateFromLocalKey(selectedDate)))
    : new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'full' }).format(dateFromLocalKey(selectedDate));
  if (view === 'activity') return <section className="workflow-view" aria-label={t('activityTitle')}>
    <div className="activity-day-navigation" role="group" aria-label={t('activityDayNavigation')}>
      <button type="button" aria-label={t('activityPreviousDay')} disabled={state.todayLoading} onClick={() => { setSelectedSessionId(null); setSelectedDate(date => shiftLocalDate(date, -1)); }}>←</button>
      <h3 aria-live="polite">{dayLabel}</h3>
      <div className="activity-day-actions"><button type="button" aria-label={t('activityNextDay')} disabled={state.todayLoading || selectedDate >= todayDate} onClick={() => { setSelectedSessionId(null); setSelectedDate(date => shiftLocalDate(date, 1)); }}>→</button><button type="button" className="activity-refresh" disabled={state.todayLoading} onClick={() => { const range = localDayRangeUtc(selectedDate); void dashboard.loadToday(range.startUtc, range.endUtc); }}>{state.todayLoading ? t('refreshing') : t('activityRefresh')}</button></div>
    </div>
    {state.todayLoading && <p role="status">{t('activityLoading')}</p>}
    {state.todayError && <p className="notice" role="alert">{t('activityUnavailable')} <button type="button" disabled={state.todayLoading} onClick={() => { const range = localDayRangeUtc(selectedDate); void dashboard.loadToday(range.startUtc, range.endUtc); }}>{t('refresh')}</button></p>}
    {state.today && !state.todayLoading && !state.todayError && <><div className="today-summary"><article><span>{t('focusTimeOnDay')}</span><strong>{focusTime(state.today.focusSeconds)}</strong></article><article><span>{t('completedToday')}</span><strong>{state.today.completedSessions}</strong></article></div>
      <section className="activity-timeline" aria-labelledby="timeline-heading"><h3 id="timeline-heading">{t('activityTimeline')}</h3>{state.today.timeline.length === 0 ? <p className="session-guidance">{t('activityEmptyDay')}</p> : <ol>{state.today.timeline.map((span, index) => <li key={`${span.sessionId}:${span.kind}:${span.startUtc}:${index}`}><span className={`timeline-mark ${span.kind.toLowerCase()}`} aria-hidden="true"/><div><div className="timeline-title"><strong>{span.profileName}</strong><span>{t(span.kind)}</span></div><p>{time(span.startUtc)}–{time(span.endUtc)} · {focusTime(Math.floor((Date.parse(span.endUtc) - Date.parse(span.startUtc)) / 1000))}</p><small>{span.completed ? t('sessionCompleted') : t('sessionEndedEarly')}</small>{state.today!.timeline.findIndex(candidate => candidate.sessionId === span.sessionId) === index && <button type="button" className="history-open" onClick={() => { setSelectedSessionId(span.sessionId); void dashboard.loadSessionSummary(span.sessionId); }}>{t('viewSessionSummary')}</button>}</div></li>)}</ol>}{state.today.timelineTruncated && <p className="start-note">{t('activityTruncated')}</p>}</section></>}
    {selectedSessionId && <Suspense fallback={<p role="status">{t('summaryLoading')}</p>}><SessionHistoryDetailLazy dashboard={dashboard} connection={connection} sessionId={selectedSessionId} language={language} t={t} close={() => setSelectedSessionId(null)} onRepeat={onRepeat ?? (() => undefined)}/></Suspense>}
  </section>;

  return <Suspense fallback={<p role="status">{t('schedulesLoading')}</p>}><ScheduleEditor dashboard={dashboard} connection={connection} language={language} t={t}/></Suspense>;
}
