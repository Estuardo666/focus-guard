import { Suspense, useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { UserPreferences } from './native/preferences';
import type { SessionReplayIntent } from './native/replay';
import type es from './locales/es.json';
import { SessionHistoryDetailLazy } from './SessionHistoryDetailLazy';
import { formatSummaryFocusedMinutes } from './focus-time';
import { exitDialog } from './ZenControls';

type Copy = (key: keyof typeof es) => string;

export function SessionEndExperience({ dashboard, connection, sessionId, mode, language, t, onClose, onRepeat }: {
  dashboard: DashboardClient;
  connection: ConnectionSnapshot;
  sessionId: string;
  mode: UserPreferences['sessionEnd'];
  language: 'es' | 'en';
  t: Copy;
  onClose(): void;
  onRepeat(replay: SessionReplayIntent): void;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const summary = state.sessionSummary?.sessionId.toLowerCase() === sessionId.toLowerCase() ? state.sessionSummary : null;
  const [showSummary, setShowSummary] = useState(mode !== 'Notification');
  const dialog = useRef<HTMLDialogElement>(null);
  const closingForUnmount = useRef(false);
  const closeCallback = useRef(onClose);
  useEffect(() => { closeCallback.current = onClose; }, [onClose]);

  useEffect(() => {
    const element = dialog.current;
    if (!showSummary || !element || element.open) return;
    element.showModal();
    return () => {
      closingForUnmount.current = true;
      if (element.open) element.close();
      closingForUnmount.current = false;
    };
  }, [showSummary]);

  useEffect(() => {
    if (mode !== 'Notification' || showSummary) return;
    const timer = window.setTimeout(() => closeCallback.current(), 9000);
    return () => window.clearTimeout(timer);
  }, [mode, showSummary]);

  if (!summary) return null;

  return <>
    {mode === 'Notification' && !showSummary && <aside className="session-end-notice" role="status" aria-live="polite" aria-atomic="true">
      <div><strong>{t(summary.outcome === 'Completed' ? 'sessionCompleted' : 'sessionEndedEarly')}</strong><span>{formatSummaryFocusedMinutes(summary.focusedMinutes, summary.plannedMinutes, t)} · {summary.profileName}</span></div>
      <button type="button" className="session-end-open" onClick={() => setShowSummary(true)}>{t('viewSessionSummary')}</button>
      <button type="button" className="session-end-dismiss" aria-label={t('close')} onClick={onClose}>{t('close')}</button>
    </aside>}
    {showSummary && <dialog ref={dialog} className="session-end-dialog" data-presentation={mode} aria-label={t('sessionSummary')} onCancel={event => { event.preventDefault(); exitDialog(dialog.current, () => dialog.current?.close()); }} onClose={() => { if (!closingForUnmount.current) onClose(); }}>
      <Suspense fallback={<p role="status">{t('summaryLoading')}</p>}><SessionHistoryDetailLazy dashboard={dashboard} connection={connection} sessionId={sessionId} language={language} t={t}
        close={() => exitDialog(dialog.current, () => dialog.current?.close())}
        onRepeat={replay => { dialog.current?.close(); onRepeat(replay); }}/></Suspense>
    </dialog>}
  </>;
}
