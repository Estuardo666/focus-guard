import { Suspense, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from 'react';
import { MotionConfig } from 'framer-motion';
import { Cross2Icon, DesktopIcon, EnterIcon, ExitIcon, GearIcon, GlobeIcon, LockClosedIcon, SizeIcon, TimerIcon } from '@radix-ui/react-icons';
import type { DashboardClient } from './native/dashboard';
import type { NativeStatusClient as StatusClient } from './native/client';
import type { ShellClient, Surface, ToastPayload } from './native/shell';
import type { SessionSummary } from './native/history';
import { useLiveRemaining } from './live-remaining';
import { formatClock } from './formatting';
import { formatSummaryFocusedMinutes } from './focus-time';
import { loadOrbStyle, loadPalette } from './design-system/palettes';
import { FocusOrb } from './FocusOrb';
import { SessionHistoryDetailLazy } from './SessionHistoryDetailLazy';
import es from './locales/es.json';
import en from './locales/en.json';

type CopyKey = keyof typeof es;
type Copy = (key: CopyKey) => string;
type Language = 'es' | 'en';

/**
 * The small desktop surfaces that live outside the main window (they replace the Avalonia ones): the notification-area
 * panel, session and phase notices, the session summary and the block screen. Each reads the service through its own
 * bridge and asks the host to open the main window, the compact view, close itself, or quit.
 */
export function SurfaceApp({ surface, shell, client, dashboard }: { surface: Surface; shell: ShellClient; client: StatusClient; dashboard: DashboardClient }) {
  const [language, setLanguage] = useState<Language>('es');
  const [reduceMotion, setReduceMotion] = useState(false);
  const [prepared, setPrepared] = useState(false);
  const t: Copy = key => (language === 'es' ? es : en)[key];
  useEffect(() => {
    let live = true;
    void dashboard.loadPreferences().catch(() => undefined).then(() => {
      if (!live) return;
      const preferences = dashboard.getSnapshot().preferences;
      if (preferences) {
        setLanguage(preferences.uiLanguage);
        setReduceMotion(preferences.reduceMotion);
        document.documentElement.dataset.theme = preferences.theme === 'Light' ? 'light' : 'dark';
      }
      setPrepared(true);
    });
    return () => { live = false; };
  }, [dashboard]);
  // The host keeps the popup off-screen until this: theme, language and first paint are done (two frames).
  useEffect(() => {
    if (!prepared) return;
    let second = 0;
    const first = requestAnimationFrame(() => { second = requestAnimationFrame(() => { shell.ready(); }); });
    return () => { cancelAnimationFrame(first); cancelAnimationFrame(second); };
  }, [prepared, shell]);
  useLayoutEffect(() => {
    const root = document.documentElement;
    root.lang = language;
    root.dataset.palette = loadPalette();
    root.dataset.orb = loadOrbStyle();
    root.dataset.reduceMotion = reduceMotion ? 'true' : 'false';
    root.dataset.surface = surface.surface;
  }, [language, reduceMotion, surface.surface]);
  if (!prepared) return null;
  return <MotionConfig reducedMotion={reduceMotion ? 'always' : 'user'}>
    {surface.surface === 'tray' && <TrayPanel shell={shell} client={client} dashboard={dashboard} t={t} language={language}/>}
    {surface.surface === 'toast' && <Notice payload={surface.payload} shell={shell} dashboard={dashboard} t={t} language={language}/>}
    {surface.surface === 'summary' && <SummarySurface sessionId={surface.sessionId} shell={shell} client={client} dashboard={dashboard} t={t} language={language}/>}
    {surface.surface === 'block' && <BlockSurface target={surface.target} startUtc={surface.startUtc} endUtc={surface.endUtc} strict={surface.strict} shell={shell} t={t} language={language}/>}
  </MotionConfig>;
}

const shortTime = (language: Language, utc: string) => new Intl.DateTimeFormat(language === 'es' ? 'es-EC' : 'en-US', { timeStyle: 'short' }).format(new Date(utc));

/** Notification-area panel: what is running right now and the four things people do from the tray. */
function TrayPanel({ shell, client, dashboard, t, language }: { shell: ShellClient; client: StatusClient; dashboard: DashboardClient; t: Copy; language: Language }) {
  const snapshot = useSyncExternalStore(client.subscribe, client.getSnapshot);
  const listing = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  useEffect(() => { void dashboard.loadProfiles(0); }, [dashboard]);
  const remaining = useLiveRemaining(snapshot);
  const status = snapshot.status;
  const active = !!status && ['Active', 'Activating'].includes(status.sessionState);
  const profile = active ? listing.catalog?.items.find(item => item.id.toLowerCase() === status.profileId?.toLowerCase()) : undefined;
  const total = status?.startUtc && status.endUtc ? (Date.parse(status.endUtc) - Date.parse(status.startUtc)) / 1000 : 0;
  const progress = active && total > 0 ? Math.min(1, Math.max(0, 1 - (remaining ?? status.remainingSeconds) / total)) : 0;
  return <main className="surface tray-panel" aria-label="FocusGuard">
    <header className="tray-head">
      <span className="orb-thumb" data-orb={document.documentElement.dataset.orb ?? 'pearl'} aria-hidden="true"/>
      <div><strong>FocusGuard</strong><span>{snapshot.phase !== 'ready' ? t('trayConnecting') : active ? (status.strictMode ? t('strict') : t('trayProtected')) : t('trayIdle')}</span></div>
      <span className="tray-dot" data-on={active} aria-hidden="true"/>
    </header>
    {active ? <section className="tray-session" aria-live="polite">
      <p className="tray-timer" aria-label={t('remaining')}>{formatClock(remaining ?? status.remainingSeconds)}</p>
      <div className="tray-progress" aria-hidden="true"><span style={{ transform: `scaleX(${progress})` }}/></div>
      <dl className="tray-facts">
        <div><dt><TimerIcon aria-hidden="true"/>{t('trayProfile')}</dt><dd>{profile?.name ?? '—'}</dd></div>
        {profile && <div><dt><GlobeIcon aria-hidden="true"/>{t('websitesTitle')}</dt><dd>{profile.blockedWebsiteCount}</dd></div>}
        {profile && <div><dt><DesktopIcon aria-hidden="true"/>{t('applicationsTitle')}</dt><dd>{profile.blockedApplicationCount}</dd></div>}
        {status.endUtc && <div><dt><LockClosedIcon aria-hidden="true"/>{t('trayEndsAt')}</dt><dd>{shortTime(language, status.endUtc)}</dd></div>}
      </dl>
    </section> : <section className="tray-idle">
      <p>{t('trayIdleHint')}</p>
      <button type="button" className="primary-action" onClick={() => shell.open('summary')}>{t('trayStart')}</button>
    </section>}
    <nav className="tray-actions" aria-label={t('trayActions')}>
      <button type="button" onClick={() => shell.open('summary')}><EnterIcon aria-hidden="true"/>{t('trayOpen')}</button>
      <button type="button" onClick={() => shell.openCompact()}><SizeIcon aria-hidden="true"/>{t('compactMode')}</button>
      <button type="button" onClick={() => shell.open('preferences')}><GearIcon aria-hidden="true"/>{t('preferences')}</button>
      <button type="button" className="tray-quit" onClick={() => shell.quit()}><ExitIcon aria-hidden="true"/><span>{t('trayQuit')}{active && <small>{t('trayQuitKeeps')}</small>}</span></button>
    </nav>
  </main>;
}

/** Corner notice for a finished session or a new cycle phase; it never takes focus and leaves on its own. */
function Notice({ payload, shell, dashboard, t, language }: { payload: ToastPayload; shell: ShellClient; dashboard: DashboardClient; t: Copy; language: Language }) {
  const [summary, setSummary] = useState<SessionSummary | null>(null);
  const hovered = useRef(false);
  useEffect(() => {
    if (payload.kind !== 'sessionEnd') return;
    void dashboard.loadSessionSummary(payload.sessionId).then(() => {
      const value = dashboard.getSnapshot().sessionSummary;
      if (value && value.sessionId.toLowerCase() === payload.sessionId.toLowerCase()) setSummary(value);
    });
  }, [dashboard, payload]);
  useEffect(() => {
    const timer = window.setInterval(() => { if (!hovered.current) { window.clearInterval(timer); shell.close(); } }, 9000);
    return () => window.clearInterval(timer);
  }, [shell]);
  let title: string, body: string;
  if (payload.kind === 'sessionEnd') {
    title = summary ? t(summary.outcome === 'Completed' ? 'sessionCompleted' : 'sessionEndedEarly') : t('sessionCompleted');
    body = summary ? `${formatSummaryFocusedMinutes(summary.focusedMinutes, summary.plannedMinutes, t)} · ${summary.profileName}` : '';
  } else {
    const focus = payload.startedKind === 'Focus';
    const time = payload.startedEndUtc ? shortTime(language, payload.startedEndUtc) : '';
    title = payload.startedKind ? t(focus ? 'phaseNoticeFocusTitle' : 'phaseNoticeBreakTitle') : t(payload.endedKind === 'Focus' ? 'phaseNoticeFocusEnded' : 'phaseNoticeBreakEnded');
    body = payload.startedKind
      ? focus ? t('phaseNoticeFocusBody').replace('{cycle}', String(payload.startedCycle ?? '')).replace('{time}', time) : t('phaseNoticeBreakBody').replace('{time}', time)
      : '';
    // "p. m." already ends in a period.
    body = body.replace(/\.\.$/, '.');
  }
  return <main className="surface notice-surface" role="status" aria-live="polite" onMouseEnter={() => { hovered.current = true; }} onMouseLeave={() => { hovered.current = false; }}>
    <span className="orb-thumb" data-orb={document.documentElement.dataset.orb ?? 'pearl'} aria-hidden="true"/>
    <div className="notice-copy"><strong>{title}</strong>{body && <span>{body}</span>}</div>
    <div className="notice-actions">
      {payload.kind === 'sessionEnd' && <button type="button" onClick={() => shell.open('activity')}>{t('viewSessionSummary')}</button>}
      <button type="button" className="notice-close" aria-label={t('close')} onClick={() => shell.close()}><Cross2Icon aria-hidden="true"/></button>
    </div>
  </main>;
}

/** The end-of-session summary as its own window, with the same detail the Activity screen shows. */
function SummarySurface({ sessionId, shell, client, dashboard, t, language }: { sessionId: string; shell: ShellClient; client: StatusClient; dashboard: DashboardClient; t: Copy; language: Language }) {
  const connection = useSyncExternalStore(client.subscribe, client.getSnapshot);
  useEffect(() => { void dashboard.loadProfiles(0); void dashboard.loadSessionSummary(sessionId); }, [dashboard, sessionId]);
  return <main className="surface summary-surface">
    <Suspense fallback={<p role="status">{t('summaryLoading')}</p>}>
      <SessionHistoryDetailLazy dashboard={dashboard} connection={connection} sessionId={sessionId} language={language} t={t}
        close={() => shell.close()} onRepeat={() => shell.open('summary')}/>
    </Suspense>
    <footer className="summary-surface-actions"><button type="button" className="ghost-action" onClick={() => shell.open('activity')}>{t('trayOpen')}</button></footer>
  </main>;
}

/** Full-screen pause when a blocked app is opened: calm, with the time left, and a way back. Esc is ignored; the Windows key is not. */
function BlockSurface({ target, startUtc, endUtc, strict, shell, t, language }: { target: string; startUtc: string; endUtc: string; strict: boolean; shell: ShellClient; t: Copy; language: Language }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(timer); }, []);
  const end = Date.parse(endUtc), start = Date.parse(startUtc);
  const remaining = Math.max(0, Math.round((end - now) / 1000));
  const progress = end > start ? Math.min(1, Math.max(0, (now - start) / (end - start))) : 0;
  const name = target.replace(/^.*[\\/]/, '').replace(/\.exe$/i, '');
  useEffect(() => { if (remaining === 0) shell.close(); }, [remaining, shell]);
  return <main className="surface block-surface" aria-labelledby="block-title">
    <div className="atmosphere" aria-hidden="true"><span/><span/><span/></div>
    <FocusOrb mode="countdown" progress={progress} className="block-orb"><p className="timer">{formatClock(remaining)}</p></FocusOrb>
    <h1 id="block-title">{t('blockTitle').replace('{app}', name)}</h1>
    <p className="block-body">{t('blockBody').replace('{time}', shortTime(language, endUtc)).replace(/\.\.$/, '.')}</p>
    {strict && <p className="block-strict"><LockClosedIcon aria-hidden="true"/>{t('blockStrict')}</p>}
    <button type="button" className="primary-action" autoFocus onClick={() => shell.close()}>{t('blockBack')}</button>
  </main>;
}
