import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import * as Tabs from '@radix-ui/react-tabs';
import { ActivityLogIcon, CalendarIcon, GearIcon, GlobeIcon, Link2Icon, LockClosedIcon, MoonIcon, PersonIcon, QuestionMarkCircledIcon, ReloadIcon, SunIcon, TimerIcon } from '@radix-ui/react-icons';
import { flushSync } from 'react-dom';
import { useLiveRemaining } from './live-remaining';
import { BlockedStack } from './BlockedStack';
import { ringSegments } from './cycle-schedule';
import { createNativeStatusClient } from './native/client';
import type { ShellClient, ShellSnapshot } from './native/shell';
import { formatClock, formatMoment, formatRemaining } from './formatting';
import es from './locales/es.json';
import en from './locales/en.json';
import { ActiveSessionControls } from './ActiveSessionControls';
import { FocusOrb } from './FocusOrb';
import { MotionConfig } from 'framer-motion';
import { loadOrbStyle, loadPalette, loadReviewBeforeStart, palettes, saveOrbStyle, savePalette, saveReviewBeforeStart, type OrbStyle, type Palette } from './design-system/palettes';
import type { DashboardClient } from './native/dashboard';
import type { ProfilesClient } from './native/profiles';
import type { ApplicationsClient } from './native/applications';
import type { UserPreferences } from './native/preferences';
import type { SessionReplayIntent } from './native/replay';
import type { BridgeStatus } from './native/protocol';
import type { WindowControlSnapshot, WindowControlsClient } from './native/window-controls';

export type StatusClient = ReturnType<typeof createNativeStatusClient>;
type Language = 'es' | 'en';
type CopyKey = keyof typeof es;
const subscribeNoDashboard = (_listener: () => void) => () => undefined;
const getNoDashboardSnapshot = () => null;
const noShellSnapshot: ShellSnapshot = Object.freeze({ mode: 'main', surface: null, navigation: null });
const getNoShellSnapshot = () => noShellSnapshot;
const navigationItems = [
  { key: 'summary', Icon: TimerIcon },
  { key: 'profiles', Icon: PersonIcon },
  { key: 'activity', Icon: ActivityLogIcon },
  { key: 'schedules', Icon: CalendarIcon },
  { key: 'preferences', Icon: GearIcon },
  { key: 'connection', Icon: Link2Icon },
  { key: 'help', Icon: QuestionMarkCircledIcon },
] as const;
const noWindowControlSnapshot: WindowControlSnapshot = Object.freeze({ compact: false, alwaysOnTop: false, detailsExpanded: true, phase: 'waiting', error: null });
const subscribeNoWindowControls = (_listener: () => void) => () => undefined;
const getNoWindowControlSnapshot = () => noWindowControlSnapshot;
const factIcons = { service: Link2Icon, extension: GlobeIcon, protection: LockClosedIcon } as const;
const paletteLabels: Record<Palette, CopyKey> = { aurora: 'paletteAurora', mist: 'paletteMist', ember: 'paletteEmber', ocean: 'paletteOcean' };
const PreferencesPanel = lazy(() => import('./PreferencesPanel').then(module => ({ default: module.PreferencesPanel })));
const ProfileEditor = lazy(() => import('./ProfileEditor').then(module => ({ default: module.ProfileEditor })));
const RecoveryAccess = lazy(() => import('./StrictReviewPanel').then(module => ({ default: module.RecoveryAccess })));
const DashboardStart = lazy(() => import('./DashboardStart').then(module => ({ default: module.DashboardStart })));
const WorkflowViews = lazy(() => import('./WorkflowViews').then(module => ({ default: module.WorkflowViews })));
const SessionEndExperience = lazy(() => import('./SessionEndExperience').then(module => ({ default: module.SessionEndExperience })));
const CompactFocus = lazy(() => import('./CompactFocus').then(module => ({ default: module.CompactFocus })));

