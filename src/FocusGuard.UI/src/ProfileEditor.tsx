import { useEffect, useRef, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from 'react';
import { motion } from 'framer-motion';
import { CheckIcon, CopyIcon, Cross2Icon, DesktopIcon, GlobeIcon, MixerHorizontalIcon, PlusIcon, ReloadIcon, TrashIcon } from '@radix-ui/react-icons';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient } from './native/dashboard';
import type { MatchKind, ProfilesClient, ProfileMethod, RuleAction } from './native/profiles';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { ApplicationIcon, ApplicationsPicker } from './ApplicationsPicker';
import type { ApplicationsClient } from './native/applications';
import { CardSlider, exitDialog, InfoTip } from './ZenControls';

type Copy = (key: keyof typeof es) => string;
type RuleTab = 'sites' | 'apps' | 'breaks';
const colors = ['#EFD9D5', '#E8BDCF', '#D6B7EB', '#B6BCEC', '#96CFE6', '#9CD9C8', '#B6D7A7', '#E4D59C', '#E4B697'];
const spring = { type: 'spring', duration: 0.4, bounce: 0 } as const;
const accentStyle = (hex: string | null | undefined) => hex ? { '--profile-accent': hex } as CSSProperties : undefined;

/**
 * Profiles read like Opal's blocks: pick a profile card, then edit one kind of rule at a time (sites, apps, breaks).
 * Name, colour, duplicate and delete live behind "Personalizar" so the rules stay the first thing you see.
 */
