import { useEffect, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { UserPreferences } from './native/preferences';
import type es from './locales/es.json';

type Copy = (key: keyof typeof es) => string;
export function PreferencesPanel({ dashboard, t, onSaved }: { dashboard: DashboardClient; t: Copy; onSaved(preferences: UserPreferences): void }) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [draft, setDraft] = useState<UserPreferences | null>(null);
  const saving = state.preferencesPhase === 'writing';
  useEffect(() => { if (state.preferences) setDraft(state.preferences); }, [state.preferences]);
  // Opening the tab retries a failed (or never-completed) startup read instead of waiting for a manual refresh.
  useEffect(() => {
    const current = dashboard.getSnapshot();
    if (!current.preferences && !current.preferencesLoading) void dashboard.loadPreferences();
  }, [dashboard]);
  const save = async () => { if (!draft || saving || state.preferencesPhase === 'unknown') return; const result = await dashboard.savePreferences(draft); if (result) onSaved(result); };
  const refresh = () => void dashboard.loadPreferences();
  const unavailable = state.preferencesError === 'PreferencesUnavailable' || state.preferencesError === 'AuthorizationUnavailable';
  const readFailed = state.preferencesPhase === 'idle';
  const errorText = state.preferencesPhase === 'unknown' ? t('preferencesUnknown') : unavailable ? t('preferencesUnavailable') : readFailed ? t('preferencesLoadError') : t('preferencesSaveError');
  return <section className="preferences-panel workflow-view" aria-labelledby="preferences-title">
    <div className="workflow-heading heading-actions-only"><div className="sr-only"><h2 id="preferences-title">{t('preferencesTitle')}</h2><p>{t('preferencesDetail')}</p></div><button type="button" disabled={state.preferencesLoading || saving} onClick={refresh}>{state.preferencesLoading ? t('refreshing') : t('refreshPreferences')}</button></div>
    {!draft && state.preferencesLoading && <p role="status">{t('preferencesLoading')}</p>}
    {state.preferencesError && <p className="notice" role="alert">{errorText}{state.preferencesPhase === 'unknown' && <button type="button" onClick={refresh}>{t('refresh')}</button>}</p>}
    {draft && <><div className="preferences-grid">
      <label>{t('reduceMotion')}<small>{t('reduceMotionDetail')}</small><input type="checkbox" checked={draft.reduceMotion} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, reduceMotion: event.target.checked } : current)}/></label>
      <label>{t('defaultBlocking')}<small>{t('defaultBlockingDetail')}</small><input type="checkbox" checked={draft.enforcementEnabled} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, enforcementEnabled: event.target.checked } : current)}/></label>
      <label>{t('defaultDuration')}<small>{t('defaultDurationDetail')}</small><input type="number" min="1" max="720" value={draft.lastDurationMinutes} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, lastDurationMinutes: Number(event.target.value) } : current)}/></label>
      <label>{t('defaultSessionMode')}<small>{t('defaultSessionModeDetail')}</small><select value={draft.lastSessionMode} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, lastSessionMode: event.target.value as UserPreferences['lastSessionMode'] } : current)}><option value="SimpleTimer">{t('timerOption')}</option><option value="FocusCycle">{t('cycleOption')}</option></select></label>
      <label>{t('sessionEndPresentation')}<small>{t('sessionEndPresentationDetail')}</small><select value={draft.sessionEnd} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, sessionEnd: event.target.value as UserPreferences['sessionEnd'] } : current)}><option value="Window">{t('sessionEndWindow')}</option><option value="Notification">{t('sessionEndNotification')}</option><option value="DimmedWindow">{t('sessionEndDimmed')}</option><option value="None">{t('sessionEndNone')}</option></select></label>
      <label className="preference-toggle">{t('cyclePhaseNotifications')}<small>{t('cyclePhaseNotificationsDetail')}</small><input type="checkbox" checked={draft.cyclePhaseNotifications} disabled={saving || state.preferencesPhase === 'unknown'} onChange={event => setDraft(current => current ? { ...current, cyclePhaseNotifications: event.target.checked } : current)}/></label>
    </div><p className="start-note">{t('preferencesSharedWithClassic')}</p><div className="review-actions"><button type="button" className="primary-action" disabled={saving || state.preferencesPhase === 'unknown' || !Number.isInteger(draft.lastDurationMinutes) || draft.lastDurationMinutes < 1 || draft.lastDurationMinutes > 720} onClick={() => void save()}>{saving ? t('savingPreferences') : t('savePreferences')}</button></div></>}
  </section>;
}
