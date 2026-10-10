import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { ScheduleSummary } from './native/workflows';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';

type Copy = (key: keyof typeof es) => string;
type Draft = { id: string; revision: string | null; name: string; days: number; startTime: string; timeZoneId: string; profileId: string; durationMinutes: number; enforcementEnabled: boolean; enabled: boolean; existing: ScheduleSummary | null };
type RowAction = { kind: 'delete' } | { kind: 'enabled'; value: boolean };
const defaultDays = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
const clockText = (minute: number) => `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`;

export function ScheduleEditor({ dashboard, connection, language, t }: { dashboard: DashboardClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy }) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const dialog = useRef<HTMLDialogElement>(null);
  const trigger = useRef<HTMLButtonElement | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [review, setReview] = useState(false);
  const [rowAction, setRowAction] = useState<{ schedule: ScheduleSummary; action: RowAction } | null>(null);
  const profiles = state.catalog?.items ?? [];
  const rows = state.schedulesError ? [] : state.schedules?.items ?? [];
  const editable = connection.phase === 'ready' && !!connection.status && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(connection.status.sessionState)
    && !!state.catalog?.pilotEnabled && state.schedules?.editingEnabled === true && !state.schedulesError && state.schedulePhase !== 'writing' && state.schedulePhase !== 'unknown';
  const busy = state.schedulesLoading || state.schedulePhase === 'writing';
  const currentProfile = profiles.find(profile => profile.id === draft?.profileId);
  const previousProfile = draft?.existing && draft.existing.profileId === draft.profileId ? draft.existing : null;
  const blockedApplications = currentProfile?.blockedApplicationCount ?? previousProfile?.blockedApplicationRuleCount ?? 0;
  const blockedWebsites = currentProfile?.blockedWebsiteCount ?? previousProfile?.blockedWebsiteRuleCount ?? 0;
  const dialogOpen = !!draft || !!rowAction;
  useEffect(() => { dashboard.observeStatus(connection.status, connection.phase === 'ready'); }, [dashboard, connection.status, connection.phase]);
  useEffect(() => { if (!state.catalog && !state.catalogLoading) void dashboard.loadProfiles(0); }, [dashboard, state.catalog, state.catalogLoading]);
  useEffect(() => { const element = dialog.current; if (dialogOpen && element && !element.open) element.showModal(); if (!dialogOpen && element?.open) element.close(); }, [dialogOpen]);
  useEffect(() => { if (!dialogOpen && trigger.current?.isConnected) trigger.current.focus(); }, [dialogOpen]);
  const openNew = (button: HTMLButtonElement) => {
    trigger.current = button;
    setDraft({ id: crypto.randomUUID(), revision: null, name: '', days: defaultDays, startTime: '09:00',
      timeZoneId: state.schedules?.timeZoneId ?? 'UTC', profileId: profiles[0]?.id ?? '', durationMinutes: 50,
      enforcementEnabled: false, enabled: false, existing: null });
    setReview(false);
  };
  const openEdit = (schedule: ScheduleSummary, button: HTMLButtonElement) => {
    if (!schedule.revision || !schedule.profileId) return;
    trigger.current = button;
    setDraft({ id: schedule.id, revision: schedule.revision, name: schedule.name, days: schedule.days,
      startTime: clockText(schedule.startMinute), timeZoneId: schedule.timeZoneId, profileId: schedule.profileId,
      durationMinutes: schedule.durationMinutes, enforcementEnabled: schedule.enforcementEnabled,
      enabled: schedule.enabled, existing: schedule });
    setReview(false);
  };
  const update = (patch: Partial<Draft>) => setDraft(current => current ? { ...current, ...patch } : current);
  const close = () => { setDraft(null); setRowAction(null); setReview(false); };
  const save = async () => {
    if (!draft || !editable || !draft.name.trim() || !draft.profileId) return;
    const parts = draft.startTime.split(':');
    const hour = Number(parts[0] ?? ''), minute = Number(parts[1] ?? '');
    if (!Number.isInteger(hour) || !Number.isInteger(minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59 || draft.enforcementEnabled && blockedApplications + blockedWebsites === 0) return;
    const result = await dashboard.saveSchedule({ scheduleId: draft.id, expectedRevision: draft.revision,
      name: draft.name.trim(), days: draft.days, startMinute: hour * 60 + minute, timeZoneId: draft.timeZoneId,
      profileId: draft.profileId, durationMinutes: draft.durationMinutes, enforcementEnabled: draft.enforcementEnabled, enabled: draft.enabled });
    if (result) close();
  };
  const applyRowAction = async () => {
    if (!rowAction || !rowAction.schedule.revision) return;
    const result = rowAction.action.kind === 'delete'
      ? await dashboard.deleteSchedule(rowAction.schedule.id, rowAction.schedule.revision)
      : await dashboard.setScheduleEnabled(rowAction.schedule.id, rowAction.schedule.revision, rowAction.action.value);
    if (result) close();
  };
  const dayLabels = Array.from({ length: 7 }, (_, day) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { weekday: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2024, 0, 7 + day))));
  const resultLabel = (value: string) => ({ Started: 'scheduleResultStarted', Busy: 'scheduleResultBusy', Missed: 'scheduleResultMissed', Claimed: 'scheduleResultClaimed', Rejected: 'scheduleResultRejected' } as const)[value as 'Started' | 'Busy' | 'Missed' | 'Claimed' | 'Rejected'] ?? (value ? 'scheduleResultOther' : 'notYet');
  const errorLabel = state.scheduleError === 'ScheduleConflict' || state.scheduleError === 'ScheduleMissing' ? t('scheduleConflict')
    : state.scheduleError === 'ScheduleOverlap' ? t('scheduleOverlap')
    : state.scheduleError === 'ScheduleLimit' ? t('scheduleLimit')
    : state.scheduleError === 'NoBlockingRules' ? t('noBlockingRules')
    : state.scheduleError === 'ProfileMissing' ? t('profileMissing')
    : state.scheduleError === 'ScheduleEditorUnavailable' || state.scheduleError === 'Unauthorized' ? t('scheduleEditorUnavailable')
    : state.scheduleError === 'OutcomeUnknown' ? t('scheduleUnknown')
    : state.scheduleError ? t('scheduleSaveError') : null;
  const formatNext = (value: string | null, timeZoneIanaId?: string | null) => value ? new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', {
    dateStyle: 'medium', timeStyle: 'short', ...(timeZoneIanaId ? { timeZone: timeZoneIanaId } : {})
  }).format(new Date(value)) : t('scheduleNoNext');
  const selectedOption = profiles.find(profile => profile.id === draft?.profileId);
  const errorNotice = errorLabel ? <p role="alert" className="notice">{errorLabel} <button type="button" disabled={busy} onClick={() => void dashboard.loadSchedules()}>{t('refresh')}</button></p> : null;
  return <section className="schedule-manager" aria-label={t('schedulesTitle')}>
    <div className="workflow-heading heading-actions-only"><div className="sr-only"><h2>{t('schedulesTitle')}</h2><p>{t('schedulesDetail')}</p></div><div className="schedule-actions"><button type="button" disabled={state.schedulesLoading} onClick={() => void dashboard.loadSchedules()}>{state.schedulesLoading ? t('refreshing') : t('refreshSchedules')}</button><button type="button" className="primary-action" disabled={!editable || busy || !profiles.length} onClick={event => openNew(event.currentTarget)}>{t('newSchedule')}</button></div></div>
    {errorNotice}
    {state.schedulesError && <p className="notice" role="alert">{t('schedulesUnavailable')}</p>}
    {state.catalog && !state.catalog.pilotEnabled
      ? <p className="start-note">{t('editorPilotRequired')}</p>
      : state.schedules && state.schedules.editingEnabled !== true && <p className="start-note">{t('scheduleReadOnlyHost')}</p>}
    {state.schedulesLoading && <p role="status">{t('schedulesLoading')}</p>}
    {state.schedules && !state.schedulesLoading && !state.schedulesError && rows.length === 0 && <div className="schedule-empty-state" role="status" aria-labelledby="schedule-empty-title">
      <h3 id="schedule-empty-title">{t('schedulesEmpty')}</h3>
      <p>{t('schedulesEmptyDetail')}</p>
    </div>}
    <div className="schedule-list">{rows.map(schedule => <article className="schedule-card" key={schedule.id}>
      <div className="schedule-title"><h3>{schedule.name}</h3><span className={`badge ${schedule.enabled ? 'schedule-enabled' : ''}`}>{t(schedule.enabled ? 'scheduleEnabled' : 'schedulePaused')}</span></div>
      <p>{dayLabels.filter((_, day) => schedule.days & (1 << day)).join(' · ')} · {clockText(schedule.startMinute)} · {schedule.timeZoneId}</p>
      <p>{schedule.profileName} · {schedule.durationMinutes} {t('minutesShort')}{schedule.enforcementEnabled ? ` · ${t('enableBlocking')}` : ` · ${t('timerOnly')}`}</p>
      <dl><div><dt>{t('scheduleNextStart')}</dt><dd>{formatNext(schedule.nextStartUtc, schedule.timeZoneIanaId)}</dd></div><div><dt>{t('scheduleNextEnd')}</dt><dd>{schedule.nextEndUtc === undefined ? (schedule.nextStartUtc ? t('scheduleEndUnavailable') : t('scheduleNoNext')) : formatNext(schedule.nextEndUtc, schedule.timeZoneIanaId)}</dd></div><div><dt>{t('scheduleLastResult')}</dt><dd>{t(resultLabel(schedule.lastResult))}</dd></div></dl>
      <div className="schedule-actions"><button type="button" disabled={!editable || busy || !schedule.revision} onClick={event => { trigger.current = event.currentTarget; setRowAction({ schedule, action: { kind: 'enabled', value: !schedule.enabled } }); }}>{t(schedule.enabled ? 'schedulePause' : 'scheduleEnable')}</button><button type="button" disabled={!editable || busy || !schedule.revision || !schedule.profileId} onClick={event => openEdit(schedule, event.currentTarget)}>{t('editSchedule')}</button><button type="button" className="danger-action" disabled={!editable || busy || !schedule.revision} onClick={event => { trigger.current = event.currentTarget; setRowAction({ schedule, action: { kind: 'delete' } }); }}>{t('deleteSchedule')}</button></div>
    </article>)}</div>
    {(draft || rowAction) && <dialog ref={dialog} className="delete-review schedule-review" aria-labelledby="schedule-review-title" onCancel={event => { event.preventDefault(); close(); }}>
      {draft && !review ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{draft.existing ? t('editSchedule') : t('newSchedule')}</h2>
          <form id="schedule-entry-form" className="schedule-form" onSubmit={event => { event.preventDefault(); setReview(true); }}>
            <label>{t('scheduleName')}<input autoFocus maxLength={80} required value={draft.name} disabled={!editable || busy} onChange={event => update({ name: event.target.value })}/></label>
            <label>{t('chooseProfile')}<select required value={draft.profileId} disabled={!editable || busy} onChange={event => update({ profileId: event.target.value, enforcementEnabled: false })}>
              {draft.existing && !profiles.some(profile => profile.id === draft.existing!.profileId) && <option value={draft.existing.profileId ?? ''}>{draft.existing.profileName} · {t('scheduleSavedProfile')}</option>}
              {profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}
            </select></label>
            {state.catalog && state.catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={!state.catalog.offset || state.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, state.catalog!.offset - 8))}>{t('previous')}</button><span>{state.catalog.offset + 1}–{Math.min(state.catalog.offset + 8, state.catalog.total)} / {state.catalog.total}</span><button type="button" disabled={state.catalog.offset + 8 >= state.catalog.total || state.catalogLoading} onClick={() => void dashboard.loadProfiles(state.catalog!.offset + 8)}>{t('next')}</button></div>}
            <fieldset disabled={!editable || busy}><legend>{t('scheduleDays')}</legend><div className="schedule-days">{dayLabels.map((label, day) => <label key={day}><input type="checkbox" checked={(draft.days & (1 << day)) !== 0} onChange={event => update({ days: event.target.checked ? draft.days | (1 << day) : draft.days & ~(1 << day) })}/><span>{label}</span></label>)}</div></fieldset>
            <div className="schedule-form-grid"><label>{t('scheduleTime')}<input type="time" required value={draft.startTime} disabled={!editable || busy} onChange={event => update({ startTime: event.target.value })}/></label><label>{t('durationLabel')}<input type="number" min="1" max="720" required aria-describedby="schedule-minimum-duration" value={draft.durationMinutes} disabled={!editable || busy} onChange={event => update({ durationMinutes: Number(event.target.value) })}/><small id="schedule-minimum-duration">{t('scheduleMinimumDuration')}</small></label></div>
            <p className="start-note">{t('scheduleTimeZone')}: {draft.timeZoneId}</p>
            <label className="blocking-option"><input type="checkbox" checked={draft.enforcementEnabled} disabled={!editable || busy || blockedApplications + blockedWebsites === 0} onChange={event => update({ enforcementEnabled: event.target.checked })}/><span>{t('enableBlocking')}<small>{selectedOption ? `${countLabel(selectedOption.blockedApplicationCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · ${countLabel(selectedOption.blockedWebsiteCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}` : draft.existing ? `${countLabel(draft.existing.blockedApplicationRuleCount ?? 0, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · ${countLabel(draft.existing.blockedWebsiteRuleCount ?? 0, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}` : t('noSelection')}</small></span></label>
            <label className="blocking-option"><input type="checkbox" checked={draft.enabled} disabled={!editable || busy} onChange={event => update({ enabled: event.target.checked })}/><span>{t('scheduleAutoStartConsent')}<small>{t('scheduleAutoStartDetail')}</small></span></label>
          </form>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" disabled={busy} onClick={close}>{t('cancel')}</button><button type="submit" form="schedule-entry-form" className="primary-action" disabled={!editable || busy || !draft.name.trim() || !draft.days || !draft.profileId || !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(draft.startTime) || draft.enforcementEnabled && blockedApplications + blockedWebsites === 0}>{t('reviewSchedule')}</button></div>
      </> : draft ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{t('reviewSchedule')}</h2><p>{t('scheduleReviewDetail')}</p><dl><div><dt>{t('scheduleName')}</dt><dd>{draft.name}</dd></div><div><dt>{t('scheduleTime')}</dt><dd>{draft.startTime} · {dayLabels.filter((_, day) => draft.days & (1 << day)).join(' · ')} · {draft.timeZoneId}</dd></div><div><dt>{t('chooseProfile')}</dt><dd>{selectedOption?.name ?? draft.existing?.profileName}</dd></div><div><dt>{t('durationLabel')}</dt><dd>{draft.durationMinutes} {t('minutesShort')}</dd></div><div><dt>{t('scheduleAutoStartConsent')}</dt><dd>{draft.enabled ? t('scheduleEnabled') : t('schedulePaused')}</dd></div></dl><p className="start-note">{draft.enforcementEnabled ? t('scheduleBlockingEnabled') : t('scheduleTimerOnly')}</p>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" disabled={busy} onClick={() => setReview(false)}>{t('back')}</button><button type="button" className="primary-action" disabled={!editable || busy} onClick={() => void save()}>{busy ? t('savingSchedule') : t('confirmSchedule')}</button></div>
      </> : rowAction ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{t(rowAction.action.kind === 'delete' ? 'deleteSchedule' : rowAction.action.value ? 'scheduleEnable' : 'schedulePause')} · {rowAction.schedule.name}</h2><p>{t(rowAction.action.kind === 'delete' ? 'scheduleDeleteConsequence' : rowAction.action.value ? 'scheduleEnableConsequence' : 'schedulePauseConsequence')}</p><p>{rowAction.schedule.profileName} · {rowAction.schedule.durationMinutes} {t('minutesShort')} · {rowAction.schedule.timeZoneId}</p>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" autoFocus onClick={close}>{t('cancel')}</button><button type="button" className={rowAction.action.kind === 'delete' ? 'danger-action' : 'primary-action'} disabled={!editable || busy} onClick={() => void applyRowAction()}>{t(rowAction.action.kind === 'delete' ? 'deleteSchedule' : rowAction.action.value ? 'scheduleEnable' : 'schedulePause')}</button></div>
      </> : null}
    </dialog>}
  </section>;
}