export function ProfileEditor({ profiles, dashboard, applications, connection, language, t }: {
  profiles: ProfilesClient; dashboard: DashboardClient; applications?: ApplicationsClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy;
}) {
  const state = useSyncExternalStore(profiles.subscribe, profiles.getSnapshot);
  const listing = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [selectedId, setSelectedId] = useState('');
  const [tab, setTab] = useState<RuleTab>('sites');
  const [name, setName] = useState(''); const [newName, setNewName] = useState('');
  const [appValue, setAppValue] = useState(''); const [siteValue, setSiteValue] = useState('');
  const [breakAppValue, setBreakAppValue] = useState(''); const [breakWebsiteValue, setBreakWebsiteValue] = useState('');
  const [breakMatchKind, setBreakMatchKind] = useState<MatchKind>('FileName');
  const [selectedDefaultPresetId, setSelectedDefaultPresetId] = useState('');
  const [appAction, setAppAction] = useState<RuleAction>('Block'); const [siteAction, setSiteAction] = useState<RuleAction>('Block');
  const [kind, setKind] = useState<MatchKind>('FileName'); const [deleteReview, setDeleteReview] = useState(false);
  const catalog = listing.catalog; const current = state.detail?.id === selectedId ? state.detail : null;
  // While the next profile loads, keep the previous one on screen (dimmed) so the page neither flickers nor jumps.
  const lastShown = useRef(current); if (current) lastShown.current = current;
  const detail = current ?? (selectedId && !state.error ? lastShown.current : null);
  const pending = !current && !!detail;
  const busy = state.phase === 'loading' || state.phase === 'writing';
  const freshIdle = connection.phase === 'ready' && connection.status && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(connection.status.sessionState);
  const allowed = !!freshIdle && !!catalog?.pilotEnabled && !busy && state.phase !== 'unknown' && !['ProfileConflict', 'ProfileMissing'].includes(state.error ?? '');
  const editable = allowed && !!current?.editingEnabled;
  const breakSettingsEditable = allowed && !!current?.cycleSettingsEditingEnabled;
  useEffect(() => { if (!selectedId) setSelectedId(catalog?.items[0]?.id ?? ''); }, [catalog, selectedId]);
  useEffect(() => { setDeleteReview(false); profiles.clear(); if (selectedId) void profiles.load(selectedId); }, [profiles, selectedId]);
  useEffect(() => { if (detail) setName(detail.name); }, [detail?.id, detail?.name]);
  useEffect(() => { setSelectedDefaultPresetId(detail?.defaultCyclePresetId ?? ''); }, [detail?.id, detail?.defaultCyclePresetId]);
  useEffect(() => { if (!freshIdle) setDeleteReview(false); }, [freshIdle]);
  const reload = async () => {
    await dashboard.loadProfiles(catalog?.offset ?? 0);
    if (dashboard.getSnapshot().catalogError || dashboard.getSnapshot().catalogLoading) return;
    if (selectedId) await profiles.load(selectedId);
    else profiles.acknowledgeReload();
  };
  const mutate = async (method: ProfileMethod, values: Record<string, unknown>) => {
    const breakMutation = method.startsWith('profile.break.') || method === 'profile.cycle.defaultPreset.set';
    if (!allowed || (method !== 'profile.create' && (breakMutation ? !breakSettingsEditable : !editable))) return;
    setDeleteReview(false); dashboard.cancelReview();
    const result = await profiles.mutate(method, values);
    if (!result) return;
    setAppValue(''); setSiteValue(''); setNewName(''); setBreakAppValue(''); setBreakWebsiteValue('');
    await dashboard.loadProfiles(catalog?.offset ?? 0);
    if (result.profileId) { setSelectedId(result.profileId); await profiles.load(result.profileId); }
    else { profiles.clear(); setSelectedId(''); }
  };
  const openTab = (next: RuleTab) => {
    setTab(next);
    if (next === 'breaks' && !listing.cycles && !listing.cyclesLoading) void dashboard.loadCycles();
  };
  const errorKey = state.error === 'CatalogChanged' ? 'catalogChanged' : state.error === 'RuleAlreadyExists' ? 'ruleAlreadyExists' : state.error === 'ProfileConflict' ? 'profileConflict' : state.error === 'NameConflict' ? 'nameConflict' : state.error === 'LastProfile' ? 'lastProfile' : state.error === 'RuleLimitReached' ? 'breakRuleLimit' : state.error === 'PresetMissing' ? 'profileMissing' : state.error === 'InvalidInput' ? 'invalidRuleInput' : state.error === 'ProfileMissing' ? 'profileMissing' : state.phase === 'unknown' ? 'profileUnknown' : 'profileEditError';
  const summary = catalog?.items.find(profile => profile.id === detail?.id);
  const blockedLine = summary ? `${countLabel(summary.blockedWebsiteCount, t, 'singleBlockedWebsite', 'multipleBlockedWebsites')} · ${countLabel(summary.blockedApplicationCount, t, 'singleBlockedApplication', 'multipleBlockedApplications')}` : null;
  const tabs: readonly { key: RuleTab; id: string; label: string; count: number; Icon: typeof GlobeIcon }[] = detail ? [
    { key: 'sites', id: 'site-rules-title', label: t('websitesTitle'), count: detail.websiteTotal, Icon: GlobeIcon },
    { key: 'apps', id: 'app-rules-title', label: t('applicationsTitle'), count: detail.applicationTotal, Icon: DesktopIcon },
    { key: 'breaks', id: 'break-settings-title', label: t('breaksTab'), count: detail.breakApplicationTotal + detail.breakWebsiteTotal, Icon: MixerHorizontalIcon },
  ] : [];
  return <>
    {catalog && !catalog.pilotEnabled && <p className="profile-readonly-note" role="note">{t('editorPilotRequired')}</p>}
    <section className="profiles-studio" aria-label={t('profiles')}>
      <div className="profiles-rail profile-gallery">
        <div className="profile-gallery-heading"><h2 id="profiles-gallery-title">{t('profiles')}</h2>
          <button type="button" className="icon-action" onClick={() => void reload()} disabled={busy} title={t('refreshProfiles')}><ReloadIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('refreshProfiles')}</span></button></div>
        <CardSlider className="profile-cards" groupRole="group" labelledBy="profiles-gallery-title" previous={t('previous')} next={t('next')} goTo={index => t('goToCard').replace('{index}', String(index))}>
          {catalog?.items.map(profile => <button type="button" key={profile.id} className="profile-card" aria-pressed={profile.id === selectedId} disabled={state.phase === 'writing'} onClick={() => setSelectedId(profile.id)} style={accentStyle(profile.accentHex)}>
            <span className="profile-card-ring" aria-hidden="true"/>
            <span className="choice-card-icon" aria-hidden="true">{profile.name.slice(0, 1).toUpperCase()}</span>
            <span className="profile-card-name">{profile.name}</span>
            <span className="profile-card-meta"><span title={countLabel(profile.websiteRuleCount, t, 'singleWebsiteRule', 'multipleWebsiteRules')}><GlobeIcon aria-hidden="true" focusable="false"/>{profile.websiteRuleCount}<span className="sr-only"> {t('websitesTitle')}</span></span><span title={countLabel(profile.applicationRuleCount, t, 'singleApplicationRule', 'multipleApplicationRules')}><DesktopIcon aria-hidden="true" focusable="false"/>{profile.applicationRuleCount}<span className="sr-only"> {t('applicationsTitle')}</span></span></span>
          </button>)}
          <form className="new-profile profile-card new" onSubmit={event => { event.preventDefault(); if (newName.trim()) void mutate('profile.create', { name: newName.trim(), accentHex: null }); }}>
            <span className="choice-card-icon new-icon" aria-hidden="true"><PlusIcon/></span>
            <label className="sr-only" htmlFor="new-profile-name">{t('newProfileName')}</label>
            <input id="new-profile-name" value={newName} maxLength={80} placeholder={t('newProfile')} onChange={event => setNewName(event.target.value)} disabled={!allowed} required/>
            <button className="primary-action new-profile-submit" type="submit" disabled={!allowed || !newName.trim()} title={t('createProfile')}><CheckIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('createProfile')}</span></button>
          </form>
        </CardSlider>
        {catalog && catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={catalog.offset === 0 || busy || listing.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, catalog.offset - 8))}>{t('previous')}</button><span>{catalog.offset + 1}–{Math.min(catalog.offset + 8, catalog.total)} / {catalog.total}</span><button type="button" disabled={catalog.offset + 8 >= catalog.total || busy || listing.catalogLoading} onClick={() => void dashboard.loadProfiles(catalog.offset + 8)}>{t('next')}</button></div>}
      </div>
      <div className="profile-workspace" data-pending={pending}>
        {!freshIdle && <p className="notice">{t('profileReadOnlySession')}</p>}
        {state.error && <div className="notice" role="alert"><p>{t(errorKey)}</p><button type="button" onClick={() => void reload()} disabled={busy}>{t('reloadProfile')}</button></div>}
        {listing.catalogError && <p className="notice" role="alert">{t('profilesError')}</p>}
        {busy && <p className={`profile-busy${detail ? ' sr-only' : ''}`} role="status">{state.phase === 'writing' ? t('savingProfile') : t('profilesLoading')}</p>}
        {!detail && !busy && <p className="session-guidance">{t('selectProfile')}</p>}
        {detail && <>
          <details className="profile-identity-settings profile-hero" key={detail.id} style={accentStyle(detail.accentHex)}>
            <summary className="profile-identity-summary">
              <span className="choice-card-icon profile-hero-orb" aria-hidden="true">{detail.name.slice(0, 1).toUpperCase()}</span>
              <span className="profile-settings-copy"><span className="profile-settings-name" role="heading" aria-level={2}>{detail.name}</span>{blockedLine && <span className="profile-blocked-line">{blockedLine}</span>}{detail.description && <small className="profile-description">{detail.description}</small>}</span>
              <span className="profile-customize"><MixerHorizontalIcon aria-hidden="true" focusable="false"/>{t('customizeProfile')}</span>
            </summary>
            <section className="profile-identity">
              <form className="rename-profile" onSubmit={event => { event.preventDefault(); void mutate('profile.rename', { name: name.trim() }); }}><label htmlFor="profile-name">{t('profileName')}</label><div><input id="profile-name" value={name} maxLength={80} onChange={event => setName(event.target.value)} disabled={!editable} required/><button type="submit" disabled={!editable || !name.trim() || name.trim() === detail.name}>{t('renameProfile')}</button></div></form>
              <fieldset className="profile-colors" disabled={!editable}><legend>{t('profileColor')}</legend>{colors.map((color, index) => { const selected = detail.accentHex?.toUpperCase() === color; return <button type="button" key={color} className="color-choice" aria-label={`${t('profileColor')} ${index + 1}`} aria-pressed={selected} onClick={() => void mutate('profile.accent', { accentHex: color })}><span className="profile-color-swatch" style={{ backgroundColor: color }} aria-hidden="true">{selected && <CheckIcon className="profile-color-check" aria-hidden="true" focusable="false"/>}</span></button>; })}<input type="color" aria-label={t('customColor')} value={detail.accentHex ?? '#9CD9C8'} onChange={event => void mutate('profile.accent', { accentHex: event.target.value })}/><button type="button" className="ghost-action" onClick={() => void mutate('profile.accent', { accentHex: null })}>{t('defaultColor')}</button></fieldset>
              <div className="profile-tools"><button type="button" disabled={!editable} onClick={() => void mutate('profile.duplicate', { language })}><CopyIcon aria-hidden="true" focusable="false"/>{t('duplicateProfile')}</button><button className="danger-action" type="button" disabled={!editable || (catalog?.total ?? 0) <= 1} onClick={() => setDeleteReview(true)}><TrashIcon aria-hidden="true" focusable="false"/>{t('deleteProfile')}</button></div>
              {deleteReview && <DeleteConfirmation name={detail.name} enabled={editable} t={t} cancel={() => setDeleteReview(false)} confirm={() => void mutate('profile.delete', { confirmed: true })}/>}
            </section>
          </details>
          <div className="rule-editors" key={`rules-${detail.id}`}>
            <div className="segmented rule-tabs" role="tablist" aria-label={t('profileRulesLabel')}>
              {tabs.map(entry => <button key={entry.key} type="button" role="tab" id={entry.id} aria-selected={tab === entry.key} aria-controls={`${entry.id}-panel`} tabIndex={tab === entry.key ? 0 : -1} data-checked={tab === entry.key}
                onClick={() => openTab(entry.key)}
                onKeyDown={event => { if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return; event.preventDefault(); const index = tabs.findIndex(candidate => candidate.key === tab); const next = tabs[(index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length]!; openTab(next.key); document.getElementById(next.id)?.focus(); }}>
                {tab === entry.key && <motion.span layoutId="rule-tab-pill" className="segment-pill" aria-hidden="true" transition={spring}/>}
                <span className="segment-label"><entry.Icon aria-hidden="true" focusable="false"/>{entry.label}<span className="rule-tab-count">{entry.count}</span></span>
              </button>)}
            </div>
            <section className="rule-editor" role="tabpanel" id="site-rules-title-panel" aria-labelledby="site-rules-title" hidden={tab !== 'sites'}>
              <form className="rule-quick-add" onSubmit={event => { event.preventDefault(); if (siteValue.trim()) void mutate('profile.website.add', { action: siteAction, pattern: siteValue.trim() }); }}>
                <label className="sr-only" htmlFor="website-pattern">{t('websitePattern')}</label>
                <span className="rule-input"><GlobeIcon aria-hidden="true" focusable="false"/><input id="website-pattern" value={siteValue} maxLength={1024} onChange={event => setSiteValue(event.target.value)} placeholder="youtube.com" disabled={!editable} required/><InfoTip label={t('moreInfo')} text={t('websitePatternHelp')}/></span>
                <ActionSegment name="site-action" value={siteAction} onChange={setSiteAction} disabled={!editable} t={t}/>
                <button type="submit" className="primary-action" disabled={!editable || !siteValue.trim()}><PlusIcon aria-hidden="true" focusable="false"/>{t('addWebsiteRule')}</button>
              </form>
              <section className="saved-rule-section" aria-labelledby="saved-site-rules-title">
                <h3 id="saved-site-rules-title" className="sr-only">{t('savedRulesTitle')}</h3>
                {detail.websiteTotal === 0
                  ? <p className="rule-empty"><GlobeIcon aria-hidden="true" focusable="false"/>{t('noWebsiteRules')}</p>
                  : <>
                    <RuleGroups t={t} rules={detail.websites} render={rule => <li key={rule.id} className={`rule-chip ${rule.action.toLowerCase()}`}><span className="rule-favicon" aria-hidden="true">{rule.pattern.replace(/^\*\./, '').slice(0, 1).toUpperCase()}</span><div><span className={`rule-action ${rule.action.toLowerCase()}`}>{t(rule.action)}</span><strong>{rule.pattern}</strong></div><button type="button" aria-label={`${t('removeRule')}: ${rule.pattern}`} disabled={!editable} onClick={() => void mutate('profile.website.remove', { ruleId: rule.id.replaceAll('-', '') })}><Cross2Icon aria-hidden="true" focusable="false"/></button></li>}/>
                    <RulePages offset={detail.websiteOffset} total={detail.websiteTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, offset, detail.breakApplicationOffset, detail.breakWebsiteOffset)}/>
                  </>}
              </section>
            </section>
            <section className="rule-editor" role="tabpanel" id="app-rules-title-panel" aria-labelledby="app-rules-title" hidden={tab !== 'apps'}>
              {applications && tab === 'apps' && <ApplicationsPicker applications={applications} enabled={editable} inline isAdded={app => detail.applications.some(rule => { const value = rule.value.toLowerCase(); return value === app.path.toLowerCase() || value === app.path.replace(/^.*[\\/]/, '').toLowerCase(); })} t={t} add={async (app, action) => { await mutate('profile.application.addFromCatalog', { action, catalogId: app.id }); }}/>}
              <section className="saved-rule-section" aria-labelledby="saved-app-rules-title">
                <h3 id="saved-app-rules-title" className="sr-only">{t('savedRulesTitle')}</h3>
                {detail.applicationTotal === 0
                  ? <p className="rule-empty"><DesktopIcon aria-hidden="true" focusable="false"/>{t('noApplicationRules')}</p>
                  : <>
                    <RuleGroups t={t} rules={detail.applications} render={rule => <li key={rule.id} className={`rule-chip ${rule.action.toLowerCase()}`}>{rule.presentation?.icon ? <ApplicationIcon icon={rule.presentation.icon} name={rule.presentation.name}/> : <span className="rule-favicon" aria-hidden="true">{(rule.presentation?.name ?? rule.value).replace(/^.*[\\/]/, '').slice(0, 1).toUpperCase()}</span>}<div><span className={`rule-action ${rule.action.toLowerCase()}`}>{t(rule.action)}</span><strong>{rule.presentation?.name ?? rule.value}</strong>{rule.presentation && rule.presentation.name !== rule.value && <small className="rule-target">{rule.value}</small>}{rule.presentation?.path && rule.presentation.path !== rule.value && <small className="rule-target">{rule.presentation.path}</small>}{rule.matchKind !== 'FileName' && <small>{t(rule.matchKind)}</small>}{rule.presentation && rule.presentation.resolution !== 'ExactMatch' && <small className={`rule-resolution ${rule.presentation.resolution.toLowerCase()}`}>{t(rule.presentation.resolution)}</small>}</div><button type="button" aria-label={`${t('removeRule')}: ${rule.value}`} disabled={!editable} onClick={() => void mutate('profile.application.remove', { ruleId: rule.id.replaceAll('-', '') })}><Cross2Icon aria-hidden="true" focusable="false"/></button></li>}/>
                    <RulePages offset={detail.applicationOffset} total={detail.applicationTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, offset, detail.websiteOffset, detail.breakApplicationOffset, detail.breakWebsiteOffset)}/>
                  </>}
              </section>
              <details className="manual-application" open={!applications}><summary>{t('advancedApplicationRule')}</summary>
                <form onSubmit={event => { event.preventDefault(); if (appValue.trim()) void mutate('profile.application.add', { action: appAction, matchKind: kind, value: appValue.trim() }); }}>
                  <label htmlFor="application-value">{t('applicationValue')}</label><input id="application-value" value={appValue} maxLength={1024} onChange={event => setAppValue(event.target.value)} placeholder={kind === 'FileName' ? 'app.exe' : kind === 'Sha256' ? 'SHA-256' : 'C:\\Apps\\app.exe'} disabled={!editable} required/>
                  <div className="rule-add-options"><label>{t('ruleAction')}<select value={appAction} onChange={event => setAppAction(event.target.value as RuleAction)} disabled={!editable}><option value="Block">{t('Block')}</option><option value="Allow">{t('Allow')}</option></select></label><label>{t('matchKind')}<select value={kind} onChange={event => setKind(event.target.value as MatchKind)} disabled={!editable}>{(['FileName', 'ExactPath', 'Folder', 'Sha256'] as const).map(value => <option key={value} value={value}>{t(value)}</option>)}</select></label></div><button type="submit" disabled={!editable || !appValue.trim()}>{t('addApplicationRule')}</button>
                </form><p className="start-note">{t('manualApplicationsNote')}</p>
              </details>
            </section>
            <section className="rule-editor break-settings" role="tabpanel" id="break-settings-title-panel" aria-labelledby="break-settings-title" hidden={tab !== 'breaks'}>
              <div className="break-intro"><p>{t('breakRuleSummary')}</p><InfoTip label={t('moreInfo')} text={t('breakSettingsDetail')}/></div>
              <div className="break-rule-editors">
                <section aria-labelledby="break-applications-title">
                  <div className="break-col-head"><h3 id="break-applications-title"><DesktopIcon aria-hidden="true" focusable="false"/>{t('applicationsTitle')}</h3><InfoTip label={t('moreInfo')} text={t('breakApplicationHelp')}/></div>
                  <form className="break-add" onSubmit={event => { event.preventDefault(); if (breakAppValue.trim()) void mutate('profile.break.application.add', { matchKind: breakMatchKind, value: breakAppValue.trim() }); }}>
                    <label htmlFor="break-application-value"><span className="sr-only">{t('applicationValue')}</span><input id="break-application-value" value={breakAppValue} maxLength={1024} onChange={event => setBreakAppValue(event.target.value)} placeholder={breakMatchKind === 'FileName' ? 'app.exe' : breakMatchKind === 'Sha256' ? 'SHA-256' : 'C:\\Apps\\app.exe'} disabled={!breakSettingsEditable} required/></label>
                    <label htmlFor="break-application-kind"><span className="sr-only">{t('matchKind')}</span><select id="break-application-kind" value={breakMatchKind} onChange={event => setBreakMatchKind(event.target.value as MatchKind)} disabled={!breakSettingsEditable}>{(['FileName', 'ExactPath', 'Folder', 'Sha256'] as const).map(value => <option key={value} value={value}>{t(value)}</option>)}</select></label>
                    <button type="submit" disabled={!breakSettingsEditable || !breakAppValue.trim()} title={t('addBreakApplication')}><PlusIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('addBreakApplication')}</span></button>
                  </form>
                  {detail.breakApplicationTotal === 0 ? <p className="rule-empty">{t('breakEmptyShort')}</p>
                    : <ul className="saved-rules">{detail.breakApplications.map(rule => <li key={rule.id} className="rule-chip block"><span className="rule-favicon" aria-hidden="true">{rule.value.replace(/^.*[\\/]/, '').slice(0, 1).toUpperCase()}</span><div><strong>{rule.value}</strong>{rule.matchKind !== 'FileName' && <small>{t(rule.matchKind)}</small>}</div><button type="button" aria-label={`${t('removeBreakException')}: ${rule.value}`} disabled={!breakSettingsEditable} onClick={() => void mutate('profile.break.application.remove', { ruleId: rule.id.replaceAll('-', '') })}><Cross2Icon aria-hidden="true" focusable="false"/></button></li>)}</ul>}
                  <RulePages offset={detail.breakApplicationOffset} total={detail.breakApplicationTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, detail.websiteOffset, offset, detail.breakWebsiteOffset)}/>
                </section>
                <section aria-labelledby="break-websites-title">
                  <div className="break-col-head"><h3 id="break-websites-title"><GlobeIcon aria-hidden="true" focusable="false"/>{t('websitesTitle')}</h3><InfoTip label={t('moreInfo')} text={t('breakWebsiteHelp')}/></div>
                  <form className="break-add break-website-form" onSubmit={event => { event.preventDefault(); if (breakWebsiteValue.trim()) void mutate('profile.break.website.add', { pattern: breakWebsiteValue.trim() }); }}>
                    <label htmlFor="break-website-pattern"><span className="sr-only">{t('websitePattern')}</span><input id="break-website-pattern" value={breakWebsiteValue} maxLength={1024} onChange={event => setBreakWebsiteValue(event.target.value)} placeholder="*.example.com" disabled={!breakSettingsEditable} required/></label>
                    <button type="submit" disabled={!breakSettingsEditable || !breakWebsiteValue.trim()} title={t('addBreakWebsite')}><PlusIcon aria-hidden="true" focusable="false"/><span className="sr-only">{t('addBreakWebsite')}</span></button>
                  </form>
                  {detail.breakWebsiteTotal === 0 ? <p className="rule-empty">{t('breakEmptyShort')}</p>
                    : <ul className="saved-rules">{detail.breakWebsites.map(rule => <li key={rule.id} className="rule-chip block"><span className="rule-favicon" aria-hidden="true">{rule.pattern.replace(/^\*\./, '').slice(0, 1).toUpperCase()}</span><div><strong>{rule.pattern}</strong></div><button type="button" aria-label={`${t('removeBreakException')}: ${rule.pattern}`} disabled={!breakSettingsEditable} onClick={() => void mutate('profile.break.website.remove', { ruleId: rule.id.replaceAll('-', '') })}><Cross2Icon aria-hidden="true" focusable="false"/></button></li>)}</ul>}
                  <RulePages offset={detail.breakWebsiteOffset} total={detail.breakWebsiteTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, detail.websiteOffset, detail.breakApplicationOffset, offset)}/>
                </section>
              </div>
              <section className="profile-cycle-default" aria-label={t('defaultCyclePreset')}>
                <label htmlFor="profile-default-cycle-preset">{t('defaultCyclePreset')}</label>
                <div className="profile-cycle-default-controls">
                  <select id="profile-default-cycle-preset" value={selectedDefaultPresetId} onChange={event => setSelectedDefaultPresetId(event.target.value)} disabled={!breakSettingsEditable || listing.cyclesLoading}>
                    <option value="">{t('chooseCycleEachTime')}</option>
                    {detail.defaultCyclePresetId && !listing.cycles?.items.some(preset => preset.id.toLowerCase() === detail.defaultCyclePresetId!.toLowerCase()) && <option value={detail.defaultCyclePresetId}>{detail.defaultCyclePresetName ?? t('currentDefaultPreset')}</option>}
                    {listing.cycles?.items.map(preset => <option key={preset.id} value={preset.id}>{preset.name}{preset.isBuiltIn ? ` · ${t('builtInPreset')}` : ''}</option>)}
                  </select>
                  <button type="button" disabled={!breakSettingsEditable || listing.cyclesLoading || selectedDefaultPresetId === (detail.defaultCyclePresetId ?? '')} onClick={() => void mutate('profile.cycle.defaultPreset.set', { presetId: selectedDefaultPresetId ? selectedDefaultPresetId.replaceAll('-', '') : null })}>{t('saveDefaultCyclePreset')}</button>
                </div>
                {listing.cyclesLoading && <p role="status">{t('cyclesLoading')}</p>}
                {listing.cyclesError && <p className="notice" role="alert">{t('cyclesError')} <button type="button" disabled={listing.cyclesLoading} onClick={() => void dashboard.loadCycles(listing.cycles?.offset ?? 0)}>{t('refresh')}</button></p>}
                {listing.cycles && listing.cycles.total > 8 && <div className="catalog-pages"><button type="button" disabled={listing.cycles.offset === 0 || busy || listing.cyclesLoading} onClick={() => void dashboard.loadCycles(Math.max(0, listing.cycles!.offset - 8))}>{t('previous')}</button><span>{listing.cycles.offset + 1}–{Math.min(listing.cycles.offset + 8, listing.cycles.total)} / {listing.cycles.total}</span><button type="button" disabled={listing.cycles.offset + 8 >= listing.cycles.total || busy || listing.cyclesLoading} onClick={() => void dashboard.loadCycles(listing.cycles!.offset + 8)}>{t('next')}</button></div>}
              </section>
              {!breakSettingsEditable && <p className="start-note">{t('cycleSettingsReadOnly')}</p>}
            </section>
          </div>
        </>}
      </div>
    </section>
  </>;
}
function ActionSegment({ name, value, onChange, disabled, t }: { name: string; value: RuleAction; onChange(value: RuleAction): void; disabled: boolean; t: Copy }) {
  return <div className="segmented" role="radiogroup" aria-label={t('ruleAction')}>{(['Block', 'Allow'] as const).map(option => <label key={option} data-checked={value === option}><input type="radio" name={name} value={option} checked={value === option} disabled={disabled} onChange={() => onChange(option)}/>{value === option && <motion.span layoutId={`${name}-pill`} className="segment-pill" aria-hidden="true" transition={spring}/>}<span className="segment-label">{t(option)}</span></label>)}</div>;
}
/** Blocked rules first and always-allowed rules as their own group, so each rule's effect reads at a glance. */
function RuleGroups<Rule extends { id: string; action: RuleAction }>({ rules, render, t }: { rules: readonly Rule[]; render(rule: Rule): ReactNode; t: Copy }) {
  const blocked = rules.filter(rule => rule.action === 'Block');
  const allowed = rules.filter(rule => rule.action === 'Allow');
  return <div className="rule-groups">
    {blocked.length > 0 && <div className="rule-group"><h4>{t('blockedGroup')}</h4><ul className="saved-rules">{blocked.map(render)}</ul></div>}
    {allowed.length > 0 && <div className="rule-group allowed"><h4>{t('allowedGroup')}</h4><ul className="saved-rules">{allowed.map(render)}</ul></div>}
  </div>;
}
function DeleteConfirmation({ name, enabled, t, cancel, confirm }: { name: string; enabled: boolean; t: Copy; cancel(): void; confirm(): void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const element = dialog.current; if (element && !element.open) element.showModal(); return () => element?.close(); }, []);
  const leave = () => exitDialog(dialog.current, cancel);
  return <dialog ref={dialog} className="delete-review" aria-labelledby="delete-title" aria-describedby="delete-consequences" onCancel={event => { event.preventDefault(); leave(); }}><h3 id="delete-title">{t('deleteProfile')} · {name}</h3><p id="delete-consequences">{t('deleteConsequences')}</p><div className="review-actions"><button type="button" autoFocus onClick={leave}>{t('cancel')}</button><button className="danger-action" type="button" disabled={!enabled} onClick={() => exitDialog(dialog.current, confirm)}>{t('confirmDelete')}</button></div></dialog>;
}
function RulePages({ offset, total, busy, t, move }: { offset: number; total: number; busy: boolean; t: Copy; move(offset: number): void }) {
  if (total <= 6) return null;
  return <div className="catalog-pages"><button type="button" disabled={offset === 0 || busy} onClick={() => move(Math.max(0, offset - 6))}>{t('previous')}</button><span>{offset + 1}–{Math.min(offset + 6, total)} / {total}</span><button type="button" disabled={offset + 6 >= total || busy} onClick={() => move(offset + 6)}>{t('next')}</button></div>;
}
