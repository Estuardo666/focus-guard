import type { RefObject } from 'react';
import { useEffect, useSyncExternalStore } from 'react';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient } from './native/dashboard';
import type { WindowControlSnapshot, WindowControlsClient } from './native/window-controls';
import type es from './locales/es.json';
import { formatMoment, formatRemaining } from './formatting';

type Copy = (key: keyof typeof es) => string;

export function CompactFocus({ connection, dashboard, windowControls, windowState, language, t, refresh, expandButtonRef }: {
  connection: ConnectionSnapshot;
  dashboard: Pick<DashboardClient, 'subscribe' | 'getSnapshot' | 'loadSessionIntent'>;
  windowControls: Pick<WindowControlsClient, 'setCompactMode' | 'setAlwaysOnTop' | 'setDetailsExpanded'>;
  windowState: WindowControlSnapshot;
  language: 'es' | 'en';
  t: Copy;
  refresh(): Promise<void>;
  expandButtonRef: RefObject<HTMLButtonElement | null>;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const status = connection.status;
  const sessionId = status?.sessionId ?? null;
  const active = status !== null && ['Active', 'Activating'].includes(status.sessionState);
  const matchingIntent = sessionId && state.sessionReplay?.sessionId.toLowerCase() === sessionId.toLowerCase()
    ? state.sessionReplay : null;

  useEffect(() => {
    if (!windowState.compact || !active || !sessionId || matchingIntent) return;
    void dashboard.loadSessionIntent(sessionId);
  }, [active, dashboard, matchingIntent?.sessionId, sessionId, windowState.compact]);

  const profileName = matchingIntent?.profile?.name ?? matchingIntent?.originalProfileName
    ?? (status?.profileId ? state.catalog?.items.find(profile => profile.id.toLowerCase() === status.profileId?.toLowerCase())?.name : undefined);
  const phaseKind = status?.mode === 'FocusCycle' ? status.currentPhase?.kind : 'Focus';
  const remaining = status?.mode === 'FocusCycle'
    ? status.currentPhase?.remainingSeconds ?? null
    : status?.remainingSeconds ?? null;
  const recovering = status?.sessionState === 'RecoveryPending';
  const strictActive = status?.strictMode === true && active;
  const protectionWarning = active && matchingIntent?.enforcementEnabled === true && !status?.enforcementConnected;
  const connectionWarning = connection.phase !== 'ready';
  const windowBusy = windowState.phase === 'writing' || windowState.phase === 'waiting';

  useEffect(() => {
    if (windowState.compact) expandButtonRef.current?.focus();
  }, [expandButtonRef, windowState.compact]);

  return <section className="compact-focus" aria-labelledby="compact-title" hidden={!windowState.compact}>
    <header className="compact-focus-header">
      <div className="compact-brand"><span aria-hidden="true">FocusGuard</span><span className="compact-caption">{t('compactSession')}</span></div>
      <div className="compact-focus-actions">
        <button className="compact-details-toggle" type="button" disabled={windowBusy}
          aria-expanded={windowState.detailsExpanded} aria-controls="compact-details"
          aria-label={t(windowState.detailsExpanded ? 'collapseCompactDetails' : 'expandCompactDetails')}
          onClick={() => void windowControls.setDetailsExpanded(!windowState.detailsExpanded)}>
          {t(windowState.detailsExpanded ? 'hideDetails' : 'showDetails')}
        </button>
        <button ref={expandButtonRef} className="compact-expand" type="button" disabled={windowBusy}
          onClick={() => void windowControls.setCompactMode(false)}>{t('openFullView')}</button>
      </div>
    </header>
    {active && status ? <>
      <div className="compact-session-main">
        <div className="compact-session-context">
          <h1 id="compact-title">{phaseKind ? t(phaseKind) : t('phaseUnavailable')}</h1>
          <p>{profileName ?? (state.replayLoading ? t('compactProfileLoading') : t('compactProfileUnavailable'))}</p>
          {status.strictMode && <span className="badge strict">{t('strict')}</span>}
        </div>
        <p className="compact-timer" aria-label={`${t('remaining')}: ${remaining === null ? t('unknown') : formatRemaining(remaining)}`}>
          {remaining === null ? '—' : formatRemaining(remaining)}
        </p>
      </div>
    </> : <div className="compact-session-empty">
      <h1 id="compact-title">{recovering ? t('RecoveryPending') : connection.phase === 'ready' && status ? t('noSession') : connection.phase === 'loading' ? t('waiting') : t('statusUnavailableTitle')}</h1>
      <p>{connection.phase === 'ready' && status && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState)
        ? t('compactNoSessionDetail') : connection.phase === 'loading' ? t('loading') : t('compactUnavailableSummary')}</p>
    </div>}

    <div id="compact-details" className="compact-details" hidden={!windowState.detailsExpanded}>
      {active && status && <div className="compact-session-meta">
        {status.endUtc && <p><span>{t('compactEndsAt')}</span><strong>{formatMoment(status.endUtc, language)}</strong></p>}
        {status.mode === 'FocusCycle' && status.currentPhase?.endUtc &&
          <p><span>{t('currentPhase')}</span><strong>{formatMoment(status.currentPhase.endUtc, language)}</strong></p>}
        {matchingIntent?.objective && <p className="compact-objective"><span>{t('sessionObjectiveLabel')}</span><strong>{matchingIntent.objective}</strong></p>}
      </div>}
      <p className="compact-service-health"><span>{t('service')}</span> · {connection.phase === 'ready' ? t('connected') : connection.phase === 'stale' ? t('stale') : t('notConnected')}</p>
      <footer className="compact-focus-footer">
        <span>{t('compactCloseHint')}</span>
        <button type="button" aria-pressed={windowState.alwaysOnTop} disabled={windowBusy}
          onClick={() => void windowControls.setAlwaysOnTop(!windowState.alwaysOnTop)}>
          {t('alwaysOnTop')}
        </button>
      </footer>
    </div>

    <div className="compact-alerts">
      {connectionWarning && <div className="compact-alert" role="status">
        <p>{connection.phase === 'stale' ? t('staleDetail') : connection.phase === 'unauthorized' ? t('unauthorizedDetail') : t('unavailableDetail')}</p>
        <button type="button" onClick={() => void refresh()} disabled={connection.phase === 'loading'}>{t('refresh')}</button>
      </div>}
      {protectionWarning && <div className="compact-alert" role="status"><p>{t('compactProtectionWarning')}</p></div>}
      {strictActive && <div className="compact-alert strict" role="status"><p>{t('compactStrictWarning')}</p></div>}
      {recovering && <div className="compact-alert strict" role="status"><p>{t('compactRecoveryPending')}</p></div>}
      {windowState.phase === 'error' && <div className="compact-alert" role="alert"><p>{t('windowActionFailed')}</p></div>}
    </div>

  </section>;
}
