import { useEffect, useRef, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type es from './locales/es.json';
import { exitDialog } from './ZenControls';

export function ActiveSessionControls({ dashboard, connection, language, t, refresh }: { dashboard: DashboardClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t(key: keyof typeof es): string; refresh(): Promise<void> }) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const trigger = useRef<HTMLButtonElement | null>(null);
  const status = connection.status;
  const active = !!status && ['Active', 'Activating'].includes(status.sessionState);
  const ready = connection.phase === 'ready';
  const allowed = ready && active && !status.strictMode && !!state.catalog?.pilotEnabled && !(state.phase === 'error' && state.controlAction) && ['idle', 'error', 'actionReview'].includes(state.phase);
  const breakActive = status?.mode === 'FocusCycle' && !!status.currentPhase && ['ShortBreak', 'LongBreak'].includes(status.currentPhase.kind);
  const review = state.controlReview;
  useEffect(() => { dashboard.observeStatus(connection.status, ready); }, [dashboard, connection.status, ready]);
  useEffect(() => { if (state.phase === 'controlled') void refresh(); }, [state.phase, refresh]);
  if (!active && !(state.controlAction && ['controlling', 'controlled', 'unknown'].includes(state.phase))) return null;
  const moment = (utc: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(utc));
  const reason = !ready ? 'controlNeedsReady' : status?.strictMode ? 'controlStrict' : !state.catalog?.pilotEnabled ? 'controlPilotRequired' : null;
  return <section className="session-controls" aria-label={t('sessionControls')}>
    <div className="control-buttons"><button type="button" disabled={!allowed} onClick={event => { trigger.current = event.currentTarget; dashboard.reviewControl('stop'); }}>{t('stopSession')}</button>{status?.mode === 'FocusCycle' && <button type="button" disabled={!allowed || !breakActive} onClick={event => { trigger.current = event.currentTarget; dashboard.reviewControl('skip'); }}>{t('skipBreak')}</button>}</div>
    {reason && <p className="start-note">{t(reason)}</p>}
    {!reason && status?.mode === 'FocusCycle' && !breakActive && <p className="start-note">{t('skipOnlyBreak')}</p>}
    {review && <ActionReview title={t(review.action === 'stop' ? 'stopReviewTitle' : 'skipReviewTitle')} confirmLabel={t(review.action === 'stop' ? 'stopSession' : 'skipBreak')} detail={t(review.action === 'stop' ? 'stopConsequences' : 'skipConsequences')} endLabel={t('currentSessionEnd')} end={moment(review.endUtc)} {...(review.phaseEndUtc ? { phaseEnd: moment(review.phaseEndUtc) } : {})} t={t} enabled={!!allowed} cancel={() => dashboard.cancelReview()} confirm={() => void dashboard.confirmControl()} trigger={trigger.current}/>}
    {state.phase === 'controlling' && <p role="status">{t('controllingSession')}</p>}
    {state.phase === 'controlled' && <p role="status">{t('controlAccepted')}</p>}
    {state.phase === 'unknown' && state.controlAction && <p className="notice" role="alert">{t('controlUnknown')} <button type="button" onClick={() => void refresh()}>{t('refresh')}</button></p>}
    {state.phase === 'error' && state.controlAction && <p className="notice" role="alert">{t(['SessionChanged', 'BreakChanged', 'ControlRejected', 'NormalSessionRequired'].includes(state.error ?? '') ? 'controlChanged' : 'controlError')}</p>}
  </section>;
}

function ActionReview({ title, confirmLabel, detail, endLabel, end, phaseEnd, t, enabled, cancel, confirm, trigger }: { title: string; confirmLabel: string; detail: string; endLabel: string; end: string; phaseEnd?: string; t(key: keyof typeof es): string; enabled: boolean; cancel(): void; confirm(): void; trigger: HTMLButtonElement | null }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const element = dialog.current; if (element && !element.open) element.showModal(); return () => { element?.close(); if (trigger?.isConnected) { if (!trigger.disabled) trigger.focus(); else trigger.closest('section')?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus(); } }; }, [trigger]);
  return <dialog ref={dialog} className="delete-review control-review" aria-labelledby="control-review-title" aria-describedby="control-review-detail" onCancel={event => { event.preventDefault(); exitDialog(dialog.current, cancel); }}><h3 id="control-review-title">{title}</h3><p id="control-review-detail">{detail}</p><dl><div><dt>{endLabel}</dt><dd>{end}</dd></div>{phaseEnd && <div><dt>{t('currentBreakEnd')}</dt><dd>{phaseEnd}</dd></div>}</dl><div className="review-actions"><button type="button" autoFocus onClick={() => exitDialog(dialog.current, cancel)}>{t('cancel')}</button><button type="button" className="primary-action" disabled={!enabled} onClick={confirm}>{confirmLabel}</button></div></dialog>;
}
