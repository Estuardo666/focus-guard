import { useEffect, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from 'react';
import { CheckIcon, Cross2Icon, LapTimerIcon, LightningBoltIcon, LockClosedIcon, Pencil1Icon, PersonIcon, ReloadIcon, ResumeIcon, TargetIcon, TimerIcon } from '@radix-ui/react-icons';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
import type { SessionSummary } from './native/history';
import type es from './locales/es.json';
import { formatSummaryFocusedMinutes } from './focus-time';

type Copy = (key: keyof typeof es) => string;
export function SessionHistoryDetail({ dashboard, connection, sessionId, language, t, close, onRepeat }: {
  dashboard: DashboardClient; connection: ConnectionSnapshot; sessionId: string; language: 'es' | 'en'; t: Copy; close(): void; onRepeat(replay: SessionReplayIntent): void;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const summary = state.sessionSummary?.sessionId.toLowerCase() === sessionId.toLowerCase() ? state.sessionSummary : null;
  const replay = state.sessionReplay?.sessionId.toLowerCase() === sessionId.toLowerCase() ? state.sessionReplay : null;
  const [replayRequestedFor, setReplayRequestedFor] = useState<string | null>(null);
  const [reflection, setReflection] = useState('');
  const [reflectionSaving, setReflectionSaving] = useState(false);
  const [reflectionError, setReflectionError] = useState(false);
  const [reflectionSaved, setReflectionSaved] = useState(false);
  const reflectionLabelId = `session-reflection-label-${sessionId}`;
  const reflectionInputId = `session-reflection-text-${sessionId}`;
  useEffect(() => { setReplayRequestedFor(sessionId); void dashboard.loadSessionIntent(sessionId); }, [dashboard, sessionId]);
  useEffect(() => {
    if (!summary) return;
    setReflection(summary.reflection);
    setReflectionError(false);
    setReflectionSaved(false);
  }, [summary?.sessionId, sessionId]);
  // strictMode lingers in the status after a strict session ends, so only the state list may gate this.
  const allowed = connection.phase === 'ready' && !!state.catalog?.pilotEnabled && !!connection.status
    && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(connection.status.sessionState)
    && state.capturePhase !== 'writing' && state.capturePhase !== 'unknown';
  const canPrepareRepeat = connection.phase === 'ready' && !!state.catalog?.pilotEnabled && !!connection.status
    && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(connection.status.sessionState)
    && !['unknown', 'preparing', 'starting', 'controlling', 'actionReview'].includes(state.phase);
  const moment = (utc: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(utc));
  const completed = summary?.outcome === 'Completed';
  const ratio = summary ? Math.min(1, summary.plannedMinutes > 0 ? summary.focusedMinutes / summary.plannedMinutes : 0) : 0;
  const shown = completed ? 1 : ratio;
  const badges = summary ? earnedBadges(summary, t) : [];
  return <section className="session-history-detail achievement" data-outcome={summary?.outcome ?? 'loading'} aria-labelledby="history-detail-title">
    <div className="history-detail-heading"><p className="achievement-kicker">{t('sessionSummary')} · <span title={sessionId}>{sessionId.slice(0, 8)}</span></p>
      <button type="button" className="achievement-close" aria-label={t('close')} onClick={close}><Cross2Icon aria-hidden="true" focusable="false"/></button></div>
    {state.summaryLoading && <p role="status">{t('summaryLoading')}</p>}
    {state.summaryError && <p className="notice" role="alert">{t('summaryUnavailable')} <button type="button" onClick={() => void dashboard.loadSessionSummary(sessionId)}>{t('refresh')}</button></p>}
    {state.replayLoading && <p role="status">{t('replayLoading')}</p>}
    {state.replayError && <p className="notice" role="alert">{t('replayIntentError')} <button type="button" disabled={state.replayLoading} onClick={() => void dashboard.loadSessionIntent(sessionId)}>{t('refresh')}</button></p>}
    {summary && <>
      <header className="achievement-hero">
        <div className="achievement-medal" aria-hidden="true">
          {completed && <span className="achievement-burst">{Array.from({ length: 12 }, (_, index) => <i key={index} style={{ '--i': index } as CSSProperties}/>)}</span>}
          <svg viewBox="0 0 120 120"><circle className="medal-track" cx="60" cy="60" r="52"/><circle className="medal-ring" cx="60" cy="60" r="52" pathLength={100} strokeDasharray={`${Math.max(shown * 100, 0.01)} 100`}/></svg>
          <span className="medal-core">{completed ? <CheckIcon/> : <TimerIcon/>}</span>
        </div>
        <h3 id="history-detail-title">{t(completed ? 'achievementCompletedTitle' : 'achievementEndedTitle')}</h3>
        <p className="achievement-subtitle">{completed ? t('achievementCompletedBody').replace('{profile}', summary.profileName) : t('achievementEndedBody').replace('{percent}', String(Math.round(ratio * 100)))}</p>
      </header>
      {badges.length > 0 && <section className="achievement-badges" aria-labelledby={`badges-${sessionId}`}>
        <h4 id={`badges-${sessionId}`}>{t('achievementsTitle')}</h4>
        <ul>{badges.map((badge, index) => <li key={badge.key} style={{ '--i': index } as CSSProperties}><span className="badge-icon" data-tone={badge.tone} aria-hidden="true">{badge.icon}</span><span>{badge.label}</span></li>)}</ul>
      </section>}
      <div className="summary-facts">
        <article className="fact-hero"><TimerIcon aria-hidden="true"/><span>{t('focusedTime')}</span><strong>{formatSummaryFocusedMinutes(summary.focusedMinutes, summary.plannedMinutes, t)}</strong><small>{t('achievementOfPlanned').replace('{minutes}', String(summary.plannedMinutes))}</small></article>
        <article><LockClosedIcon aria-hidden="true"/><span>{t('blockedAttempts')}</span><strong>{summary.blockedAttempts}</strong></article>
        {summary.outcome === 'Completed' && summary.streakDays > 0 && <article className="fact-streak"><FlameIcon/><span>{t('streak')}</span><strong>{summary.streakDays}</strong><span className="streak-days" aria-hidden="true">{Array.from({ length: 7 }, (_, index) => <i key={index} data-on={index < Math.min(summary.streakDays, 7)}/>)}</span></article>}
        {summary.cycleMetrics && <><article><LapTimerIcon aria-hidden="true"/><span>{t('completedFocusCycles')}</span><strong>{summary.cycleMetrics.completedFocusCycles}</strong></article><article><ResumeIcon aria-hidden="true"/><span>{t('completedBreaks')}</span><strong>{summary.cycleMetrics.completedBreaks}</strong></article></>}
        {!completed && <article className="fact-profile"><PersonIcon aria-hidden="true"/><span>{t('profile')}</span><strong>{summary.profileName}</strong></article>}
      </div>
      {summary.objective && <section className="session-objective"><h4><TargetIcon aria-hidden="true"/>{t('sessionObjective')}</h4><p>{summary.objective}</p></section>}
      <details className="session-reflection" open={!!summary.reflection || undefined}>
        <summary><Pencil1Icon aria-hidden="true"/>{summary.reflection ? t('sessionReflection') : t('reflectionAdd')}</summary>
        <label id={reflectionLabelId} htmlFor={reflectionInputId} className="sr-only">{t('sessionReflection')}</label>
        <textarea id={reflectionInputId} rows={3} maxLength={1000} value={reflection} placeholder={t('sessionReflectionPlaceholder')}
          onChange={event => { setReflection(event.target.value); setReflectionError(false); setReflectionSaved(false); }} disabled={reflectionSaving}/>
        <div className="session-reflection-actions"><small>{t('sessionReflectionLocal')}</small>
          <button type="button" disabled={reflectionSaving || reflection === summary.reflection} onClick={() => void (async () => {
            setReflectionSaving(true); setReflectionSaved(false);
            const saved = await dashboard.saveSessionReflection(sessionId, reflection);
            setReflectionSaving(false);
            if (saved === null) { setReflectionError(true); return; }
            setReflection(saved); setReflectionError(false); setReflectionSaved(true);
          })()}>{reflectionSaving ? t('reflectionSaving') : t('saveReflection')}</button>
        </div>
        <p className="reflection-saved" role="status">{reflectionSaved ? t('reflectionSaved') : ''}</p>
        {reflectionError && <p className="notice" role="alert">{t(connection.phase === 'ready' ? 'reflectionSaveUnavailable' : 'reflectionNeedsConnection')}</p>}
      </details>
      {(summary.captures.length > 0 || summary.capturesTruncated) && <section className="achievement-notes">
        <h4>{t('capturedItems')}</h4>
        <ul className="captured-list">{summary.captures.map(capture => <li key={capture.id}><div><strong>{capture.text}</strong><small>{capture.sourceLabel} · {moment(capture.capturedAtUtc)}</small></div><div className="capture-actions">{capture.openable && <button type="button" disabled={!allowed} onClick={() => void dashboard.resolveCapture(sessionId, capture.id, 'open')}>{t('captureOpen')}</button>}<button type="button" disabled={!allowed} onClick={() => void dashboard.resolveCapture(sessionId, capture.id, 'dismiss')}>{t('captureDismiss')}</button></div></li>)}</ul>
        {summary.capturesTruncated && <p className="start-note">{t('capturesTruncated')}</p>}
        {!allowed && summary.captures.length > 0 && <p className="start-note">{connection.phase !== 'ready' ? t('historyControlsNeedsReady') : connection.status?.strictMode ? t('historyControlsStrict') : t('historyControlsUnavailable')}</p>}
        {state.captureError && <p className="notice" role="alert">{t(state.captureError === 'CaptureOpenFailed' ? 'captureOpenFailed' : state.captureError === 'CaptureOpenAlreadyAttempted' || state.captureError === 'OutcomeUnknown' ? 'captureUnknown' : 'captureResolveError')} <button type="button" disabled={state.summaryLoading} onClick={() => void dashboard.loadSessionSummary(sessionId)}>{t('refresh')}</button></p>}
        {state.capturePhase === 'writing' && <p role="status">{t('captureResolving')}</p>}
        {state.capturePhase === 'unknown' && <p className="start-note">{t('captureNoRetry')}</p>}
      </section>}
      <footer className="achievement-actions">
        <div className="achievement-buttons">
          {replay?.replayable && <button type="button" className="history-repeat primary-action" disabled={!canPrepareRepeat} onClick={() => onRepeat(replay)}><ReloadIcon aria-hidden="true"/>{t('repeatWithReview')}</button>}
          <button type="button" className="ghost-action" onClick={close}>{t('close')}</button>
        </div>
        {!state.replayLoading && !state.replayError && replayRequestedFor === sessionId && replay && !replay.replayable && <p className="start-note">{t(replay.unavailableReason === 'MissingProfile' ? 'replayProfileMissing' : replay.unavailableReason === 'StrictReviewRequired' ? 'replayStrictUnavailable' : replay.unavailableReason === 'EnforcementConsentUnknown' ? 'replayConsentUnknown' : replay.unavailableReason === 'CycleConfigurationUnavailable' ? 'replayCycleUnavailable' : 'replayUnavailable')}</p>}
        {!state.replayLoading && !state.replayError && replayRequestedFor === sessionId && !replay && <p className="start-note">{t('replayUnavailable')}</p>}
        {replay?.replayable && !canPrepareRepeat && <p className="start-note">{t('replayNeedsReady')}</p>}
        {replay?.replayable && <p className="start-note">{t('replayCurrentProfile')}</p>}
        <p className="start-note achievement-ended">{t('sessionEndedAt')} · {moment(summary.endedAtUtc)}</p>
      </footer>
    </>}
  </section>;
}

type Badge = Readonly<{ key: string; label: string; icon: ReactNode; tone: 'gold' | 'violet' | 'teal' | 'rose' }>;

/** Only what the service actually recorded for this session earns a badge; nothing is invented to look generous. */
function earnedBadges(summary: SessionSummary, t: Copy): Badge[] {
  const badges: Badge[] = [];
  if (summary.outcome === 'Completed') badges.push({ key: 'complete', label: t('badgeComplete'), icon: <CheckIcon/>, tone: 'gold' });
  if (summary.outcome === 'Completed' && summary.streakDays >= 2) badges.push({ key: 'streak', label: t('badgeStreak').replace('{days}', String(summary.streakDays)), icon: <FlameIcon/>, tone: 'rose' });
  if (summary.blockedAttempts > 0) badges.push({ key: 'shield', label: summary.blockedAttempts === 1 ? t('badgeShieldOne') : t('badgeShield').replace('{count}', String(summary.blockedAttempts)), icon: <LockClosedIcon/>, tone: 'violet' });
  if (summary.focusedMinutes >= 45) badges.push({ key: 'deep', label: t('badgeDeep'), icon: <LightningBoltIcon/>, tone: 'teal' });
  if (summary.cycleMetrics && summary.cycleMetrics.completedFocusCycles >= 2) badges.push({ key: 'cycles', label: t('badgeCycles').replace('{count}', String(summary.cycleMetrics.completedFocusCycles)), icon: <LapTimerIcon/>, tone: 'teal' });
  if (summary.objective) badges.push({ key: 'intent', label: t('badgeIntent'), icon: <TargetIcon/>, tone: 'violet' });
  return badges;
}

function FlameIcon() {
  return <svg width="15" height="15" viewBox="0 0 15 15" aria-hidden="true" focusable="false"><path fill="currentColor" d="M7.6 1c.3 2.1-.7 3.2-1.7 4.3C5 6.3 4 7.3 4 9a3.5 3.5 0 0 0 7 0c0-1.3-.6-2.2-1.2-2.8.1 1-.3 1.7-1 2 .3-2.2-.2-4.7-1.2-7.2Z"/></svg>;
}
