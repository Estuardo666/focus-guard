import { lazy, Suspense, useCallback, useEffect, useRef, useState, useSyncExternalStore, type CSSProperties } from 'react';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient, ProfileSummary } from './native/dashboard';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { CycleFields } from './CycleFields';
import type { CycleConfiguration, CycleInput } from './native/cycles';
import type { SessionReplayIntent } from './native/replay';
const StrictReviewPanel = lazy(() => import('./StrictReviewPanel').then(module => ({ default: module.StrictReviewPanel })));

type Copy = (key: keyof typeof es) => string;
export function DashboardStart({ dashboard, connection, language, t, refresh, replay, onReplayApplied }: {
  dashboard: DashboardClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy; refresh(): Promise<void>;
  replay?: SessionReplayIntent | null; onReplayApplied?(): void;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [profileId, setProfileId] = useState('');
  const [minutes, setMinutes] = useState('25');
  const [blocking, setBlocking] = useState(false);
  const [strictMode, setStrictMode] = useState(false);
  const [objective, setObjective] = useState('');
  const [sessionType, setSessionType] = useState<'timer' | 'cycle'>('timer');
  const [cycleInput, setCycleInput] = useState<CycleInput | null>(null);
  const [replayProfile, setReplayProfile] = useState<ProfileSummary | null>(null);
  const [replayConfiguration, setReplayConfiguration] = useState<CycleConfiguration | null>(null);
  const [replaySessionId, setReplaySessionId] = useState<string | null>(null);
  const preferencesApplied = useRef(false);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const reviewAction = useRef<HTMLButtonElement>(null);
  const hadReview = useRef(false);
  const changeCycle = useCallback((input: CycleInput | null) => setCycleInput(input), []);
  const catalog = state.catalog;
  const status = connection.status;
  const cycleReview = state.cycleReview;
  const review = state.review ?? cycleReview;
  const activeSessionId = status && ['Active', 'Activating'].includes(status.sessionState) ? status.sessionId : null;
  const activeIntent = activeSessionId && state.sessionReplay?.sessionId.toLowerCase() === activeSessionId.toLowerCase()
    ? state.sessionReplay : null;
  const selected = catalog?.items.find(profile => profile.id.toLowerCase() === profileId.toLowerCase())
    ?? (replayProfile?.id.toLowerCase() === profileId.toLowerCase() ? replayProfile : undefined);
  const available = connection.phase === 'ready' && status && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState);
  const busy = state.phase === 'preparing' || state.phase === 'starting' || state.strictPhase === 'preparing' || state.strictPhase === 'activating';
  const allowed = available && !!catalog?.pilotEnabled && !busy && state.phase !== 'unknown' && state.phase !== 'started'
    && !['unknown', 'started'].includes(state.strictPhase);
  const validDuration = Number.isInteger(Number(minutes)) && Number(minutes) >= 1 && Number(minutes) <= 720;
  const reviewDisabled = !allowed || !selected || (sessionType === 'timer' ? !validDuration : !cycleInput) || state.cyclesLoading;
  // A disabled primary action must say why: invalid fields otherwise leave it dead with no explanation.
  const inputHint = allowed && selected && !state.cyclesLoading ? (sessionType === 'timer' ? (!validDuration ? 'startInvalidDuration' : null) : (!cycleInput ? 'startInvalidCycle' : null)) : null;
  const reviewHint = catalog ? !catalog.pilotEnabled ? 'pilotRequired' : !available ? 'startNeedsReady' : inputHint : null;
  const exactMoment = (utc: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(utc));
  useEffect(() => { dashboard.observeStatus(connection.status, connection.phase === 'ready'); }, [dashboard, connection.status, connection.phase]);
  useEffect(() => {
    if (!review) return;
    const preparationId = review.preparationId;
    const checkExpiry = () => dashboard.expireReview(preparationId);
    checkExpiry();
    const interval = window.setInterval(checkExpiry, 1000);
    window.addEventListener('focus', checkExpiry);
    document.addEventListener('visibilitychange', checkExpiry);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', checkExpiry);
      document.removeEventListener('visibilitychange', checkExpiry);
    };
  }, [dashboard, review?.preparationId, review?.expiresUtc]);
  useEffect(() => {
    const root = document.documentElement;
    const accentHex = activeIntent?.profile?.accentHex;
    if (activeSessionId && accentHex) root.style.setProperty('--profile-accent', accentHex);
    else root.style.removeProperty('--profile-accent');
    return () => { root.style.removeProperty('--profile-accent'); };
  }, [activeSessionId, activeIntent?.profile?.accentHex]);
  useEffect(() => {
    if (!activeSessionId || state.sessionReplay?.sessionId.toLowerCase() === activeSessionId.toLowerCase()) return;
    void dashboard.loadSessionIntent(activeSessionId);
  }, [dashboard, activeSessionId, state.sessionReplay?.sessionId]);
  useEffect(() => {
    if (!catalog || replayProfile?.id.toLowerCase() === profileId.toLowerCase()) return;
    if (!catalog.items.some(profile => profile.id.toLowerCase() === profileId.toLowerCase())) setProfileId(catalog.items[0]?.id ?? '');
  }, [catalog, replayProfile, profileId]);
  useEffect(() => {
    if (catalog && catalog.total > 0 && catalog.offset >= catalog.total)
      void dashboard.loadProfiles(Math.floor((catalog.total - 1) / 8) * 8);
  }, [catalog, dashboard]);
  useEffect(() => { if (selected && selected.blockedApplicationCount + selected.blockedWebsiteCount === 0) setBlocking(false); }, [selected]);
  useEffect(() => { if (state.phase === 'started') void refresh(); }, [state.phase, refresh]);
  useEffect(() => { if (state.strictPhase === 'started') void refresh(); }, [state.strictPhase, refresh]);
  useEffect(() => {
    if (preferencesApplied.current || !state.preferences) return;
    preferencesApplied.current = true;
    if (replaySessionId) return;
    setMinutes(String(state.preferences.lastDurationMinutes));
    setBlocking(state.preferences.enforcementEnabled);
    const cycleMode = state.preferences.lastSessionMode === 'FocusCycle';
    setSessionType(cycleMode ? 'cycle' : 'timer');
    if (cycleMode && !state.cycles && !state.cyclesLoading) void dashboard.loadCycles();
  }, [dashboard, state.preferences, state.cycles, state.cyclesLoading, replaySessionId]);
  useEffect(() => {
    if (!replay || replay.sessionId === replaySessionId || !replay.replayable || !replay.profile
      || replay.durationMinutes === null || replay.mode === null || replay.enforcementEnabled === null
      || replay.mode === 'FocusCycle' && replay.cycleConfiguration === null) return;
    setReplayProfile(replay.profile);
    setReplayConfiguration(replay.mode === 'FocusCycle' ? replay.cycleConfiguration : null);
    setProfileId(replay.profile.id);
    setMinutes(String(replay.durationMinutes));
    setBlocking(replay.enforcementEnabled);
    setStrictMode(replay.strict === true);
    setObjective(replay.objective ?? '');
    setSessionType(replay.mode === 'FocusCycle' ? 'cycle' : 'timer');
    setCycleInput(replay.mode === 'FocusCycle' && replay.cycleConfiguration
      ? { presetId: null, configuration: replay.cycleConfiguration } : null);
    setReplaySessionId(replay.sessionId);
    if (replay.mode === 'FocusCycle' && !state.cycles && !state.cyclesLoading) void dashboard.loadCycles();
    onReplayApplied?.();
  }, [dashboard, replay, replaySessionId, state.cycles, state.cyclesLoading, onReplayApplied]);
  useEffect(() => { const present = !!review; if (present) reviewHeading.current?.focus(); else if (hadReview.current && ['idle', 'error'].includes(state.phase)) reviewAction.current?.focus(); hadReview.current = present; }, [review, state.phase]);
  if (status && ['Active', 'Activating'].includes(status.sessionState)) {
    const named = catalog?.items.find(profile => profile.id === status.profileId);
    const replayProfile = activeIntent?.profile;
    const activeProfile = named ?? (replayProfile && replayProfile.id.toLowerCase() === status.profileId?.toLowerCase() ? replayProfile : undefined);
    const objective = activeIntent?.objective;
    return activeProfile || objective ? <div className="active-profile" style={activeProfile?.accentHex ? { '--profile-accent': activeProfile.accentHex } as CSSProperties : undefined}>{activeProfile && <p className="active-profile-name">{activeProfile.accentHex && <span className="profile-accent-marker" aria-hidden="true"/>}{t('currentSavedProfile')} · {activeProfile.name}</p>}
      {objective && <p className="active-objective"><span>{t('sessionObjectiveLabel')}</span> · {objective}</p>}</div> : null;
  }
  const errorText = state.phase === 'unknown' ? t('startUnknown') : state.error === 'NoBlockingRules' ? t('noBlockingRules') : state.error === 'ProfileMissing' ? t('profileMissing') : state.error === 'PresetMissing' ? t('presetMissing') : state.error === 'BreakRulesInvalid' ? t('breakRulesInvalid') : state.error === 'PreparationRejected' && sessionType === 'cycle' ? t('cyclePrepareError') : ['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') ? t('reviewUnavailable') : t('startError');
  const blockedApps = state.review?.blockedApplicationCount ?? cycleReview?.applicationRuleCount ?? 0;
  const blockedSites = state.review?.blockedWebsiteCount ?? cycleReview?.websiteRuleCount ?? 0;
  const phaseMoment = (utc: string) => new Date(utc).toLocaleDateString(language) === new Date(review!.startUtc).toLocaleDateString(language) ? new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'medium' }).format(new Date(utc)) : exactMoment(utc);
  return <section className="start-panel" aria-labelledby={review ? 'review-title' : 'start-title'}>
    {!review && <div className="start-heading"><div><h2 id="start-title">{t('startTitle')}</h2><p>{t(catalog && !catalog.pilotEnabled ? 'startReadOnlyDetail' : 'startDetail')}</p></div><div className="start-heading-controls"><span className="badge">{catalog?.pilotEnabled ? t('pilotMode') : t('readingOnly')}</span>{catalog && <button ref={reviewAction} className="primary-action" type="submit" form="focusguard-start-session" disabled={reviewDisabled} aria-describedby={reviewHint ? 'start-review-hint' : undefined}>{busy ? t(state.strictPhase === 'preparing' || state.phase === 'preparing' ? 'preparingSession' : 'startingSession') : t(strictMode ? 'reviewStrictSession' : 'reviewSession')}</button>}{reviewHint && <p className="start-action-hint" id="start-review-hint">{t(reviewHint)}</p>}</div></div>}
    {!review && ['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') && <p className="notice" role="alert">{t('reviewUnavailable')}</p>}
    {state.catalogError && <p className="notice" role="alert">{t('profilesError')} <button type="button" onClick={() => void dashboard.loadProfiles()} disabled={state.catalogLoading || busy}>{t('refresh')}</button></p>}
    {state.catalogLoading && !catalog && <p role="status">{t('profilesLoading')}</p>}
    {catalog && catalog.total === 0 && <p className="session-guidance">{t('noProfiles')}</p>}
    {catalog && (catalog.total > 0 || selected) && !review && <form id="focusguard-start-session" onSubmit={event => { event.preventDefault(); if (allowed && selected) { if (strictMode) { void dashboard.prepareStrict(selected.id, sessionType === 'timer' ? 'SimpleTimer' : 'FocusCycle', sessionType === 'timer' ? Number(minutes) * 60 : 0, sessionType === 'cycle' ? cycleInput : null, blocking); } else if (sessionType === 'timer' && validDuration) void dashboard.prepare(selected.id, Number(minutes), blocking); else if (sessionType === 'cycle' && cycleInput) void dashboard.prepareCycle(selected.id, cycleInput, blocking); } }}>
      <fieldset className="session-type" disabled={!allowed}><legend>{t('sessionType')}</legend>{(['timer', 'cycle'] as const).map(kind => <label key={kind}><input type="radio" name="session-type" checked={sessionType === kind} onChange={() => { if (kind !== sessionType) { setReplayConfiguration(null); setReplaySessionId(null); if (kind === 'timer') setCycleInput(null); } setSessionType(kind); if (kind === 'cycle' && !state.cycles && !state.cyclesLoading) void dashboard.loadCycles(); }}/>{t(kind === 'timer' ? 'timerOption' : 'cycleOption')}</label>)}</fieldset>
      <label className="blocking-option strict-mode-option"><input type="checkbox" checked={strictMode} onChange={event => setStrictMode(event.target.checked)} disabled={!allowed || busy}/><span>{t('strictMode')}<small>{t('strictModeDetail').replace('{minutes}', '15')}</small></span></label>
      {replaySessionId && <p className="start-note">{t('repeatDraftReview')}</p>}{replaySessionId && strictMode && <p className="start-note">{t('repeatStrictDraftReview')}</p>}
      <div className="start-fields"><label>{t('profile')}<select value={profileId} onChange={event => { setProfileId(event.target.value); setBlocking(false); setReplayProfile(null); setReplayConfiguration(null); setReplaySessionId(null); setCycleInput(null); }} disabled={!allowed || state.catalogLoading} required>{catalog.items.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}{replayProfile && !catalog.items.some(profile => profile.id.toLowerCase() === replayProfile.id.toLowerCase()) && <option value={replayProfile.id}>{replayProfile.name} · {t('replayCurrentProfile')}</option>}</select></label>{sessionType === 'timer' && <label>{t('durationMinutes')}<input type="number" min="1" max="720" step="1" value={minutes} onChange={event => setMinutes(event.target.value)} disabled={!allowed} required/></label>}</div>
      {sessionType === 'cycle' && <CycleFields key={`${profileId}:${selected?.defaultCyclePresetId ?? ''}:${replaySessionId ?? 'manual-cycle'}`} dashboard={dashboard} state={state} allowed={!!allowed} t={t} onChange={changeCycle} replayConfiguration={replayConfiguration} preferredPresetId={selected?.defaultCyclePresetId ?? null} preferredPresetName={selected?.defaultCyclePresetName ?? null} preferredPresetConfiguration={selected?.defaultCyclePresetConfiguration ?? null}/>}
      <label className="session-objective-input"><span>{t('sessionObjectiveOptional')}<small>{t('sessionObjectiveLocal')}</small></span><textarea rows={2} maxLength={240} value={objective} onChange={event => setObjective(event.target.value)} disabled={!allowed}/><small className="objective-count">{objective.length}/240</small></label>
      {catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={catalog.offset === 0 || busy || state.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, catalog.offset - 8))}>{t('previous')}</button><span>{catalog.offset + 1}–{Math.min(catalog.offset + 8, catalog.total)} / {catalog.total}</span><button type="button" disabled={catalog.offset + 8 >= catalog.total || busy || state.catalogLoading} onClick={() => void dashboard.loadProfiles(catalog.offset + 8)}>{t('next')}</button></div>}
      {selected && <p className={`rule-summary profile-rule-summary ${selected.accentHex ? 'has-profile-accent' : ''}`} style={selected.accentHex ? { '--profile-accent': selected.accentHex } as CSSProperties : undefined}>{selected.accentHex && <span className="profile-accent-marker" aria-hidden="true"/>}<span>{countLabel(selected.applicationRuleCount, t, 'singleApplicationRule', 'multipleApplicationRules')} · {countLabel(selected.websiteRuleCount, t, 'singleWebsiteRule', 'multipleWebsiteRules')}</span></p>}
    <label className="blocking-option enforcement-toggle"><input type="checkbox" checked={blocking} onChange={event => setBlocking(event.target.checked)} disabled={!allowed || !selected || selected.blockedApplicationCount + selected.blockedWebsiteCount === 0}/><span>{t('enableBlocking')}<small>{selected ? `${countLabel(selected.blockedApplicationCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · ${countLabel(selected.blockedWebsiteCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}` : t('noSelection')}</small></span></label>
      <p className="start-note">{blocking ? t('unsavedWarning') : t(sessionType === 'cycle' ? 'cycleNoBlocking' : 'timerOnly')}</p>
    </form>}
    {review && <section className="normal-review" aria-labelledby="review-title"><h2 ref={reviewHeading} tabIndex={-1} id="review-title">{t('reviewTitle')}</h2>{replaySessionId && <p className="start-note">{t('repeatReviewCurrentProfile')}</p>}<dl><div><dt>{t('profile')}</dt><dd>{review.profileName}</dd></div>{cycleReview && <div><dt>{t(cycleReview.presetName ? 'cyclePreset' : 'cycleConfiguration')}</dt><dd>{cycleReview.presetName ?? t('customConfiguration')}</dd></div>}{objective.trim() && <div><dt>{t('sessionObjectiveLabel')}</dt><dd>{objective.trim()}</dd></div>}<div><dt>{t('durationMinutes')}</dt><dd>{(Date.parse(review.endUtc) - Date.parse(review.startUtc)) / 60000}</dd></div><div><dt>{t('endsExactly')}</dt><dd>{exactMoment(review.endUtc)}</dd></div><div><dt>{t('reviewValidUntil')}</dt><dd>{exactMoment(review.expiresUtc)}</dd></div></dl><p>{review.enforcementEnabled ? `${countLabel(blockedApps, t, cycleReview ? 'singleApplicationRule' : 'singleBlockedApplication', cycleReview ? 'multipleApplicationRules' : 'multipleBlockedApplications')} · ${countLabel(blockedSites, t, cycleReview ? 'singleWebsiteRule' : 'singleBlockedWebsite', cycleReview ? 'multipleWebsiteRules' : 'multipleBlockedWebsites')}` : t(cycleReview ? 'cycleNoBlocking' : 'timerOnly')}</p>{review.enforcementEnabled && <p className="notice">{t('unsavedWarning')}</p>}{review.enforcementEnabled && blockedSites > 0 && !review.extensionReady && <p className="notice">{t('browserWarning')}</p>}{cycleReview && <><h3>{t('cycleSchedule')}</h3><ol className="cycle-schedule" tabIndex={0} aria-label={t('cycleSchedule')}>{cycleReview.phases.map(phase => <li key={phase.index}><span>{phase.focusCycleNumber} · {t(phase.kind)}</span><span>{phaseMoment(phase.startUtc)} → {phaseMoment(phase.endUtc)}</span></li>)}</ol>{cycleReview.partialFinalPhase && <p className="notice">{t('partialFinalPhase')}</p>}{(cycleReview.breakApplicationRuleCount > 0 || cycleReview.breakWebsiteRuleCount > 0) && <details className="strict-break-rules strict-rule-summary"><summary>{t('breakRuleSummary')}: {countLabel(cycleReview.breakApplicationRuleCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · {countLabel(cycleReview.breakWebsiteRuleCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}</summary><p className="start-note">{t('strictBreakExceptionMeaning')}</p><div className="strict-rule-columns"><section><h3>{t('breakExceptionApps')}</h3>{cycleReview.breakApplications.length ? <ul>{cycleReview.breakApplications.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section><section><h3>{t('breakExceptionSites')}</h3>{cycleReview.breakWebsites.length ? <ul>{cycleReview.breakWebsites.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section></div>{cycleReview.rulesTruncated && <p className="start-note">{t('strictRulesTruncated')}</p>}</details>}</>}<p className="start-note">{t('normalRecovery')}</p><div className="review-actions"><button type="button" onClick={() => dashboard.cancelReview()}>{t('cancel')}</button><button className="primary-action" type="button" disabled={!allowed} onClick={() => void dashboard.confirm(objective)}>{t('confirmNormal')}</button></div></section>}
    {busy && <p role="status">{state.phase === 'starting' || state.strictPhase === 'activating' ? t('startingSession') : t('preparingSession')}</p>}
    {(state.phase === 'error' || state.phase === 'unknown') && !['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') && <p className="notice" role="alert">{errorText}</p>}
    {state.phase === 'started' && <p role="status">{t('startAccepted')}</p>}
    {state.strictPhase === 'preparing' && <p role="status">{t('preparingStrictSession')}</p>}
    {state.strictPhase === 'activating' && <p role="status">{t('startingStrictSession')}</p>}
    {strictMode && state.strictError && <p className="notice" role="alert">{t(state.strictError === 'OutcomeUnknown' ? 'strictActivationUnknown' : state.strictError === 'BreakRulesInvalid' ? 'breakRulesInvalid' : state.strictError === 'NoBlockingRules' ? 'noBlockingRules' : state.strictError === 'ProfileMissing' ? 'profileMissing' : 'strictReviewUnavailable')}{state.strictPhase === 'unknown' && <button type="button" onClick={() => void refresh()}>{t('refresh')}</button>}</p>}
    {state.strictReview && <Suspense fallback={<p role="status">{t('strictReviewLoading')}</p>}><StrictReviewPanel dashboard={dashboard} review={state.strictReview} objective={objective} language={language} t={t} close={() => dashboard.cancelStrictReview()}/></Suspense>}
  </section>;
}

