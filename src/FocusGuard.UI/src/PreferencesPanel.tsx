import { useEffect, useId, useRef, useState, useSyncExternalStore, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { BellIcon, ColorWheelIcon, MoonIcon, ReloadIcon, SunIcon, TimerIcon } from '@radix-ui/react-icons';
import type { DashboardClient } from './native/dashboard';
import type { UserPreferences } from './native/preferences';
import { orbStyles, palettes, type OrbStyle, type Palette } from './design-system/palettes';
import { FocusOrb } from './FocusOrb';
import { InfoTip } from './ZenControls';
import type es from './locales/es.json';

type Copy = (key: keyof typeof es) => string;
type Option<Value extends string> = { value: Value; label: string; icon?: ReactNode };
type Section = 'appearance' | 'sessions' | 'ending';
const quickDurations = [15, 25, 50, 90] as const;
const paletteLabels: Record<Palette, keyof typeof es> = { aurora: 'paletteAurora', mist: 'paletteMist', ember: 'paletteEmber', ocean: 'paletteOcean' };
const orbLabels: Record<OrbStyle, keyof typeof es> = { pearl: 'orbPearl', voice: 'orbVoice', lava: 'orbLava', cloud: 'orbCloud' };
const sessionEndOptions = ['Window', 'DimmedWindow', 'Notification', 'None'] as const;
const sessionEndLabels: Record<UserPreferences['sessionEnd'], keyof typeof es> = { Window: 'sessionEndWindow', DimmedWindow: 'sessionEndDimmed', Notification: 'sessionEndNotification', None: 'sessionEndNone' };
const spring = { type: 'spring', duration: 0.4, bounce: 0 } as const;

/**
 * Preferences as three short pages (Appearance, Sessions, When it ends) instead of one long form. Looks that only live
 * on this device (orb, ambience, review step) apply at once; the service-backed ones go through the save bar.
 */
export function PreferencesPanel({ dashboard, t, onSaved, palette, onPaletteChange, orbStyle, onOrbStyleChange, reviewBeforeStart, onReviewBeforeStartChange, startup }: {
  dashboard: DashboardClient; t: Copy; onSaved(preferences: UserPreferences): void; palette?: Palette; onPaletteChange?(palette: Palette): void;
  orbStyle?: OrbStyle; onOrbStyleChange?(style: OrbStyle): void; reviewBeforeStart?: boolean; onReviewBeforeStartChange?(value: boolean): void;
  /** Sign-in start lives in the signed-in user's Run key, read and written by the desktop host. */
  startup?: Readonly<{ read(): Promise<boolean | null>; write(enabled: boolean): Promise<boolean | null> }>;
}) {
  const state = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [draft, setDraft] = useState<UserPreferences | null>(null);
  const appliedPreferences = useRef<UserPreferences | null>(null);
  const [section, setSection] = useState<Section>('appearance');
  const saving = state.preferencesPhase === 'writing';
  const locked = saving || state.preferencesPhase === 'unknown';
  useEffect(() => {
    const preferences = state.preferences;
    if (!preferences) return;
    const previous = appliedPreferences.current;
    appliedPreferences.current = preferences;
    setDraft(current => {
      if (!current || !previous) return preferences;
      const next = { ...preferences };
      for (const key of Object.keys(preferences) as (keyof UserPreferences)[]) {
        if (current[key] !== previous[key]) Object.assign(next, { [key]: current[key] });
      }
      return next;
    });
  }, [state.preferences]);
  const [startAtSignIn, setStartAtSignIn] = useState<boolean | null>(null);
  const [startupBusy, setStartupBusy] = useState(false);
  useEffect(() => { let live = true; void startup?.read().then(value => { if (live) setStartAtSignIn(value); }); return () => { live = false; }; }, [startup]);
  const changeStartup = async (enabled: boolean) => {
    if (!startup) return;
    setStartupBusy(true);
    const stored = await startup.write(enabled);
    setStartupBusy(false);
    if (stored !== null) setStartAtSignIn(stored);
  };
  // Opening the tab retries a failed (or never-completed) startup read instead of waiting for a manual refresh.
  useEffect(() => {
    const current = dashboard.getSnapshot();
    if (!current.preferences && !current.preferencesLoading) void dashboard.loadPreferences();
  }, [dashboard]);
  const validDuration = !!draft && Number.isInteger(draft.lastDurationMinutes) && draft.lastDurationMinutes >= 1 && draft.lastDurationMinutes <= 720;
  const dirty = !!draft && !!state.preferences && (Object.keys(draft) as (keyof UserPreferences)[]).some(key => draft[key] !== state.preferences![key]);
  const save = async () => { if (!draft || locked || !validDuration) return; const result = await dashboard.savePreferences(draft); if (result) onSaved(result); };
  const refresh = () => void dashboard.loadPreferences();
  const update = <Key extends keyof UserPreferences>(key: Key, value: UserPreferences[Key]) => setDraft(current => current ? { ...current, [key]: value } : current);
  const unavailable = state.preferencesError === 'PreferencesUnavailable' || state.preferencesError === 'AuthorizationUnavailable';
  const readFailed = state.preferencesPhase === 'idle';
  const errorText = state.preferencesPhase === 'unknown' ? t('preferencesUnknown') : unavailable ? t('preferencesUnavailable') : readFailed ? t('preferencesLoadError') : t('preferencesSaveError');
  const sections: readonly { key: Section; label: string; Icon: typeof SunIcon }[] = [
    { key: 'appearance', label: t('appearance'), Icon: ColorWheelIcon },
    { key: 'sessions', label: t('prefGroupSessions'), Icon: TimerIcon },
    { key: 'ending', label: t('sessionEndPresentation'), Icon: BellIcon },
  ];
  const tabId = (key: Section) => `pref-tab-${key}`;
  return <section className="preferences-panel workflow-view prefs-studio" aria-labelledby="preferences-title">
    <div className="sr-only"><h2 id="preferences-title">{t('preferencesTitle')}</h2><p>{t('preferencesDetail')}</p></div>
    <div className="pref-toolbar">
      <div className="segmented pref-tabs" role="tablist" aria-label={t('preferencesTitle')}>
        {sections.map(entry => <button key={entry.key} type="button" role="tab" id={tabId(entry.key)} aria-selected={section === entry.key} aria-controls={`${tabId(entry.key)}-panel`} tabIndex={section === entry.key ? 0 : -1}
          onClick={() => setSection(entry.key)}
          onKeyDown={event => { if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return; event.preventDefault(); const index = sections.findIndex(candidate => candidate.key === section); const next = sections[(index + (event.key === 'ArrowRight' ? 1 : sections.length - 1)) % sections.length]!; setSection(next.key); document.getElementById(tabId(next.key))?.focus(); }}>
          {section === entry.key && <motion.span layoutId="pref-tab-pill" className="segment-pill" aria-hidden="true" transition={spring}/>}
          <span className="segment-label"><entry.Icon aria-hidden="true" focusable="false"/>{entry.label}</span>
        </button>)}
      </div>
      <button type="button" className="icon-action" disabled={state.preferencesLoading || saving} title={t('refreshPreferences')} onClick={refresh}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{state.preferencesLoading ? t('refreshing') : t('refreshPreferences')}</span></button>
    </div>
    {!draft && state.preferencesLoading && <p role="status">{t('preferencesLoading')}</p>}
    {state.preferencesError && <p className="notice" role="alert">{errorText}{state.preferencesPhase === 'unknown' && <button type="button" onClick={refresh}>{t('refresh')}</button>}</p>}
    {draft && <>
      <div className="pref-page" role="tabpanel" id={`${tabId('appearance')}-panel`} aria-labelledby={tabId('appearance')} hidden={section !== 'appearance'}>
        {(orbStyle && onOrbStyleChange || palette && onPaletteChange) && <section className="pref-orb-stage" data-orb-picker={!!(orbStyle && onOrbStyleChange)} aria-labelledby="pref-orb-title">
          {orbStyle && onOrbStyleChange && <div className="pref-orb-preview" aria-hidden="true">{section === 'appearance' && <FocusOrb mode="countdown" progress={0.68}/>}</div>}
          <div className="pref-orb-picker">
            <h3 id="pref-orb-title">{t(orbStyle && onOrbStyleChange ? 'orbStyle' : 'ambience')}</h3>
            {orbStyle && onOrbStyleChange && <div className="pref-orb-options" role="radiogroup" aria-labelledby="pref-orb-title">{orbStyles.map(value => <label key={value} className="pref-orb-option" data-checked={orbStyle === value}>
              <input type="radio" name="pref-orb" value={value} checked={orbStyle === value} onChange={() => onOrbStyleChange(value)}/>
              <span className="orb-thumb" data-orb={value} aria-hidden="true"/><span>{t(orbLabels[value])}</span>
            </label>)}</div>}
            {palette && onPaletteChange && <div className="pref-inline">
              <span className="pref-inline-label">{t('ambience')}<InfoTip label={t('moreInfo')} text={t('ambienceDetail')}/></span>
              <div className="pref-ambience" role="group" aria-label={t('ambience')}>{palettes.map(value => <button key={value} type="button" className="ambience-swatch ambience-card" data-swatch={value} aria-pressed={palette === value} title={t(paletteLabels[value])} onClick={() => onPaletteChange(value)}><span className="segment-label">{t(paletteLabels[value])}</span></button>)}</div>
            </div>}
          </div>
        </section>}
        <PreferenceGroup>
          <PreferenceRow t={t} label={t('themeLabel')}>
            <Segmented name="pref-theme" value={draft.theme} disabled={locked} onChange={value => update('theme', value)}
              options={[{ value: 'Dark', label: t('dark'), icon: <MoonIcon aria-hidden="true" focusable="false"/> }, { value: 'Light', label: t('light'), icon: <SunIcon aria-hidden="true" focusable="false"/> }]}/>
          </PreferenceRow>
          <PreferenceRow t={t} label={t('language')}>
            <Segmented name="pref-language" value={draft.uiLanguage} disabled={locked} onChange={value => update('uiLanguage', value)} options={[{ value: 'es', label: 'Español' }, { value: 'en', label: 'English' }]}/>
          </PreferenceRow>
          <PreferenceRow t={t} label={t('reduceMotion')} detail={t('reduceMotionDetail')}>
            <Switch label={t('reduceMotion')} checked={draft.reduceMotion} disabled={locked} onChange={value => update('reduceMotion', value)}/>
          </PreferenceRow>
        </PreferenceGroup>
      </div>
      <div className="pref-page" role="tabpanel" id={`${tabId('sessions')}-panel`} aria-labelledby={tabId('sessions')} hidden={section !== 'sessions'}>
        <PreferenceGroup>
          {startAtSignIn !== null && <PreferenceRow t={t} label={t('startAtSignIn')} detail={t('startAtSignInDetail')}>
            <Switch label={t('startAtSignIn')} checked={startAtSignIn} disabled={startupBusy} onChange={value => void changeStartup(value)}/>
          </PreferenceRow>}
          {onReviewBeforeStartChange && <PreferenceRow t={t} label={t('reviewBeforeStart')} detail={t('reviewBeforeStartDetail')}>
            <Switch label={t('reviewBeforeStart')} checked={reviewBeforeStart !== false} disabled={false} onChange={onReviewBeforeStartChange}/>
          </PreferenceRow>}
          <PreferenceRow t={t} label={t('defaultBlocking')} detail={t('defaultBlockingDetail')}>
            <Switch label={t('defaultBlocking')} checked={draft.enforcementEnabled} disabled={locked} onChange={value => update('enforcementEnabled', value)}/>
          </PreferenceRow>
          <PreferenceRow t={t} label={t('defaultSessionMode')}>
            <Segmented name="pref-mode" value={draft.lastSessionMode} disabled={locked} onChange={value => update('lastSessionMode', value)} options={[{ value: 'SimpleTimer', label: t('timerOption') }, { value: 'FocusCycle', label: t('cycleOption') }]}/>
          </PreferenceRow>
          <PreferenceRow t={t} label={t('defaultDuration')} detail={t('defaultDurationDetail')} stacked>
            <div className="pref-duration">
              <div className="quick-durations" role="group" aria-label={t('defaultDuration')}>{quickDurations.map(value => <button key={value} type="button" aria-pressed={draft.lastDurationMinutes === value} disabled={locked} onClick={() => update('lastDurationMinutes', value)}>{draft.lastDurationMinutes === value && <motion.span layoutId="pref-duration-pill" className="segment-pill" aria-hidden="true" transition={spring}/>}<span className="segment-label">{value} {t('minutesShort')}</span></button>)}</div>
              <label className="pref-number"><span className="sr-only">{t('defaultDuration')}</span><input type="number" min="1" max="720" value={draft.lastDurationMinutes} disabled={locked} aria-invalid={!validDuration} onChange={event => update('lastDurationMinutes', Number(event.target.value))}/><span aria-hidden="true">{t('minutesShort')}</span></label>
            </div>
          </PreferenceRow>
        </PreferenceGroup>
      </div>
      <div className="pref-page" role="tabpanel" id={`${tabId('ending')}-panel`} aria-labelledby={tabId('ending')} hidden={section !== 'ending'}>
        <section className="pref-group pref-ending" aria-labelledby="pref-ending-title">
          <header><h3 id="pref-ending-title">{t('sessionEndPresentation')}<InfoTip label={t('moreInfo')} text={t('sessionEndPresentationDetail')}/></h3></header>
          <div className="session-end-options" role="radiogroup" aria-label={t('sessionEndPresentation')}>{sessionEndOptions.map(option => <label key={option} className="session-end-option" data-option={option} data-checked={draft.sessionEnd === option}>
            <input type="radio" name="pref-session-end" value={option} checked={draft.sessionEnd === option} disabled={locked} onChange={() => update('sessionEnd', option)}/>
            <span className="session-end-preview" aria-hidden="true"><span/></span><span className="session-end-label">{t(sessionEndLabels[option])}</span>
          </label>)}</div>
        </section>
        <PreferenceGroup>
          <PreferenceRow t={t} label={t('cyclePhaseNotifications')} detail={t('cyclePhaseNotificationsDetail')}>
            <Switch label={t('cyclePhaseNotifications')} checked={draft.cyclePhaseNotifications} disabled={locked} onChange={value => update('cyclePhaseNotifications', value)}/>
          </PreferenceRow>
        </PreferenceGroup>
      </div>
      <p className="pref-footnote">{t('preferencesSharedWithClassic')}</p>
      <AnimatePresence>{(dirty || saving) && <motion.div className="pref-save-bar" role="region" aria-label={t('unsavedChanges')}
        initial={{ opacity: 0, transform: 'translateY(16px) scale(0.98)' }} animate={{ opacity: 1, transform: 'translateY(0px) scale(1)' }} exit={{ opacity: 0, transform: 'translateY(12px) scale(0.98)', transition: { duration: 0.14 } }}
        transition={{ duration: 0.24, ease: [0.23, 1, 0.32, 1] }}>
        <span>{t('unsavedChanges')}</span>
        <div className="review-actions"><button type="button" disabled={saving} onClick={() => state.preferences && setDraft(state.preferences)}>{t('discardChanges')}</button><button type="button" className="primary-action" disabled={locked || !validDuration} onClick={() => void save()}>{saving ? t('savingPreferences') : t('savePreferences')}</button></div>
      </motion.div>}</AnimatePresence>
    </>}
  </section>;
}

function PreferenceGroup({ title, children }: { title?: string; children: ReactNode }) {
  const id = useId();
  return <section className="pref-group" {...(title ? { 'aria-labelledby': id } : {})}>{title && <header><h3 id={id}>{title}</h3></header>}<div className="pref-rows">{children}</div></section>;
}

/** A setting on one line: label (with its explanation behind an "i") on the left, the control on the right. */
function PreferenceRow({ t, label, detail, stacked = false, children }: { t: Copy; label: string; detail?: string; stacked?: boolean; children: ReactNode }) {
  return <div className="pref-row" data-stacked={stacked}><div className="pref-copy"><span>{label}</span>{detail && <InfoTip label={t('moreInfo')} text={detail}/>}</div><div className="pref-control">{children}</div></div>;
}

function Segmented<Value extends string>({ name, value, options, disabled, onChange }: { name: string; value: Value; options: Option<Value>[]; disabled: boolean; onChange(value: Value): void }) {
  return <div className="segmented" role="radiogroup">{options.map(option => <label key={option.value} data-checked={value === option.value}>
    <input type="radio" name={name} value={option.value} checked={value === option.value} disabled={disabled} onChange={() => onChange(option.value)}/>
    {value === option.value && <motion.span layoutId={`${name}-pill`} className="segment-pill" aria-hidden="true" transition={spring}/>}
    <span className="segment-label">{option.icon}{option.label}</span>
  </label>)}</div>;
}

function Switch({ label, checked, disabled, onChange }: { label: string; checked: boolean; disabled: boolean; onChange(value: boolean): void }) {
  return <label className="switch"><input type="checkbox" role="switch" aria-label={label} checked={checked} disabled={disabled} onChange={event => onChange(event.target.checked)}/><span aria-hidden="true"/></label>;
}
