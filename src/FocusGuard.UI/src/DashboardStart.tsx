import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from 'react';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient, ProfileSummary } from './native/dashboard';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { CycleFields } from './CycleFields';
import { FocusOrb } from './FocusOrb';
import { DesktopIcon, GlobeIcon } from '@radix-ui/react-icons';
import { CardSlider, InfoTip, Stepper } from './ZenControls';
import { motion } from 'framer-motion';
import type { CycleConfiguration, CycleInput } from './native/cycles';
import type { SessionReplayIntent } from './native/replay';
// Five minutes first: a tiny commitment is the easiest way past procrastination.
const quickDurations = [5, 15, 25, 50, 90] as const;
const StrictReviewPanel = lazy(() => import('./StrictReviewPanel').then(module => ({ default: module.StrictReviewPanel })));

type Copy = (key: keyof typeof es) => string;
export function DashboardStart({ dashboard, connection, language, t, refresh, replay, onReplayApplied, blockedSummary, renderBlockedPreview, reviewBeforeStart = true }: {
  dashboard: DashboardClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy; refresh(): Promise<void>;
  replay?: SessionReplayIntent | null; onReplayApplied?(): void; blockedSummary?: ReactNode; reviewBeforeStart?: boolean;
  renderBlockedPreview?(profileId: string, blockedApps: number, blockedSites: number): ReactNode;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [profileId, setProfileId] = useState(() => state.catalog?.items[0]?.id ?? '');
  const [minutes, setMinutes] = useState(() => String(state.preferences?.lastDurationMinutes ?? 25));
  const [blocking, setBlocking] = useState(() => state.preferences?.enforcementEnabled ?? false);
  const [strictMode, setStrictMode] = useState(false);
  const [objective, setObjective] = useState('');
  const [sessionType, setSessionType] = useState<'timer' | 'cycle'>(() => state.preferences?.lastSessionMode === 'FocusCycle' ? 'cycle' : 'timer');
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
  // "Start without review": the service still prepares and validates the session; the UI confirms it straight away
  // unless the review has a warning to show (browser not ready, a cut-short final phase). Strict sessions always review.
  const [autoStart, setAutoStart] = useState(false);
  useEffect(() => {
    if (!autoStart) return;
    if (state.phase === 'review' && review) {
      setAutoStart(false);
      const needsEyes = review.enforcementEnabled && !review.extensionReady && (state.review?.blockedWebsiteCount ?? cycleReview?.websiteRuleCount ?? 0) > 0
        || !!cycleReview?.partialFinalPhase;
      if (!needsEyes) void dashboard.confirm(objective);
    } else if (state.phase === 'error' || state.phase === 'idle') setAutoStart(false);
  }, [autoStart, state.phase, review, cycleReview, state.review, dashboard, objective]);
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
    setBlocking(state.preferences.enforcementEnabled && (!selected || selected.blockedApplicationCount + selected.blockedWebsiteCount > 0));
    const cycleMode = state.preferences.lastSessionMode === 'FocusCycle';
    setSessionType(cycleMode ? 'cycle' : 'timer');
    if (cycleMode && !state.cycles && !state.cyclesLoading) void dashboard.loadCycles();
  }, [dashboard, state.preferences, state.cycles, state.cyclesLoading, replaySessionId, selected]);
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
    return activeProfile || objective ? <div className="active-profile" style={activeProfile?.accentHex ? { '--profile-accent': activeProfile.accentHex } as CSSProperties : undefined}>{activeProfile && <p className="active-profile-name">{activeProfile.accentHex && <span className="profile-accent-marker" aria-hidden="true"/>}{activeProfile.name}</p>}
      {activeProfile && blockedSummary}
      {objective && <p className="active-objective"><span>{t('sessionObjectiveLabel')}</span> · {objective}</p>}</div> : null;
  }
  const errorText = state.phase === 'unknown' ? t('startUnknown') : state.error === 'NoBlockingRules' ? t('noBlockingRules') : state.error === 'ProfileMissing' ? t('profileMissing') : state.error === 'PresetMissing' ? t('presetMissing') : state.error === 'BreakRulesInvalid' ? t('breakRulesInvalid') : state.error === 'PreparationRejected' && sessionType === 'cycle' ? t('cyclePrepareError') : ['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') ? t('reviewUnavailable') : t('startError');
  const blockedApps = state.review?.blockedApplicationCount ?? cycleReview?.applicationRuleCount ?? 0;
  const blockedSites = state.review?.blockedWebsiteCount ?? cycleReview?.websiteRuleCount ?? 0;
  const phaseMoment = (utc: string) => new Date(utc).toLocaleDateString(language) === new Date(review!.startUtc).toLocaleDateString(language) ? new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'medium' }).format(new Date(utc)) : exactMoment(utc);
  const dialMinutes = validDuration ? Number(minutes) : 0;
  const dialDisabled = !allowed || state.catalogLoading;
  const showSetup = !!catalog && (catalog.total > 0 || !!selected) && !review;
  const shortTime = (utc: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(utc));
  return <section className={`start-panel${review ? ' has-review' : ''}`} aria-labelledby={review ? 'review-title' : 'start-title'}>
    {showSetup && <div className="focus-launch">
      {sessionType === 'timer'
        ? <FocusOrb mode="dial" minutes={dialMinutes} onMinutesChange={value => setMinutes(String(value))} disabled={dialDisabled}
          label={t('durationMinutes')} valueText={`${dialMinutes} ${t('minutesShort')}`}>
          <motion.span key={minutes} className="orb-value" initial={{ opacity: 0, filter: 'blur(6px)', scale: 0.96 }} animate={{ opacity: 1, filter: 'blur(0px)', scale: 1 }} transition={{ duration: 0.18, ease: [0.23, 1, 0.32, 1] }}>{validDuration ? minutes : '--'}</motion.span><span className="orb-unit">{t('minutesShort')}</span>
        </FocusOrb>
        : <FocusOrb mode="countdown" progress={1}><span className="orb-caption">{t('cycleOption')}</span></FocusOrb>}
      {/* The chip row keeps its height in cycle mode so switching modes never shifts the page. */}
      {sessionType === 'timer'
        ? <div className="quick-durations" role="group" aria-label={t('durationMinutes')}>
          {quickDurations.map(value => <button key={value} type="button" aria-pressed={dialMinutes === value} disabled={dialDisabled}
            onClick={() => setMinutes(String(value))}>{dialMinutes === value && <motion.span layoutId="quick-duration-pill" className="segment-pill" aria-hidden="true" transition={{ type: 'spring', duration: 0.4, bounce: 0 }}/>}<span className="segment-label">{value} {t('minutesShort')}</span></button>)}
        </div>
        : <div className="quick-durations quick-durations-placeholder" aria-hidden="true"/>}
    </div>}
    {!review && ['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') && <p className="notice" role="alert">{t('reviewUnavailable')}</p>}
    {state.catalogError && <p className="notice" role="alert">{t('profilesError')} <button type="button" onClick={() => void dashboard.loadProfiles()} disabled={state.catalogLoading || busy}>{t('refresh')}</button></p>}
    {state.catalogLoading && !catalog && <p role="status">{t('profilesLoading')}</p>}
    {catalog && catalog.total === 0 && <p className="session-guidance">{t('noProfiles')}</p>}
    {showSetup && <AutoHeight><form id="focusguard-start-session" className="setup-card" onSubmit={event => { event.preventDefault(); if (allowed && selected) { if (!strictMode && !reviewBeforeStart) setAutoStart(true); if (strictMode) { void dashboard.prepareStrict(selected.id, sessionType === 'timer' ? 'SimpleTimer' : 'FocusCycle', sessionType === 'timer' ? Number(minutes) * 60 : 0, sessionType === 'cycle' ? cycleInput : null, blocking); } else if (sessionType === 'timer' && validDuration) void dashboard.prepare(selected.id, Number(minutes), blocking); else if (sessionType === 'cycle' && cycleInput) void dashboard.prepareCycle(selected.id, cycleInput, blocking); } }}>
      <fieldset className="session-type segmented" disabled={!allowed}><legend>{t('sessionType')}</legend>{(['timer', 'cycle'] as const).map(kind => <label key={kind} data-checked={sessionType === kind}><input type="radio" name="session-type" checked={sessionType === kind} onChange={() => { if (kind !== sessionType) { setReplayConfiguration(null); setReplaySessionId(null); if (kind === 'timer') setCycleInput(null); } setSessionType(kind); if (kind === 'cycle' && !state.cycles && !state.cyclesLoading) void dashboard.loadCycles(); }}/>{sessionType === kind && <motion.span layoutId="session-type-pill" className="segment-pill" aria-hidden="true" transition={{ type: 'spring', duration: 0.4, bounce: 0 }}/>}<span className="segment-label">{t(kind === 'timer' ? 'timerOption' : 'cycleOption')}</span></label>)}</fieldset>
      {replaySessionId && <p className="start-note">{t('repeatDraftReview')}</p>}{replaySessionId && strictMode && <p className="start-note">{t('repeatStrictDraftReview')}</p>}
      <div className="setup-list">
        <div className="setup-section">
          <span className="setup-section-label" id="setup-profile-label">{t('profile')}</span>
          <CardSlider labelledBy="setup-profile-label" previous={t('previous')} next={t('next')} goTo={index => t('goToCard').replace('{index}', String(index))}>
            {[...catalog.items, ...(replayProfile && !catalog.items.some(profile => profile.id.toLowerCase() === replayProfile.id.toLowerCase()) ? [replayProfile] : [])].map(profile => { const checked = profile.id.toLowerCase() === profileId.toLowerCase(); return <label key={profile.id} className="choice-card" data-checked={checked} style={profile.accentHex ? { '--profile-accent': profile.accentHex } as CSSProperties : undefined}>
              <input type="radio" name="setup-profile" value={profile.id} checked={checked} disabled={!allowed || state.catalogLoading} onChange={() => { setProfileId(profile.id); setBlocking(false); if (profile.id !== replayProfile?.id) { setReplayProfile(null); setReplayConfiguration(null); setReplaySessionId(null); } setCycleInput(null); }}/>
              <span className="choice-card-icon" aria-hidden="true">{profile.name.slice(0, 1).toUpperCase()}</span>
              <span className="choice-card-name">{profile.name}</span>
              <span className="choice-card-meta"><span><DesktopIcon aria-hidden="true" focusable="false"/>{profile.blockedApplicationCount}</span><span><GlobeIcon aria-hidden="true" focusable="false"/>{profile.blockedWebsiteCount}</span></span>
            </label>; })}
          </CardSlider>
          {selected && <p className={`rule-summary profile-rule-summary sr-only ${selected.accentHex ? 'has-profile-accent' : ''}`} style={selected.accentHex ? { '--profile-accent': selected.accentHex } as CSSProperties : undefined}>{countLabel(selected.applicationRuleCount, t, 'singleApplicationRule', 'multipleApplicationRules')} · {countLabel(selected.websiteRuleCount, t, 'singleWebsiteRule', 'multipleWebsiteRules')}</p>}
          {selected && renderBlockedPreview?.(selected.id, selected.blockedApplicationCount, selected.blockedWebsiteCount)}
        </div>
        {catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={catalog.offset === 0 || busy || state.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, catalog.offset - 8))}>{t('previous')}</button><span>{catalog.offset + 1}–{Math.min(catalog.offset + 8, catalog.total)} / {catalog.total}</span><button type="button" disabled={catalog.offset + 8 >= catalog.total || busy || state.catalogLoading} onClick={() => void dashboard.loadProfiles(catalog.offset + 8)}>{t('next')}</button></div>}
        <motion.div key={sessionType} className="setup-mode" initial={{ opacity: 0, transform: 'translateY(6px)' }} animate={{ opacity: 1, transform: 'translateY(0px)' }} transition={{ duration: 0.22, ease: [0.23, 1, 0.32, 1] }}>
          {sessionType === 'timer'
            ? <div className="setup-row"><label className="setup-label" htmlFor="setup-duration">{t('durationMinutes')}</label><Stepper id="setup-duration" value={minutes} min={1} max={720} step={5} onChange={setMinutes} disabled={!allowed} unit={t('minutesShort')} decrease={t('decrease')} increase={t('increase')}/></div>
            : <div className="setup-section"><CycleFields key={`${profileId}:${selected?.defaultCyclePresetId ?? ''}:${replaySessionId ?? 'manual-cycle'}`} dashboard={dashboard} state={state} allowed={!!allowed} t={t} onChange={changeCycle} replayConfiguration={replayConfiguration} preferredPresetId={selected?.defaultCyclePresetId ?? null} preferredPresetName={selected?.defaultCyclePresetName ?? null} preferredPresetConfiguration={selected?.defaultCyclePresetConfiguration ?? null}/></div>}
        </motion.div>
        <div className="setup-toggles">
          <div className="setup-toggle blocking-option enforcement-toggle" data-on={blocking}><label className="setup-toggle-copy" htmlFor="setup-blocking">{t('blockDistractions')}</label><InfoTip label={t('moreInfo')} text={blocking ? t('unsavedWarning') : t(sessionType === 'cycle' ? 'cycleNoBlocking' : 'timerOnly')}/><input id="setup-blocking" type="checkbox" checked={blocking} onChange={event => setBlocking(event.target.checked)} disabled={!allowed || !selected || selected.blockedApplicationCount + selected.blockedWebsiteCount === 0}/></div>
          <div className="setup-toggle blocking-option strict-mode-option" data-on={strictMode}><label className="setup-toggle-copy" htmlFor="setup-strict">{t('strictMode')}</label><InfoTip label={t('moreInfo')} text={t('strictModeDetail').replace('{minutes}', '15')}/><input id="setup-strict" type="checkbox" checked={strictMode} onChange={event => setStrictMode(event.target.checked)} disabled={!allowed || busy}/></div>
        </div>
        <div className="setup-objective session-objective-input"><div className="setup-objective-head"><label htmlFor="setup-objective">{t('sessionObjectiveOptional')}</label><InfoTip label={t('moreInfo')} text={t('sessionObjectiveLocal')}/></div><textarea id="setup-objective" rows={1} maxLength={240} value={objective} placeholder={t('objectivePlaceholder')} onChange={event => setObjective(event.target.value)} disabled={!allowed}/>{objective.length > 0 && <small className="objective-count">{objective.length}/240</small>}</div>
      </div>
    </form></AutoHeight>}
    {(!review || autoStart) && <div className="start-heading start-cta-bar"><div><h2 id="start-title">{t('startTitle')}</h2><p>{t(catalog && !catalog.pilotEnabled ? 'startReadOnlyDetail' : 'startDetail')}</p></div><div className="start-heading-controls"><span className="badge">{catalog?.pilotEnabled ? t('pilotMode') : t('readingOnly')}</span>{reviewHint && <p className="start-action-hint" id="start-review-hint">{t(reviewHint)}</p>}{catalog && <button ref={reviewAction} className="primary-action" type="submit" form="focusguard-start-session" disabled={reviewDisabled} aria-describedby={reviewHint ? 'start-review-hint' : undefined}>{busy ? t(state.strictPhase === 'preparing' || state.phase === 'preparing' ? 'preparingSession' : 'startingSession') : t(strictMode ? 'reviewStrictSession' : reviewBeforeStart ? 'reviewSession' : 'startNow')}</button>}</div></div>}
    {review && !autoStart && <section className="normal-review checkout" aria-labelledby="review-title">
      <header className="checkout-head"><span className="checkout-badge" aria-hidden="true"><span>{(Date.parse(review.endUtc) - Date.parse(review.startUtc)) / 60000}</span><small>{t('minutesShort')}</small></span><div><h2 ref={reviewHeading} tabIndex={-1} id="review-title">{t('reviewTitle')}</h2><p>{t('checkoutEndsAt').replace('{time}', shortTime(review.endUtc))}</p></div></header>
      {replaySessionId && <p className="start-note">{t('repeatReviewCurrentProfile')}</p>}
      <dl className="checkout-lines"><div><dt>{t('profile')}</dt><dd>{review.profileName}</dd></div>{cycleReview && <div><dt>{t(cycleReview.presetName ? 'cyclePreset' : 'cycleConfiguration')}</dt><dd>{cycleReview.presetName ?? t('customConfiguration')}</dd></div>}{objective.trim() && <div><dt>{t('sessionObjectiveLabel')}</dt><dd>{objective.trim()}</dd></div>}<div><dt>{t('durationMinutes')}</dt><dd>{(Date.parse(review.endUtc) - Date.parse(review.startUtc)) / 60000}</dd></div><div><dt>{t('endsExactly')}</dt><dd>{exactMoment(review.endUtc)}</dd></div><div><dt>{t('reviewValidUntil')}</dt><dd>{exactMoment(review.expiresUtc)}</dd></div></dl>
      <p className={`checkout-scope${review.enforcementEnabled ? ' blocking' : ''}`}>{review.enforcementEnabled ? `${countLabel(blockedApps, t, cycleReview ? 'singleApplicationRule' : 'singleBlockedApplication', cycleReview ? 'multipleApplicationRules' : 'multipleBlockedApplications')} · ${countLabel(blockedSites, t, cycleReview ? 'singleWebsiteRule' : 'singleBlockedWebsite', cycleReview ? 'multipleWebsiteRules' : 'multipleBlockedWebsites')}` : t(cycleReview ? 'cycleNoBlocking' : 'timerOnly')}</p>
      {review.enforcementEnabled && <p className="notice">{t('unsavedWarning')}</p>}{review.enforcementEnabled && blockedSites > 0 && !review.extensionReady && <p className="notice">{t('browserWarning')}</p>}{cycleReview && <><h3>{t('cycleSchedule')}</h3><ol className="cycle-schedule" tabIndex={0} aria-label={t('cycleSchedule')}>{cycleReview.phases.map(phase => <li key={phase.index}><span>{phase.focusCycleNumber} · {t(phase.kind)}</span><span>{phaseMoment(phase.startUtc)} → {phaseMoment(phase.endUtc)}</span></li>)}</ol>{cycleReview.partialFinalPhase && <p className="notice">{t('partialFinalPhase')}</p>}{(cycleReview.breakApplicationRuleCount > 0 || cycleReview.breakWebsiteRuleCount > 0) && <details className="strict-break-rules strict-rule-summary"><summary>{t('breakRuleSummary')}: {countLabel(cycleReview.breakApplicationRuleCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')} · {countLabel(cycleReview.breakWebsiteRuleCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')}</summary><p className="start-note">{t('strictBreakExceptionMeaning')}</p><div className="strict-rule-columns"><section><h3>{t('breakExceptionApps')}</h3>{cycleReview.breakApplications.length ? <ul>{cycleReview.breakApplications.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section><section><h3>{t('breakExceptionSites')}</h3>{cycleReview.breakWebsites.length ? <ul>{cycleReview.breakWebsites.map((item, index) => <li key={`${index}:${item}`}>{item}</li>)}</ul> : <p>{t('none')}</p>}</section></div>{cycleReview.rulesTruncated && <p className="start-note">{t('strictRulesTruncated')}</p>}</details>}</>}
      <p className="start-note">{t('normalRecovery')}</p>
      <div className="review-actions"><button type="button" className="ghost-action" onClick={() => dashboard.cancelReview()}>{t('cancel')}</button><button className="primary-action" type="button" disabled={!allowed} onClick={() => void dashboard.confirm(objective)}>{t('confirmNormal')}</button></div>
    </section>}
    {busy && <p role="status">{state.phase === 'starting' || state.strictPhase === 'activating' ? t('startingSession') : t('preparingSession')}</p>}
    {(state.phase === 'error' || state.phase === 'unknown') && !['ActivationRejected', 'PreparationUnavailable'].includes(state.error ?? '') && <p className="notice" role="alert">{errorText}</p>}
    {state.phase === 'started' && <p role="status">{t('startAccepted')}</p>}
    {state.strictPhase === 'preparing' && <p role="status">{t('preparingStrictSession')}</p>}
    {state.strictPhase === 'activating' && <p role="status">{t('startingStrictSession')}</p>}
    {strictMode && state.strictError && <p className="notice" role="alert">{t(state.strictError === 'OutcomeUnknown' ? 'strictActivationUnknown' : state.strictError === 'BreakRulesInvalid' ? 'breakRulesInvalid' : state.strictError === 'NoBlockingRules' ? 'noBlockingRules' : state.strictError === 'ProfileMissing' ? 'profileMissing' : 'strictReviewUnavailable')}{state.strictPhase === 'unknown' && <button type="button" onClick={() => void refresh()}>{t('refresh')}</button>}</p>}
    {state.strictReview && <Suspense fallback={<p role="status">{t('strictReviewLoading')}</p>}><StrictReviewPanel dashboard={dashboard} review={state.strictReview} objective={objective} language={language} t={t} close={() => dashboard.cancelStrictReview()}/></Suspense>}
  </section>;
}

/** Animates its height to the content, so switching session type or options never jolts the page. */
function AutoHeight({ children }: { children: ReactNode }) {
  const inner = useRef<HTMLDivElement>(null);
  const [height, setHeight] = useState<number | 'auto'>('auto');
  useLayoutEffect(() => {
    const element = inner.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => setHeight(element.offsetHeight));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return <motion.div className="auto-height" initial={false} animate={{ height }} transition={{ duration: 0.32, ease: [0.23, 1, 0.32, 1] }}><div ref={inner} className="auto-height-inner">{children}</div></motion.div>;
}
