import { useEffect, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { SessionReplayIntent } from './native/replay';
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
  return <section className="session-history-detail" aria-labelledby="history-detail-title">
    <div className="history-detail-heading"><div><h3 id="history-detail-title">{t('sessionSummary')}</h3><p>{t('historyFor')} · <span title={sessionId}>{sessionId.slice(0, 8)}</span></p></div><button type="button" onClick={close}>{t('close')}</button></div>
    {state.summaryLoading && <p role="status">{t('summaryLoading')}</p>}
    {state.summaryError && <p className="notice" role="alert">{t('summaryUnavailable')} <button type="button" onClick={() => void dashboard.loadSessionSummary(sessionId)}>{t('refresh')}</button></p>}
    {state.replayLoading && <p role="status">{t('replayLoading')}</p>}
    {state.replayError && <p className="notice" role="alert">{t('replayIntentError')} <button type="button" disabled={state.replayLoading} onClick={() => void dashboard.loadSessionIntent(sessionId)}>{t('refresh')}</button></p>}
    {summary && <><div className="summary-facts"><article><span>{t('sessionOutcome')}</span><strong>{t(summary.outcome === 'Completed' ? 'sessionCompleted' : 'sessionEndedEarly')}</strong></article><article><span>{t('profile')}</span><strong>{summary.profileName}</strong></article><article><span>{t('focusedTime')}</span><strong>{formatSummaryFocusedMinutes(summary.focusedMinutes, summary.plannedMinutes, t)}</strong></article>{summary.cycleMetrics && <><article><span>{t('completedFocusCycles')}</span><strong>{summary.cycleMetrics.completedFocusCycles}</strong></article><article><span>{t('completedBreaks')}</span><strong>{summary.cycleMetrics.completedBreaks}</strong></article></>}<article><span>{t('blockedAttempts')}</span><strong>{summary.blockedAttempts}</strong></article>{summary.outcome === 'Completed' && summary.streakDays > 0 && <article><span>{t('streak')}</span><strong>{summary.streakDays}</strong></article>}</div>
      {replay?.replayable && <button type="button" className="history-repeat" disabled={!canPrepareRepeat} onClick={() => onRepeat(replay)}>{t('repeatWithReview')}</button>}
      {!state.replayLoading && !state.replayError && replayRequestedFor === sessionId && replay && !replay.replayable && <p className="start-note">{t(replay.unavailableReason === 'MissingProfile' ? 'replayProfileMissing' : replay.unavailableReason === 'StrictReviewRequired' ? 'replayStrictUnavailable' : replay.unavailableReason === 'EnforcementConsentUnknown' ? 'replayConsentUnknown' : replay.unavailableReason === 'CycleConfigurationUnavailable' ? 'replayCycleUnavailable' : 'replayUnavailable')}</p>}
      {!state.replayLoading && !state.replayError && replayRequestedFor === sessionId && !replay && <p className="start-note">{t('replayUnavailable')}</p>}
      {replay?.replayable && !canPrepareRepeat && <p className="start-note">{t('replayNeedsReady')}</p>}
      {replay?.replayable && <p className="start-note">{t('replayCurrentProfile')}</p>}
      {summary.objective && <section className="session-objective"><h4>{t('sessionObjective')}</h4><p>{summary.objective}</p></section>}
      <section className="session-reflection" aria-labelledby={reflectionLabelId}>
        <label id={reflectionLabelId} htmlFor={reflectionInputId}>{t('sessionReflection')}</label>
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
      </section>
      <p className="start-note">{t('sessionEndedAt')} · {moment(summary.endedAtUtc)}</p>
      <h4>{t('capturedItems')}</h4>
      {summary.captures.length === 0 ? <p className="session-guidance">{t('capturedEmpty')}</p> : <ul className="captured-list">{summary.captures.map(capture => <li key={capture.id}><div><strong>{capture.text}</strong><small>{capture.sourceLabel} · {moment(capture.capturedAtUtc)}</small></div><div className="capture-actions">{capture.openable && <button type="button" disabled={!allowed} onClick={() => void dashboard.resolveCapture(sessionId, capture.id, 'open')}>{t('captureOpen')}</button>}<button type="button" disabled={!allowed} onClick={() => void dashboard.resolveCapture(sessionId, capture.id, 'dismiss')}>{t('captureDismiss')}</button></div></li>)}</ul>}
      {summary.capturesTruncated && <p className="start-note">{t('capturesTruncated')}</p>}
      {!allowed && summary.captures.length > 0 && <p className="start-note">{connection.phase !== 'ready' ? t('historyControlsNeedsReady') : connection.status?.strictMode ? t('historyControlsStrict') : t('historyControlsUnavailable')}</p>}
      {state.captureError && <p className="notice" role="alert">{t(state.captureError === 'CaptureOpenFailed' ? 'captureOpenFailed' : state.captureError === 'CaptureOpenAlreadyAttempted' || state.captureError === 'OutcomeUnknown' ? 'captureUnknown' : 'captureResolveError')} <button type="button" disabled={state.summaryLoading} onClick={() => void dashboard.loadSessionSummary(sessionId)}>{t('refresh')}</button></p>}
      {state.capturePhase === 'writing' && <p role="status">{t('captureResolving')}</p>}
      {state.capturePhase === 'unknown' && <p className="start-note">{t('captureNoRetry')}</p>}
    </>}
  </section>;
}