export function protectionFactFor(status: BridgeStatus | null | undefined, current: boolean,
  intent: SessionReplayIntent | null): { value: CopyKey; detail: CopyKey; ok: boolean } {
  if (!current || !status) return { value: 'unknown', detail: 'protectionDetail', ok: false };
  if (status.sessionState === 'Inactive' || ['Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState)) {
    return { value: 'notRequested', detail: 'protectionDetail', ok: false };
  }
  const intentMatches = !!status.sessionId && intent?.sessionId.toLowerCase() === status.sessionId.toLowerCase();
  if (!intentMatches || intent?.enforcementEnabled === null) {
    return { value: 'unknown', detail: 'enforcementIntentUnknownDetail', ok: false };
  }
  if (intentMatches && intent?.enforcementEnabled === false) {
    return { value: 'enforcementNotRequested', detail: 'enforcementNotRequestedDetail', ok: false };
  }
  return status.enforcementConnected
    ? { value: 'confirmed', detail: 'protectionDetail', ok: true }
    : { value: 'notConfirmed', detail: 'protectionDetail', ok: false };
}

function DashboardMode({ dashboard, t }: { dashboard: DashboardClient; t(key: CopyKey): string }) {
  const snapshot = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  return <>{snapshot.catalog?.pilotEnabled ? t('pilotMode') : t('readingOnly')}</>;
}

function Mark({ small = false }: { small?: boolean }) {
  return <svg className={small ? 'mark mark-small' : 'mark'} viewBox="0 0 64 64" aria-hidden="true"><path d="M22 9H14a5 5 0 0 0-5 5v8M42 9h8a5 5 0 0 1 5 5v8M55 42v8a5 5 0 0 1-5 5h-8M22 55h-8a5 5 0 0 1-5-5v-8" fill="none" stroke="currentColor" strokeWidth="4" strokeLinecap="round"/><circle cx="32" cy="32" r="7" fill="currentColor"/></svg>;
}

export function App({ client, dashboard, profiles, applications, windowControls, shell }: {
  client: StatusClient; dashboard?: DashboardClient; profiles?: ProfilesClient; applications?: ApplicationsClient; windowControls?: WindowControlsClient; shell?: ShellClient;
}) {
  const [language, setLanguage] = useState<Language>('es');
  const [theme, setTheme] = useState<'dark' | 'light'>('dark');
  const [reduceMotion, setReduceMotion] = useState(false);
  const [palette, setPalette] = useState<Palette>(loadPalette);
  const [orbStyle, setOrbStyle] = useState<OrbStyle>(loadOrbStyle);
  const [reviewBeforeStart, setReviewBeforeStart] = useState(loadReviewBeforeStart);
  // The immersive cabin opens for every running session until the person asks for the dashboard.
  const [cabinDismissedFor, setCabinDismissedFor] = useState<string | null>(null);
  const [preferencesReady, setPreferencesReady] = useState(!dashboard);
  const [cyclePhaseNotifications, setCyclePhaseNotifications] = useState(false);
  const [tab, setTab] = useState('summary');
  const [replaySession, setReplaySession] = useState<SessionReplayIntent | null>(null);
  const [endingSessionId, setEndingSessionId] = useState<string | null>(null);
  const [sessionExperience, setSessionExperience] = useState<{ sessionId: string; mode: UserPreferences['sessionEnd'] } | null>(null);
  const [phaseNotice, setPhaseNotice] = useState<{ id: string; title: string; body: string } | null>(null);
  const [interruptedSessionId, setInterruptedSessionId] = useState<string | null>(null);
  const sessionEndMode = useRef<UserPreferences['sessionEnd'] | null>(null);
  const previousLiveStatus = useRef<{ sessionId: string | null; state: string; mode: string; phaseIndex: number | null } | null>(null);
  const handledEndSessionId = useRef<string | null>(null);
  // Survives stale/error phases so a service restart can be recognised once status returns.
  const lastRunningSession = useRef<{ sessionId: string; endUtc: string | null } | null>(null);
  const compactToggle = useRef<HTMLButtonElement>(null);
  const compactExpand = useRef<HTMLButtonElement>(null);
  const previousCompact = useRef<boolean | null>(null);
  const activeIntentRequestSessionId = useRef<string | null>(null);
  const snapshot = useSyncExternalStore(client.subscribe, client.getSnapshot);
  const dashboardSnapshot = useSyncExternalStore(
    dashboard?.subscribe ?? subscribeNoDashboard,
    dashboard?.getSnapshot ?? getNoDashboardSnapshot,
    dashboard?.getSnapshot ?? getNoDashboardSnapshot,
  );
  const windowState = useSyncExternalStore(windowControls?.subscribe ?? subscribeNoWindowControls,
    windowControls?.getSnapshot ?? getNoWindowControlSnapshot);
  const refresh = useCallback(async () => {
    await client.refresh();
    await dashboard?.loadProfiles(dashboard.getSnapshot().catalog?.offset ?? 0);
    const latestSnapshot = client.getSnapshot();
    const latestStatus = latestSnapshot.status;
    if (dashboard && latestSnapshot.phase === 'ready' && latestStatus?.sessionId
      && ['Active', 'Activating'].includes(latestStatus.sessionState)) {
      const dashboardState = dashboard.getSnapshot();
      if (dashboardState.sessionReplay?.sessionId.toLowerCase() !== latestStatus.sessionId.toLowerCase()
        && !dashboardState.replayLoading) {
        activeIntentRequestSessionId.current = latestStatus.sessionId.toLowerCase();
        await dashboard.loadSessionIntent(latestStatus.sessionId);
      }
    }
  }, [client, dashboard]);
  const t = (key: CopyKey): string => (language === 'es' ? es : en)[key];
  // The tray panel and the notices ask for a section ("Abrir FocusGuard", "Ver resumen", "Preferencias").
  const shellState = useSyncExternalStore(shell?.subscribe ?? subscribeNoDashboard, shell?.getSnapshot ?? getNoShellSnapshot);
  const startup = useMemo(() => shell ? { read: () => shell.startup(), write: (enabled: boolean) => shell.startup(enabled) } : null, [shell]);
  useEffect(() => {
    const navigation = shellState.navigation;
    if (!navigation) return;
    setTab(navigation.section);
    if (navigation.section !== 'summary') setCabinDismissedFor(sessionKey);
  }, [shellState.navigation]);
  const applyPreferences = useCallback((value: UserPreferences) => {
    setLanguage(value.uiLanguage);
    setTheme(value.theme === 'Light' ? 'light' : 'dark');
    setReduceMotion(value.reduceMotion);
    sessionEndMode.current = value.sessionEnd;
    setCyclePhaseNotifications(value.cyclePhaseNotifications);
    setPreferencesReady(true);
  }, []);
  const persistPreferences = useCallback(async (patch: Partial<UserPreferences>) => {
    if (!dashboard) return null;
    const current = dashboard.getSnapshot().preferences;
    if (!current) return null;
    const saved = await dashboard.savePreferences({ ...current, ...patch });
    if (saved) applyPreferences(saved);
    return saved;
  }, [dashboard, applyPreferences]);
  useEffect(() => { client.start(); return () => client.stop(); }, [client]);
  useEffect(() => { windowControls?.start(); return () => windowControls?.stop(); }, [windowControls]);
  useEffect(() => {
    if (previousCompact.current === null) {
      previousCompact.current = windowState.compact;
      return;
    }
    if (previousCompact.current !== windowState.compact) {
      (windowState.compact ? compactExpand : compactToggle).current?.focus();
      previousCompact.current = windowState.compact;
    }
  }, [windowState.compact]);
  useEffect(() => { dashboard?.start(); return () => dashboard?.stop(); }, [dashboard]);
  useEffect(() => {
    if (!dashboard) return;
    void dashboard.loadPreferences().then(() => {
      const value = dashboard.getSnapshot().preferences;
      if (value) applyPreferences(value); else setPreferencesReady(true);
    });
  }, [dashboard, applyPreferences]);
  useEffect(() => { profiles?.start(); return () => profiles?.stop(); }, [profiles]);
  useEffect(() => { applications?.start(); return () => applications?.stop(); }, [applications]);
  // Saved app rules only carry their real icon once the host has the detected-apps catalog for this window, so load it
  // once in the background (never blocking the first paint) and then re-read the profile on screen to pick the icons up.
  const catalogWarmed = useRef(false);
  const hostReady = snapshot.phase === 'ready';
  useEffect(() => {
    if (!applications || !profiles || !hostReady || catalogWarmed.current) return;
    catalogWarmed.current = true;
    const timer = setTimeout(() => void applications.search('').then(() => {
      const { detail, phase } = profiles.getSnapshot();
      if (detail && phase === 'idle' && detail.applications.length > 0)
        void profiles.load(detail.id, detail.applicationOffset, detail.websiteOffset, detail.breakApplicationOffset, detail.breakWebsiteOffset);
    }), 600);
    return () => clearTimeout(timer);
  }, [applications, profiles, hostReady]);
  // Layout effect: descendants read the resulting CSS tokens (orb colours) in their passive effects.
  useLayoutEffect(() => {
    document.documentElement.lang = language;
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.palette = palette;
    document.documentElement.dataset.orb = orbStyle;
    document.documentElement.dataset.reduceMotion = reduceMotion ? 'true' : 'false';
  }, [language, theme, palette, orbStyle, reduceMotion]);
  const status = snapshot.status;
  const current = snapshot.phase === 'ready';
  const active = status?.sessionState === 'Active' || status?.sessionState === 'Activating';
  const inactive = status?.sessionState === 'Inactive';
  const terminal = status && ['Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState);
  // The service keeps strictMode as the session type after it ends; say plainly that blocking was released.
  const strictEnded = !!status && status.strictMode && ['Expired', 'Recovered'].includes(status.sessionState) && !status.enforcementConnected;
  useEffect(() => {
    if (!active || !status?.sessionId) {
      activeIntentRequestSessionId.current = null;
      return;
    }
    if (!dashboard || !current || tab === 'summary') return;
    const sessionId = status.sessionId;
    const key = sessionId.toLowerCase();
    if (dashboardSnapshot?.sessionReplay?.sessionId.toLowerCase() === key) {
      activeIntentRequestSessionId.current = key;
      return;
    }
    if (dashboardSnapshot?.replayLoading || activeIntentRequestSessionId.current === key) return;
    activeIntentRequestSessionId.current = key;
    void dashboard.loadSessionIntent(sessionId);
  }, [active, current, dashboard, dashboardSnapshot?.replayLoading, dashboardSnapshot?.sessionReplay?.sessionId, status?.sessionId, tab]);
  const detail = snapshot.phase === 'stale' ? t('staleDetail') : snapshot.phase === 'unauthorized' ? t('unauthorizedDetail') : t('unavailableDetail');
  const phaseLabel = t(snapshot.phase);
  const stateKey = status?.sessionState as CopyKey | undefined;
  const stateLabel = stateKey && Object.hasOwn(es, stateKey) ? t(stateKey) : t('unknown');
  useLayoutEffect(() => {
    const scrollingElement = document.scrollingElement;
    if (scrollingElement) scrollingElement.scrollTop = 0;
  }, []);
  useLayoutEffect(() => {
    if (snapshot.phase !== 'ready' || !status) return;
    const sessionId = status.sessionId?.toLowerCase() ?? null;
    const previous = previousLiveStatus.current;
    if (!sessionId || !previous) return;
    const wasRunning = ['Active', 'Activating'].includes(previous.state);
    const isTerminal = ['Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState);
    const isNewSession = sessionId !== previous.sessionId;
    const endedRunningSession = !isNewSession && wasRunning && isTerminal;
    if ((isNewSession || endedRunningSession) && document.scrollingElement) document.scrollingElement.scrollTop = 0;
  }, [snapshot.phase, status]);
  useEffect(() => {
    if (snapshot.phase !== 'ready' || !status) { previousLiveStatus.current = null; return; }
    const sessionId = status.sessionId?.toLowerCase() ?? null;
    const previous = previousLiveStatus.current;
    if (previous && sessionId && sessionId === previous.sessionId) {
      const wasRunning = ['Active', 'Activating'].includes(previous.state);
      const isTerminal = ['Expired', 'Stopped', 'Recovered', 'Failed'].includes(status.sessionState);
      if (wasRunning && isTerminal && status.sessionId) setEndingSessionId(status.sessionId);

      const phase = status.currentPhase;
      if (cyclePhaseNotifications && previous.mode === 'FocusCycle' && status.mode === 'FocusCycle'
        && previous.phaseIndex !== null && phase && phase.index !== previous.phaseIndex
        && (phase.kind === 'Focus' || phase.kind === 'ShortBreak')) {
        const time = new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(phase.endUtc));
        const isFocus = phase.kind === 'Focus';
        setPhaseNotice({
          id: `${sessionId}:${phase.index}`,
          title: t(isFocus ? 'phaseNoticeFocusTitle' : 'phaseNoticeBreakTitle'),
          body: isFocus
            ? t('phaseNoticeFocusBody').replace('{cycle}', String(phase.focusCycleNumber)).replace('{time}', time)
            : t('phaseNoticeBreakBody').replace('{time}', time),
        });
      }
    }
    previousLiveStatus.current = { sessionId, state: status.sessionState, mode: status.mode, phaseIndex: status.currentPhase?.index ?? null };
  }, [snapshot.phase, status, cyclePhaseNotifications, language]);
  useEffect(() => {
    if (snapshot.phase !== 'ready' || !status) return;
    const sessionId = status.sessionId?.toLowerCase() ?? null;
    if (sessionId && ['Active', 'Activating'].includes(status.sessionState)) {
      lastRunningSession.current = { sessionId, endUtc: status.endUtc ?? null };
      setInterruptedSessionId(null);
      return;
    }
    const lastRunning = lastRunningSession.current;
    lastRunningSession.current = null;
    // A stop or expiry keeps the session ID; only a restarted service reports Inactive without one.
    if (lastRunning && status.sessionState === 'Inactive' && !sessionId
      && (!lastRunning.endUtc || Date.parse(lastRunning.endUtc) > Date.now())) setInterruptedSessionId(lastRunning.sessionId);
  }, [snapshot.phase, status]);
  useEffect(() => {
    if (!dashboard || !endingSessionId || !preferencesReady) return;
    const key = endingSessionId.toLowerCase();
    if (handledEndSessionId.current === key) { setEndingSessionId(null); return; }
    handledEndSessionId.current = key;
    const mode = sessionEndMode.current;
    if (!mode || mode === 'None') { setEndingSessionId(null); return; }
    let current = true;
    void dashboard.loadSessionSummary(endingSessionId).then(summary => {
      if (!current) return;
      if (summary && summary.sessionId.toLowerCase() === key) setSessionExperience({ sessionId: endingSessionId, mode });
      setEndingSessionId(null);
    });
    return () => { current = false; };
  }, [dashboard, endingSessionId, preferencesReady]);
  useEffect(() => {
    if (!phaseNotice) return;
    const timer = window.setTimeout(() => setPhaseNotice(current => current?.id === phaseNotice.id ? null : current), 9000);
    return () => window.clearTimeout(timer);
  }, [phaseNotice]);
  const protection = protectionFactFor(status, current, dashboardSnapshot?.sessionReplay ?? null);
  const facts = [
    { key: 'service', title: t('service'), value: !current ? t('unknown') : status?.serviceReady ? t('connected') : t('notConnected'), detail: t('serviceDetail'), ok: current && status?.serviceReady },
    { key: 'extension', title: t('extension'), value: !current ? t('unknown') : status?.extensionReady ? t('available') : t('notConnected'), detail: t('extensionDetail'), ok: current && status?.extensionReady },
    { key: 'protection', title: t('protection'), value: t(protection.value), detail: t(protection.detail), ok: protection.ok },
  ];
  const renderFact = (fact: (typeof facts)[number]) => { const FactIcon = factIcons[fact.key as keyof typeof factIcons]; return <article className="fact" key={fact.key} data-fact={fact.key} data-ok={fact.ok ? 'true' : 'false'}>
    <span className="fact-icon" aria-hidden="true"><FactIcon focusable="false"/></span>
    <div className="fact-title"><span>{fact.title}</span><span className={`fact-indicator ${fact.ok ? 'ok' : ''}`} aria-hidden="true"/></div>
    <h3 aria-live={fact.key === 'protection' && active ? 'polite' : undefined} aria-atomic={fact.key === 'protection' && active ? true : undefined}>{fact.value}</h3>
    <p>{fact.detail}</p>
  </article>; };
  const navigationDisabled = (key: (typeof navigationItems)[number]['key']) => key === 'profiles' && !profiles || ['activity', 'schedules', 'preferences'].includes(key) && !dashboard;
  const compactAvailable = !!dashboard && !!windowControls;
  const sessionKey = status?.sessionId?.toLowerCase() ?? null;
  const cabinAvailable = active && current && !!sessionKey && !(compactAvailable && windowState.compact);
  const immersive = cabinAvailable && tab === 'summary' && cabinDismissedFor !== sessionKey;
  const sessionSeconds = status?.startUtc && status.endUtc ? (Date.parse(status.endUtc) - Date.parse(status.startUtc)) / 1000 : 0;
  const liveRemaining = useLiveRemaining(snapshot) ?? 0;
  const sessionIntent = dashboardSnapshot?.sessionReplay && sessionKey && dashboardSnapshot.sessionReplay.sessionId.toLowerCase() === sessionKey
    ? dashboardSnapshot.sessionReplay : null;
  const cycleConfiguration = status?.mode === 'FocusCycle' ? sessionIntent?.cycleConfiguration ?? null : null;
  const cycleSegments = cycleConfiguration ? ringSegments(cycleConfiguration) : null;
  const blockedSummary = profiles && sessionIntent?.profile && sessionIntent.enforcementEnabled
    ? <BlockedStack profiles={profiles} profileId={sessionIntent.profile.id} blockedApps={sessionIntent.profile.blockedApplicationCount}
      blockedSites={sessionIntent.profile.blockedWebsiteCount} t={t}/> : null;
  const sessionProgress = active && status && sessionSeconds > 0 ? liveRemaining / sessionSeconds : 1;
  // The cabin swaps in through a view transition so nothing appears or disappears at once.
  const [cabinShown, setCabinShown] = useState(immersive);
  useEffect(() => {
    if (cabinShown === immersive) return;
    const doc = document as Document & { startViewTransition?(update: () => void): unknown };
    const still = reduceMotion || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    if (doc.startViewTransition && !still) doc.startViewTransition(() => flushSync(() => setCabinShown(immersive)));
    else setCabinShown(immersive);
  }, [immersive, cabinShown, reduceMotion]);
  useEffect(() => {
    if (!immersive) return;
    const leave = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented || document.querySelector('dialog[open]')) return;
      setCabinDismissedFor(sessionKey);
    };
    window.addEventListener('keydown', leave);
    return () => window.removeEventListener('keydown', leave);
  }, [immersive, sessionKey]);
  return <MotionConfig reducedMotion={reduceMotion ? 'always' : 'user'}><Tabs.Root className="app-shell" data-immersive={cabinShown ? 'true' : 'false'} data-window-mode={compactAvailable && windowState.compact ? 'compact' : 'full'} orientation="vertical" value={tab} onValueChange={setTab}>
    <a className="skip-link" href="#main">{t('skip')}</a>
    <aside className="sidebar" aria-label="FocusGuard">
      <div className="brand"><Mark small/><span>FocusGuard</span></div>
      <span className="rail-caption">{dashboard ? <DashboardMode dashboard={dashboard} t={t}/> : t('readingOnly')}</span>
      <label className="mobile-navigation-label" htmlFor="mobile-navigation">{t('mobileNavigation')}</label>
      <select id="mobile-navigation" className="mobile-navigation" value={tab} onChange={event => setTab(event.target.value)}>
        {navigationItems.map(({ key }) => <option key={key} value={key} disabled={navigationDisabled(key)}>{t(key)}</option>)}
      </select>
      <Tabs.List className="navigation" aria-label={t('mainNavigation')}>
        {navigationItems.map(({ key, Icon }) => <Tabs.Trigger key={key} value={key} disabled={navigationDisabled(key)}><Icon className="nav-icon" aria-hidden="true" focusable="false" /><span>{t(key)}</span></Tabs.Trigger>)}
      </Tabs.List>
      <p className="sidebar-note">{t('continuity')}</p>
      <div className={`connection-strip ${snapshot.phase}`} role="status" aria-live="polite"><span className="status-dot" aria-hidden="true"/><span className="connection-label">{phaseLabel}</span><button type="button" className="connection-refresh" title={t('refresh')} onClick={() => void refresh()} disabled={snapshot.phase === 'loading'}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{snapshot.phase === 'loading' ? t('refreshing') : t('refresh')}</span></button></div>
    </aside>
    <main id="main" tabIndex={-1} className="main">
      <header className="page-header"><div><h1>{tab === 'profiles' ? t('profilesTitle') : tab === 'activity' ? t('activityTitle') : tab === 'schedules' ? t('schedulesTitle') : tab === 'preferences' ? t('preferencesTitle') : tab === 'help' ? t('helpTitle') : tab === 'connection' ? t('connectionTitle') : t('greeting')}</h1><p className="subtitle">{tab === 'profiles' ? t('profilesSubtitle') : tab === 'activity' ? t('activityDetail') : tab === 'schedules' ? t('schedulesDetail') : tab === 'preferences' ? t('preferencesDetail') : tab === 'connection' ? t('connectionDetail') : t('subtitle')}</p></div>
        <div className="page-actions">{dashboard && windowControls && <><button ref={compactToggle} className="compact-toggle" type="button" aria-keyshortcuts="Control+Shift+M" disabled={windowState.phase === 'writing' || windowState.phase === 'waiting'} onClick={() => void windowControls.setCompactMode(true)}><span>{t('compactMode')}</span><kbd>Ctrl+Shift+M</kbd></button>{windowState.phase === 'error' && !windowState.compact && <span className="window-mode-error" role="alert">{t('windowActionFailed')}</span>}</>}
          {cabinAvailable && !immersive && <button type="button" className="return-to-focus" onClick={() => { setCabinDismissedFor(null); setTab('summary'); }}>{t('immersiveEnter')}</button>}
          {!dashboard && <><div className="ambience" role="group" aria-label={t('ambience')}>{palettes.map(value => <button key={value} type="button" className="ambience-swatch" data-swatch={value} aria-pressed={palette === value} aria-label={t(paletteLabels[value])} title={t(paletteLabels[value])} onClick={() => { setPalette(value); savePalette(value); }}/>)}</div>
          <div className="preferences"><label className="sr-only" htmlFor="language">{t('language')}</label><select id="language" value={language} disabled={!!dashboard && !preferencesReady} onChange={event => { const value = event.target.value as Language; setLanguage(value); void persistPreferences({ uiLanguage: value }); }}><option value="es">ES</option><option value="en">EN</option></select><button type="button" className="icon-button" disabled={!!dashboard && !preferencesReady} aria-label={`${t('appearance')}: ${theme === 'dark' ? t('light') : t('dark')}`} onClick={() => { const value = theme === 'dark' ? 'light' : 'dark'; setTheme(value); void persistPreferences({ theme: value === 'light' ? 'Light' : 'Dark' }); }}>{theme === 'dark' ? <SunIcon className="theme-icon" aria-hidden="true" focusable="false" /> : <MoonIcon className="theme-icon" aria-hidden="true" focusable="false" />}</button></div></>}
        </div>
      </header>
      {dashboard && windowControls && <Suspense fallback={<div className="compact-focus compact-loading" role="status">{t('compactLoading')}</div>}><CompactFocus connection={snapshot} dashboard={dashboard} windowControls={windowControls}
        windowState={windowState} language={language} t={t} refresh={refresh} expandButtonRef={compactExpand}/></Suspense>}
      {(['stale', 'unauthorized'] as const).some(phase => phase === snapshot.phase) && <p className="notice">{detail}</p>}
      <Tabs.Content value="summary"><div className={`session-overview${active && current ? ' session-overview-active' : ''}`}><section className="session-surface" aria-label={t('session')}>
        {(active || !dashboard || !status || status.strictMode || !(inactive || terminal)) && <div className="session-heading"><h2 id="session-heading">{t('session')}</h2>{status && <span className={`badge ${status.strictMode && !strictEnded ? 'strict' : ''}`}>{strictEnded ? t('strictEndedBadge') : status.strictMode ? t('strict') : t('normal')}{status.mode === 'FocusCycle' ? ' · ' + t('cycle') : ''}</span>}{cabinShown && <button type="button" className="cabin-exit" aria-keyshortcuts="Escape" onClick={() => setCabinDismissedFor(sessionKey)}>{t('immersiveExit')}</button>}</div>}
        {(active || !dashboard || !status || status.strictMode || !(inactive || terminal)) && <div className="session-body">{active && status
          ? <FocusOrb mode="countdown" progress={sessionProgress} segments={cycleSegments} className="session-orb"><p className="session-state">{stateLabel}</p><p className="timer" aria-label={`${t('remaining')}: ${formatRemaining(liveRemaining)}`}>{formatClock(liveRemaining)}</p>{status.mode === 'FocusCycle' && <p className="start-note">{t('sessionRemaining')}</p>}</FocusOrb>
          : <><div className="focus-symbol"><Mark/></div>{status && <p className="session-state">{stateLabel}</p>}</>}{cabinShown && <p className="cabin-breathe">{t('breathe')}</p>}{status?.sessionState === 'Activating' && <p className="activation-pending" role="status">{t('activationPendingDetail')}</p>}{active && status ? <>{status.mode === 'FocusCycle' && <div className="active-phase">{status.currentPhase ? <><span>{t('currentPhase')} · {t('focusCycleNumber')} {status.currentPhase.focusCycleNumber}</span><strong className={status.currentPhase.kind === 'Focus' ? 'active-focus-phase' : 'active-break-phase'}>{t(status.currentPhase.kind)}</strong><p>{t('phaseRemaining')}: {formatRemaining(status.currentPhase.remainingSeconds)} · {t('until')} {new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(status.currentPhase.endUtc))}</p>{status.nextPhase && <small>{t('nextPhase')}: {t(status.nextPhase.kind)}</small>}</> : <p>{t('phaseUnavailable')}</p>}</div>}{status.endUtc && <p className="deadline">{t('until')} · {formatMoment(status.endUtc, language)}</p>}</> : status ? <><p className="idle-title">{inactive ? t('noSession') : stateLabel}</p><p className="session-guidance">{strictEnded ? t('strictEndedDetail') : inactive || terminal ? t('noSessionDetail') : t('pendingDetail')}</p></> : <><p className="idle-title">{t(snapshot.phase === 'loading' ? 'loading' : 'statusUnavailableTitle')}</p><p className="session-guidance">{snapshot.phase === 'loading' ? t('waiting') : detail}</p></>}</div>}
        {dashboard && <Suspense fallback={<p role="status">{t('startLoading')}</p>}><DashboardStart dashboard={dashboard} blockedSummary={blockedSummary} reviewBeforeStart={reviewBeforeStart} {...(profiles ? { renderBlockedPreview: (profileId: string, apps: number, sites: number) => <BlockedStack profiles={profiles} profileId={profileId} blockedApps={apps} blockedSites={sites} t={t} showSites/> } : {})} connection={snapshot} language={language} t={t} refresh={refresh} replay={replaySession} onReplayApplied={() => setReplaySession(null)}/></Suspense>}{dashboard && <ActiveSessionControls dashboard={dashboard} connection={snapshot} language={language} t={t} refresh={refresh}/>}<div className="session-footer"><span>{t('continuity')}</span><span>{snapshot.updatedAt ? `${t('lastUpdate')} · ${formatMoment(snapshot.updatedAt, language)}` : t('notYet')}</span></div>
        {status?.strictMode && active && <aside className="strict-recovery-inline" aria-label={t('strictRecoveryActiveTitle')}><strong>{t('strictRecoveryActiveTitle')}</strong><p>{t('strictRecoveryActiveDetail')}</p><code>%ProgramFiles%\FocusGuard\FocusGuard.Recovery.exe</code><button type="button" onClick={() => setTab('help')}>{t('recoveryAccessTitle')}</button></aside>}
      </section>{current && <section className="facts" aria-label={t('connectionTitle')}>{facts.map(renderFact)}</section>}</div></Tabs.Content>
      {profiles && dashboard && <Tabs.Content value="profiles"><Suspense fallback={<p role="status">{t('profilesLoading')}</p>}><ProfileEditor {...(applications ? { applications } : {})} profiles={profiles} dashboard={dashboard} connection={snapshot} language={language} t={t}/></Suspense></Tabs.Content>}<Tabs.Content value="connection"><section className="connection-intro"><h2>{t('connectionTitle')}</h2><p>{t('connectionDetail')}</p></section>{current && <section className="facts" aria-label={t('connectionTitle')}>{facts.map(renderFact)}</section>}</Tabs.Content>
      {dashboard && <Tabs.Content value="activity"><Suspense fallback={<p role="status">{t('activityLoading')}</p>}><WorkflowViews dashboard={dashboard} connection={snapshot} language={language} t={t} view="activity" onRepeat={replay => { setReplaySession(replay); setTab('summary'); }}/></Suspense></Tabs.Content>}
      {dashboard && <Tabs.Content value="schedules"><Suspense fallback={<p role="status">{t('schedulesLoading')}</p>}><WorkflowViews dashboard={dashboard} connection={snapshot} language={language} t={t} view="schedules"/></Suspense></Tabs.Content>}
      {dashboard && <Tabs.Content value="preferences"><Suspense fallback={<p role="status">{t('preferencesLoading')}</p>}><PreferencesPanel dashboard={dashboard} t={t} onSaved={applyPreferences} palette={palette} onPaletteChange={value => { setPalette(value); savePalette(value); }} orbStyle={orbStyle} onOrbStyleChange={value => { setOrbStyle(value); saveOrbStyle(value); }} reviewBeforeStart={reviewBeforeStart} onReviewBeforeStartChange={value => { setReviewBeforeStart(value); saveReviewBeforeStart(value); }} {...(startup ? { startup } : {})}/></Suspense></Tabs.Content>}
      <Tabs.Content value="help"><section className="help-list">{(['helpClose', 'helpOffline', 'helpManage', 'helpBreaks'] as const).map(key => <article key={key}><h2>{t(key)}</h2><p>{t(`${key}Detail` as CopyKey)}</p></article>)}</section>{dashboard && <Suspense fallback={<p role="status">{t('recoveryInfoLoading')}</p>}><RecoveryAccess dashboard={dashboard} t={t}/></Suspense>}</Tabs.Content>
      {dashboard && sessionExperience && <Suspense fallback={<p role="status">{t('summaryLoading')}</p>}><SessionEndExperience dashboard={dashboard} connection={snapshot} sessionId={sessionExperience.sessionId} mode={sessionExperience.mode} language={language} t={t} onClose={() => setSessionExperience(null)} onRepeat={replay => { setReplaySession(replay); setTab('summary'); }}/></Suspense>}
      {interruptedSessionId && <aside className="phase-notice interruption-notice" role="alert" aria-atomic="true"><div><strong>{t('interruptionNoticeTitle')}</strong><span>{t('interruptionNoticeBody')}</span></div><div className="notice-actions">{dashboard && <button type="button" onClick={() => { setInterruptedSessionId(null); setTab('activity'); }}>{t('interruptionNoticeActivity')}</button>}<button type="button" onClick={() => setInterruptedSessionId(null)}>{t('close')}</button></div></aside>}
      {phaseNotice && <aside className="phase-notice" role="status" aria-live="polite" aria-atomic="true"><div><strong>{phaseNotice.title}</strong><span>{phaseNotice.body}</span></div><button type="button" aria-label={t('close')} onClick={() => setPhaseNotice(null)}>{t('close')}</button></aside>}
    </main>
    <div className="atmosphere" aria-hidden="true"><span/><span/><span/></div>
  </Tabs.Root></MotionConfig>;
}
