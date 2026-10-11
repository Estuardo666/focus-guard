import { useEffect, useSyncExternalStore } from 'react';
import { GlobeIcon } from '@radix-ui/react-icons';
import { ApplicationIcon } from './ApplicationsPicker';
import type { ProfilesClient } from './native/profiles';
import type es from './locales/es.json';

type Copy = (key: keyof typeof es) => string;
const visibleApps = 5;
const visibleSites = 3;

/**
 * What a profile blocks, at a glance: overlapping app icons (first five, then +N) and the blocked websites, either as a
 * count or, with `showSites`, as their first patterns. Counts come from the profile summary; names and icons come from the
 * saved profile rules, so nobody has to open the Profiles screen to know what a session will block.
 */
export function BlockedStack({ profiles, profileId, blockedApps, blockedSites, t, showSites = false }: {
  profiles: ProfilesClient; profileId: string; blockedApps: number; blockedSites: number; t: Copy; showSites?: boolean;
}) {
  const snapshot = useSyncExternalStore(profiles.subscribe, profiles.getSnapshot);
  const detail = snapshot.detail?.id.toLowerCase() === profileId.toLowerCase() ? snapshot.detail : null;
  useEffect(() => {
    if (blockedApps + blockedSites > 0 && !detail && snapshot.phase === 'idle') void profiles.load(profileId);
  }, [profiles, profileId, blockedApps, blockedSites, detail, snapshot.phase]);
  if (blockedApps + blockedSites === 0) return null;
  const apps = (detail?.applications ?? []).filter(rule => rule.action === 'Block').slice(0, visibleApps);
  const moreApps = Math.max(0, blockedApps - apps.length);
  const sites = showSites ? (detail?.websites ?? []).filter(rule => rule.action === 'Block').slice(0, visibleSites) : [];
  const moreSites = Math.max(0, blockedSites - sites.length);
  const appsLabel = t(blockedApps === 1 ? 'appBlockedOne' : 'appsBlockedMany').replace('{count}', String(blockedApps));
  const sitesLabel = t(blockedSites === 1 ? 'siteBlockedOne' : 'sitesBlockedMany').replace('{count}', String(blockedSites));
  return <div className="blocked-stack">
    {blockedApps > 0 && <div className="app-stack" role="img" aria-label={appsLabel}>
      {apps.map(rule => { const name = rule.presentation?.name ?? rule.value.replace(/^.*[\\/]/, ''); return <span key={rule.id} className="stack-avatar" title={name}><ApplicationIcon icon={rule.presentation?.icon ?? null} name={name}/></span>; })}
      {moreApps > 0 && <span className="stack-avatar stack-more" aria-hidden="true">+{moreApps}</span>}
    </div>}
    {blockedSites > 0 && (sites.length > 0
      ? <ul className="site-chips" aria-label={sitesLabel}><GlobeIcon aria-hidden="true" focusable="false"/>{sites.map(rule => <li key={rule.id}>{rule.pattern.replace(/^\*\./, '')}</li>)}{moreSites > 0 && <li className="site-more">+{moreSites}</li>}</ul>
      : <span className="sites-blocked"><GlobeIcon aria-hidden="true" focusable="false"/>{sitesLabel}</span>)}
  </div>;
}
