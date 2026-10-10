import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { decodeIcon, type AppIcon, type ApplicationsClient, type CatalogApplication } from './native/applications';
import type { RuleAction } from './native/profiles';
import type es from './locales/es.json';
type Copy = (key: keyof typeof es) => string;
export function ApplicationIcon({ icon, name }: { icon: AppIcon | null; name: string }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  useEffect(() => { const pixels = decodeIcon(icon); const context = canvas.current?.getContext('2d'); if (pixels && context) { const image = context.createImageData(32, 32); image.data.set(pixels); context.putImageData(image, 0, 0); } }, [icon]);
  return icon ? <canvas ref={canvas} className="catalog-icon" width={32} height={32} aria-hidden="true"/> : <span className="catalog-icon fallback-icon" aria-hidden="true">{name.slice(0, 1).toUpperCase()}</span>;
}
export function ApplicationsPicker({ applications, enabled, t, add }: { applications: ApplicationsClient; enabled: boolean; t: Copy; add(application: CatalogApplication, action: RuleAction): Promise<void> }) {
  const state = useSyncExternalStore(applications.subscribe, applications.getSnapshot);
  const [query, setQuery] = useState(''); const [action, setAction] = useState<RuleAction>('Block');
  const [isOpen, setIsOpen] = useState(false);
  const requestedQuery = useRef<string | null>(null);
  useEffect(() => {
    if (!isOpen || requestedQuery.current === query) return;
    const timer = setTimeout(() => { requestedQuery.current = query; void applications.search(query); }, 250);
    return () => clearTimeout(timer);
  }, [applications, isOpen, query]);
  return <details className="applications-picker" open={isOpen}>
    <summary className="catalog-disclosure-summary" onClick={event => { event.preventDefault(); setIsOpen(open => !open); }}>
      <span className="catalog-disclosure-copy"><strong>{t('installedApplications')}</strong><small>{t('catalogDisclosureHint')}</small></span>
    </summary>
    <div className="catalog-disclosure-content">
      <div className="catalog-heading"><p id="catalog-discovery-detail" className="start-note">{t('catalogDiscoveryDetail')}</p><button type="button" disabled={state.loading} onClick={() => void applications.search(query, 0, true)}>{t('reloadCatalog')}</button></div>
      <label className="catalog-search-label" htmlFor="catalog-search">{t('searchApplications')}<input id="catalog-search" type="search" aria-describedby="catalog-discovery-detail" value={query} maxLength={80} onChange={event => setQuery(event.target.value)}/></label>
      <label className="catalog-action-label">{t('catalogRuleAction')}<select value={action} onChange={event => setAction(event.target.value as RuleAction)} disabled={!enabled}><option value="Block">{t('Block')}</option><option value="Allow">{t('Allow')}</option></select></label>
      {state.loading && <p className="start-note" role="status">{t('loadingApplications')}</p>}
      {state.error && <p className="notice" role="alert">{t('catalogError')}</p>}
      {state.page && state.page.items.length === 0 && <p className="start-note">{t('noApplications')}</p>}
      <ul className="catalog-apps">{state.page?.items.map(app => <li key={app.id}><ApplicationIcon icon={app.icon} name={app.name}/><div><strong>{app.name}</strong><small>{app.path}</small><span>{t('ExactPath')}</span></div><button type="button" disabled={!enabled || state.loading} aria-label={`${t('addCatalogApplication')}: ${app.name}`} onClick={() => void add(app, action)}>{t('addCatalogApplication')}</button></li>)}</ul>
      {state.page && state.page.total > 6 && <div className="catalog-pages"><button type="button" disabled={state.loading || state.page.offset === 0} onClick={() => void applications.search(query, Math.max(0, state.page!.offset - 6))}>{t('previous')}</button><span>{state.page.offset + 1}–{Math.min(state.page.offset + 6, state.page.total)} / {state.page.total}</span><button type="button" disabled={state.loading || state.page.offset + 6 >= state.page.total} onClick={() => void applications.search(query, state.page!.offset + 6)}>{t('next')}</button></div>}
    </div>
  </details>;
}
