import { useEffect, useRef, useState, useSyncExternalStore, type CSSProperties } from 'react';
import { CalendarIcon, LockClosedIcon, Pencil1Icon, PlusIcon, ReloadIcon, TimerIcon, TrashIcon } from '@radix-ui/react-icons';
import type { DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot } from './native/protocol';
import type { ScheduleSummary } from './native/workflows';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { exitDialog, InfoTip, Stepper } from './ZenControls';

type Copy = (key: keyof typeof es) => string;
type Draft = { id: string; revision: string | null; name: string; days: number; startTime: string; timeZoneId: string; profileId: string; durationMinutes: number; enforcementEnabled: boolean; enabled: boolean; existing: ScheduleSummary | null };
type RowAction = { kind: 'delete' } | { kind: 'enabled'; value: boolean };
const defaultDays = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5);
const clockText = (minute: number) => `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`;
/** Days are a Sunday-first bit mask; the week reads Monday first. */
const weekOrder = [1, 2, 3, 4, 5, 6, 0] as const;
const accentStyle = (hex: string | null | undefined) => hex ? { '--profile-accent': hex } as CSSProperties : undefined;

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
  const leave = () => exitDialog(dialog.current, close);
  const save = async () => {
    if (!draft || !editable || !draft.name.trim() || !draft.profileId) return;
    const parts = draft.startTime.split(':');
    const hour = Number(parts[0] ?? ''), minute = Number(parts[1] ?? '');
    if (!Number.isInteger(hour) || !Number.isInteger(minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59 || draft.enforcementEnabled && blockedApplications + blockedWebsites === 0) return;
    const result = await dashboard.saveSchedule({ scheduleId: draft.id, expectedRevision: draft.revision,
      name: draft.name.trim(), days: draft.days, startMinute: hour * 60 + minute, timeZoneId: draft.timeZoneId,
      profileId: draft.profileId, durationMinutes: draft.durationMinutes, enforcementEnabled: draft.enforcementEnabled, enabled: draft.enabled });
    if (result) leave();
  };
  const applyRowAction = async () => {
    if (!rowAction || !rowAction.schedule.revision) return;
    const result = rowAction.action.kind === 'delete'
      ? await dashboard.deleteSchedule(rowAction.schedule.id, rowAction.schedule.revision)
      : await dashboard.setScheduleEnabled(rowAction.schedule.id, rowAction.schedule.revision, rowAction.action.value);
    if (result) leave();
  };
  const locale = language === 'es' ? 'es-EC' : 'en-US';
  const weekday = (day: number, style: 'short' | 'narrow' | 'long') => new Intl.DateTimeFormat(locale, { weekday: style, timeZone: 'UTC' }).format(new Date(Date.UTC(2024, 0, 7 + day)));
  const dayLabels = Array.from({ length: 7 }, (_, day) => weekday(day, 'short'));
  const daysText = (days: number) => weekOrder.filter(day => days & (1 << day)).map(day => dayLabels[day]).join(' · ');
  const resultLabel = (value: string) => ({ Started: 'scheduleResultStarted', Busy: 'scheduleResultBusy', Missed: 'scheduleResultMissed', Claimed: 'scheduleResultClaimed', Rejected: 'scheduleResultRejected' } as const)[value as 'Started' | 'Busy' | 'Missed' | 'Claimed' | 'Rejected'] ?? (value ? 'scheduleResultOther' : 'notYet');
  const errorLabel = state.scheduleError === 'ScheduleConflict' || state.scheduleError === 'ScheduleMissing' ? t('scheduleConflict')
    : state.scheduleError === 'ScheduleOverlap' ? t('scheduleOverlap')
    : state.scheduleError === 'ScheduleLimit' ? t('scheduleLimit')
    : state.scheduleError === 'NoBlockingRules' ? t('noBlockingRules')
    : state.scheduleError === 'ProfileMissing' ? t('profileMissing')
    : state.scheduleError === 'ScheduleEditorUnavailable' || state.scheduleError === 'Unauthorized' ? t('scheduleEditorUnavailable')
    : state.scheduleError === 'OutcomeUnknown' ? t('scheduleUnknown')
    : state.scheduleError ? t('scheduleSaveError') : null;
  const formatNext = (value: string | null, timeZoneIanaId?: string | null) => value ? new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium', timeStyle: 'short', ...(timeZoneIanaId ? { timeZone: timeZoneIanaId } : {})
  }).format(new Date(value)) : t('scheduleNoNext');
  const selectedOption = profiles.find(profile => profile.id === draft?.profileId);
  const accentFor = (profileId: string | null | undefined) => profiles.find(profile => profile.id === profileId)?.accentHex;
  const errorNotice = errorLabel ? <p role="alert" className="notice">{errorLabel} <button type="button" disabled={busy} onClick={() => void dashboard.loadSchedules()}>{t('refresh')}</button></p> : null;
  const blockingCounts = selectedOption ? `${countLabel(selectedOption.blockedApplicationCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · ${countLabel(selectedOption.blockedWebsiteCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}`
    : draft?.existing ? `${countLabel(draft.existing.blockedApplicationRuleCount ?? 0, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · ${countLabel(draft.existing.blockedWebsiteRuleCount ?? 0, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}` : t('noSelection');
  return <section className="schedule-manager" aria-label={t('schedulesTitle')}>
    <div className="workflow-heading heading-actions-only"><div className="sr-only"><h2>{t('schedulesTitle')}</h2><p>{t('schedulesDetail')}</p></div>
      <div className="schedule-actions">
        <button type="button" className="icon-action" disabled={state.schedulesLoading} title={t('refreshSchedules')} onClick={() => void dashboard.loadSchedules()}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{state.schedulesLoading ? t('refreshing') : t('refreshSchedules')}</span></button>
        <button type="button" className="primary-action" disabled={!editable || busy || !profiles.length} onClick={event => openNew(event.currentTarget)}><PlusIcon aria-hidden="true" focusable="false"/>{t('newSchedule')}</button>
      </div></div>
    {errorNotice}
    {state.schedulesError && <p className="notice" role="alert">{t('schedulesUnavailable')}</p>}
    {state.catalog && !state.catalog.pilotEnabled
      ? <p className="start-note">{t('editorPilotRequired')}</p>
      : state.schedules && state.schedules.editingEnabled !== true && <p className="start-note">{t('scheduleReadOnlyHost')}</p>}
    {state.schedulesLoading && <p role="status">{t('schedulesLoading')}</p>}
    {state.schedules && !state.schedulesLoading && !state.schedulesError && rows.length === 0 && <div className="schedule-empty-state" role="status" aria-labelledby="schedule-empty-title">
      <span className="schedule-empty-orb" aria-hidden="true"><CalendarIcon/></span>
      <h3 id="schedule-empty-title">{t('schedulesEmpty')}</h3>
      <p>{t('schedulesEmptyDetail')}</p>
    </div>}
    {rows.length > 0 && <div className="week-strip" aria-label={t('scheduleWeek')} role="group">{weekOrder.map(day => {
      const today = new Date().getDay() === day;
      const items = rows.filter(schedule => schedule.days & (1 << day)).sort((a, b) => a.startMinute - b.startMinute);
      return <div key={day} className="week-day" data-today={today}>
        <span className="week-day-label">{dayLabels[day]}</span>
        {items.map(schedule => <span key={schedule.id} className="week-pill" data-enabled={schedule.enabled} style={accentStyle(accentFor(schedule.profileId))} title={`${schedule.name} · ${clockText(schedule.startMinute)}`}>{clockText(schedule.startMinute)}</span>)}
      </div>;
    })}</div>}
    <div className="schedule-list">{rows.map(schedule => <article className="schedule-card" key={schedule.id} data-enabled={schedule.enabled} style={accentStyle(accentFor(schedule.profileId))}>
      <header className="schedule-card-head">
        <div className="schedule-time"><strong>{clockText(schedule.startMinute)}</strong><span>{schedule.durationMinutes} {t('minutesShort')}</span></div>
        <button type="button" role="switch" className="schedule-switch" aria-checked={schedule.enabled} disabled={!editable || busy || !schedule.revision} onClick={event => { trigger.current = event.currentTarget; setRowAction({ schedule, action: { kind: 'enabled', value: !schedule.enabled } }); }}><span className="schedule-switch-thumb" aria-hidden="true"/><span className="sr-only">{t(schedule.enabled ? 'schedulePause' : 'scheduleEnable')}</span></button>
      </header>
      <div className="schedule-title"><h3>{schedule.name}</h3><span className={`badge ${schedule.enabled ? 'schedule-enabled' : ''}`}>{t(schedule.enabled ? 'scheduleEnabled' : 'schedulePaused')}</span></div>
      <div className="schedule-day-dots" aria-label={daysText(schedule.days)} role="img">{weekOrder.map(day => <span key={day} data-on={(schedule.days & (1 << day)) !== 0}>{weekday(day, 'narrow')}</span>)}</div>
      <p className="schedule-meta"><span className="schedule-profile"><span className="choice-card-icon" aria-hidden="true">{schedule.profileName.slice(0, 1).toUpperCase()}</span>{schedule.profileName}</span>
        <span className="schedule-mode" data-blocking={schedule.enforcementEnabled}>{schedule.enforcementEnabled ? <LockClosedIcon aria-hidden="true" focusable="false"/> : <TimerIcon aria-hidden="true" focusable="false"/>}{schedule.enforcementEnabled ? t('scheduleWithBlocking') : t('scheduleTimerOnlyShort')}</span></p>
      <dl><div><dt>{t('scheduleNextStart')}</dt><dd>{formatNext(schedule.nextStartUtc, schedule.timeZoneIanaId)}</dd></div><div><dt>{t('scheduleNextEnd')}</dt><dd>{schedule.nextEndUtc === undefined ? (schedule.nextStartUtc ? t('scheduleEndUnavailable') : t('scheduleNoNext')) : formatNext(schedule.nextEndUtc, schedule.timeZoneIanaId)}</dd></div><div><dt>{t('scheduleLastResult')}</dt><dd>{t(resultLabel(schedule.lastResult))}</dd></div></dl>
      <footer className="schedule-card-actions">
        <button type="button" className="ghost-action" disabled={!editable || busy || !schedule.revision || !schedule.profileId} onClick={event => openEdit(schedule, event.currentTarget)}><Pencil1Icon aria-hidden="true" focusable="false"/><span className="sr-only">{t('editSchedule')}</span></button>
        <button type="button" className="ghost-action danger-icon" disabled={!editable || busy || !schedule.revision} onClick={event => { trigger.current = event.currentTarget; setRowAction({ schedule, action: { kind: 'delete' } }); }}><TrashIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('deleteSchedule')}</span></button>
      </footer>
    </article>)}</div>
    {(draft || rowAction) && <dialog ref={dialog} className="delete-review schedule-review" aria-labelledby="schedule-review-title" onCancel={event => { event.preventDefault(); leave(); }}>
      {draft && !review ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{draft.existing ? t('editSchedule') : t('newSchedule')}</h2>
          <form id="schedule-entry-form" className="schedule-form" onSubmit={event => { event.preventDefault(); setReview(true); }}>
            <label className="schedule-name-field"><span className="sr-only">{t('scheduleName')}</span><input autoFocus maxLength={80} required value={draft.name} placeholder={t('scheduleNamePlaceholder')} disabled={!editable || busy} onChange={event => update({ name: event.target.value })}/></label>
            <div className="schedule-when">
              <label className="schedule-clock">{t('scheduleTime')}<input type="time" required value={draft.startTime} disabled={!editable || busy} onChange={event => update({ startTime: event.target.value })}/></label>
              <div className="schedule-duration"><span id="schedule-duration-label">{t('durationLabel')}<InfoTip label={t('moreInfo')} text={t('scheduleMinimumDuration')}/></span>
                <Stepper value={String(draft.durationMinutes)} min={1} max={720} step={5} disabled={!editable || busy} unit={t('minutesShort')} decrease={t('decrease')} increase={t('increase')} label={t('durationLabel')} describedBy="schedule-minimum-duration" onChange={value => update({ durationMinutes: Number(value) })}/>
                <small id="schedule-minimum-duration" className="sr-only">{t('scheduleMinimumDuration')}</small></div>
            </div>
            <fieldset className="schedule-days-field" disabled={!editable || busy}><legend>{t('scheduleDays')}</legend><div className="schedule-days">{weekOrder.map(day => <label key={day} data-on={(draft.days & (1 << day)) !== 0} title={weekday(day, 'long')}><input type="checkbox" checked={(draft.days & (1 << day)) !== 0} onChange={event => update({ days: event.target.checked ? draft.days | (1 << day) : draft.days & ~(1 << day) })}/><span aria-hidden="true">{weekday(day, 'narrow')}</span><span className="sr-only">{dayLabels[day]}</span></label>)}</div></fieldset>
            <fieldset className="schedule-profile-field" disabled={!editable || busy}><legend>{t('chooseProfile')}</legend>
              <div className="schedule-profiles" role="radiogroup">
                {draft.existing && !profiles.some(profile => profile.id === draft.existing!.profileId) && <label className="schedule-profile-option" data-checked={draft.profileId === draft.existing.profileId}><input type="radio" name="schedule-profile" checked={draft.profileId === draft.existing.profileId} onChange={() => update({ profileId: draft.existing!.profileId ?? '', enforcementEnabled: false })}/><span className="choice-card-icon" aria-hidden="true">{draft.existing.profileName.slice(0, 1).toUpperCase()}</span><span>{draft.existing.profileName} · {t('scheduleSavedProfile')}</span></label>}
                {profiles.map(profile => <label key={profile.id} className="schedule-profile-option" data-checked={draft.profileId === profile.id} style={accentStyle(profile.accentHex)}><input type="radio" name="schedule-profile" required value={profile.id} checked={draft.profileId === profile.id} onChange={() => update({ profileId: profile.id, enforcementEnabled: false })}/><span className="choice-card-icon" aria-hidden="true">{profile.name.slice(0, 1).toUpperCase()}</span><span>{profile.name}</span></label>)}
              </div>
              {state.catalog && state.catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={!state.catalog.offset || state.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, state.catalog!.offset - 8))}>{t('previous')}</button><span>{state.catalog.offset + 1}–{Math.min(state.catalog.offset + 8, state.catalog.total)} / {state.catalog.total}</span><button type="button" disabled={state.catalog.offset + 8 >= state.catalog.total || state.catalogLoading} onClick={() => void dashboard.loadProfiles(state.catalog!.offset + 8)}>{t('next')}</button></div>}
            </fieldset>
            <div className="setup-toggles schedule-toggles">
              <div className="setup-toggle blocking-option" data-on={draft.enforcementEnabled}><label className="setup-toggle-copy" htmlFor="schedule-blocking">{t('blockDistractions')}<small>{blockingCounts}</small></label><input id="schedule-blocking" type="checkbox" checked={draft.enforcementEnabled} disabled={!editable || busy || blockedApplications + blockedWebsites === 0} onChange={event => update({ enforcementEnabled: event.target.checked })}/></div>
              <div className="setup-toggle blocking-option" data-on={draft.enabled}><label className="setup-toggle-copy" htmlFor="schedule-autostart">{t('scheduleAutoStartShort')}</label><InfoTip label={t('moreInfo')} text={`${t('scheduleAutoStartConsent')}. ${t('scheduleAutoStartDetail')}`}/><input id="schedule-autostart" type="checkbox" checked={draft.enabled} disabled={!editable || busy} onChange={event => update({ enabled: event.target.checked })}/></div>
            </div>
            <p className="schedule-zone">{t('scheduleTimeZone')}: {draft.timeZoneId}</p>
          </form>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" disabled={busy} onClick={leave}>{t('cancel')}</button><button type="submit" form="schedule-entry-form" className="primary-action" disabled={!editable || busy || !draft.name.trim() || !draft.days || !draft.profileId || !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(draft.startTime) || !(draft.durationMinutes >= 1 && draft.durationMinutes <= 720) || draft.enforcementEnabled && blockedApplications + blockedWebsites === 0}>{t('reviewSchedule')}</button></div>
      </> : draft ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{t('reviewSchedule')}</h2>
          <div className="schedule-review-hero"><strong>{draft.startTime}</strong><span>{draft.durationMinutes} {t('minutesShort')}</span></div>
          <p>{t('scheduleReviewDetail')}</p>
          <dl><div><dt>{t('scheduleName')}</dt><dd>{draft.name}</dd></div><div><dt>{t('scheduleDays')}</dt><dd>{daysText(draft.days)} · {draft.timeZoneId}</dd></div><div><dt>{t('chooseProfile')}</dt><dd>{selectedOption?.name ?? draft.existing?.profileName}</dd></div><div><dt>{t('durationLabel')}</dt><dd>{draft.durationMinutes} {t('minutesShort')}</dd></div><div><dt>{t('scheduleAutoStartConsent')}</dt><dd>{draft.enabled ? t('scheduleEnabled') : t('schedulePaused')}</dd></div></dl><p className="start-note">{draft.enforcementEnabled ? t('scheduleBlockingEnabled') : t('scheduleTimerOnly')}</p>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" disabled={busy} onClick={() => setReview(false)}>{t('back')}</button><button type="button" className="primary-action" disabled={!editable || busy} onClick={() => void save()}>{busy ? t('savingSchedule') : t('confirmSchedule')}</button></div>
      </> : rowAction ? <>
        <div className="schedule-dialog-body" role="region" aria-labelledby="schedule-review-title" tabIndex={0}>
          <h2 id="schedule-review-title">{t(rowAction.action.kind === 'delete' ? 'deleteSchedule' : rowAction.action.value ? 'scheduleEnable' : 'schedulePause')} · {rowAction.schedule.name}</h2><p>{t(rowAction.action.kind === 'delete' ? 'scheduleDeleteConsequence' : rowAction.action.value ? 'scheduleEnableConsequence' : 'schedulePauseConsequence')}</p><p>{rowAction.schedule.profileName} · {rowAction.schedule.durationMinutes} {t('minutesShort')} · {rowAction.schedule.timeZoneId}</p>
          {errorNotice}
        </div>
        <div className="review-actions schedule-dialog-actions"><button type="button" autoFocus onClick={leave}>{t('cancel')}</button><button type="button" className={rowAction.action.kind === 'delete' ? 'danger-action' : 'primary-action'} disabled={!editable || busy} onClick={() => void applyRowAction()}>{t(rowAction.action.kind === 'delete' ? 'deleteSchedule' : rowAction.action.value ? 'scheduleEnable' : 'schedulePause')}</button></div>
      </> : null}
    </dialog>}
  </section>;
}
