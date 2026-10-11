import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { CheckIcon, MagnifyingGlassIcon, PlusIcon, ReloadIcon } from '@radix-ui/react-icons';
import { decodeIcon, type AppIcon, type ApplicationsClient, type CatalogApplication } from './native/applications';
import type { RuleAction } from './native/profiles';
import type es from './locales/es.json';
import { InfoTip } from './ZenControls';
type Copy = (key: keyof typeof es) => string;
export function ApplicationIcon({ icon, name }: { icon: AppIcon | null; name: string }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  useEffect(() => { const pixels = decodeIcon(icon); const context = canvas.current?.getContext('2d'); if (pixels && context) { const image = context.createImageData(32, 32); image.data.set(pixels); context.putImageData(image, 0, 0); } }, [icon]);
  return icon ? <canvas ref={canvas} className="catalog-icon" width={32} height={32} aria-hidden="true"/> : <span className="catalog-icon fallback-icon" aria-hidden="true">{name.slice(0, 1).toUpperCase()}</span>;
}
/**
 * Search the detected applications and add one as a rule. As a disclosure it stays closed (and silent) until opened;
 * `inline` shows the search straight away, for places where the picker is the main thing on screen.
 */
export function ApplicationsPicker({ applications, enabled, t, add, inline = false, isAdded }: { applications: ApplicationsClient; enabled: boolean; t: Copy; add(application: CatalogApplication, action: RuleAction): Promise<void>; inline?: boolean; isAdded?(application: CatalogApplication): boolean }) {
  const state = useSyncExternalStore(applications.subscribe, applications.getSnapshot);
  const [query, setQuery] = useState(''); const [action, setAction] = useState<RuleAction>('Block');
  const [isOpen, setIsOpen] = useState(false);
  const requestedQuery = useRef<string | null>(null);
  const active = inline || isOpen;
  useEffect(() => {
    if (!active || requestedQuery.current === query) return;
    const timer = setTimeout(() => { requestedQuery.current = query; void applications.search(query); }, 250);
    return () => clearTimeout(timer);
  }, [applications, active, query]);
  const content = <div className="catalog-disclosure-content">
    <div className="catalog-toolbar">
      <label className="catalog-search-label rule-input" htmlFor="catalog-search"><MagnifyingGlassIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('searchApplications')}</span><input id="catalog-search" type="search" aria-describedby="catalog-discovery-detail" placeholder={t('searchApplications')} value={query} maxLength={80} onChange={event => setQuery(event.target.value)}/>
        <InfoTip label={t('moreInfo')} text={t('catalogDiscoveryDetail')}/></label>
      <label className="catalog-action-label"><span className="sr-only">{t('catalogRuleAction')}</span><select value={action} onChange={event => setAction(event.target.value as RuleAction)} disabled={!enabled} aria-label={t('catalogRuleAction')}><option value="Block">{t('Block')}</option><option value="Allow">{t('Allow')}</option></select></label>
      <button type="button" className="icon-action" disabled={state.loading} title={t('reloadCatalog')} onClick={() => void applications.search(query, 0, true)}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('reloadCatalog')}</span></button>
    </div>
    <p id="catalog-discovery-detail" className="sr-only">{t('catalogDiscoveryDetail')}</p>
    {state.loading && <p className="start-note" role="status">{t('loadingApplications')}</p>}
    {state.error && <p className="notice" role="alert">{t('catalogError')}</p>}
    {state.page && state.page.items.length === 0 && <p className="start-note">{t('noApplications')}</p>}
    <ul className="catalog-apps">{state.page?.items.map(app => <li key={app.id}><ApplicationIcon icon={app.icon} name={app.name}/><div><strong>{app.name}</strong><small title={app.path}>{app.path}</small></div>{isAdded?.(app) ? <span className="catalog-added"><CheckIcon aria-hidden="true" focusable="false"/>{t('catalogAdded')}</span> : <button type="button" className="catalog-add" disabled={!enabled || state.loading} aria-label={`${t('addCatalogApplication')}: ${app.name}`} onClick={() => void add(app, action)}><PlusIcon aria-hidden="true" focusable="false"/><span aria-hidden="true">{t(action)}</span></button>}</li>)}</ul>
    {state.page && state.page.total > 6 && <div className="catalog-pages"><button type="button" disabled={state.loading || state.page.offset === 0} onClick={() => void applications.search(query, Math.max(0, state.page!.offset - 6))}>{t('previous')}</button><span>{state.page.offset + 1}–{Math.min(state.page.offset + 6, state.page.total)} / {state.page.total}</span><button type="button" disabled={state.loading || state.page.offset + 6 >= state.page.total} onClick={() => void applications.search(query, state.page!.offset + 6)}>{t('next')}</button></div>}
  </div>;
  if (inline) return <div className="applications-picker inline">{content}</div>;
  return <details className="applications-picker" open={isOpen}>
    <summary className="catalog-disclosure-summary" onClick={event => { event.preventDefault(); setIsOpen(open => !open); }}>
      <span className="catalog-disclosure-copy"><strong>{t('installedApplications')}</strong><small>{t('catalogDisclosureHint')}</small></span>
    </summary>
    {content}
  </details>;
}
