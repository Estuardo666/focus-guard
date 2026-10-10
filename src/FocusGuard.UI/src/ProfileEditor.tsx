import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { CheckIcon } from '@radix-ui/react-icons';
import type { ConnectionSnapshot } from './native/protocol';
import type { DashboardClient } from './native/dashboard';
import type { MatchKind, ProfilesClient, ProfileMethod, RuleAction } from './native/profiles';
import type es from './locales/es.json';
import { countLabel } from './locales/count-label';
import { ApplicationIcon, ApplicationsPicker } from './ApplicationsPicker';
import type { ApplicationsClient } from './native/applications';

type Copy = (key: keyof typeof es) => string;
const colors = ['#EFD9D5', '#E8BDCF', '#D6B7EB', '#B6BCEC', '#96CFE6', '#9CD9C8', '#B6D7A7', '#E4D59C', '#E4B697'];
export function ProfileEditor({ profiles, dashboard, applications, connection, language, t }: {
  profiles: ProfilesClient; dashboard: DashboardClient; applications?: ApplicationsClient; connection: ConnectionSnapshot; language: 'es' | 'en'; t: Copy;
}) {
  const state = useSyncExternalStore(profiles.subscribe, profiles.getSnapshot);
  const listing = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const [selectedId, setSelectedId] = useState('');
  const [name, setName] = useState(''); const [newName, setNewName] = useState('');
  const [appValue, setAppValue] = useState(''); const [siteValue, setSiteValue] = useState('');
  const [breakAppValue, setBreakAppValue] = useState(''); const [breakWebsiteValue, setBreakWebsiteValue] = useState('');
  const [breakMatchKind, setBreakMatchKind] = useState<MatchKind>('FileName');
  const [selectedDefaultPresetId, setSelectedDefaultPresetId] = useState('');
  const [appAction, setAppAction] = useState<RuleAction>('Block'); const [siteAction, setSiteAction] = useState<RuleAction>('Block');
  const [kind, setKind] = useState<MatchKind>('FileName'); const [deleteReview, setDeleteReview] = useState(false);
  const catalog = listing.catalog; const detail = state.detail?.id === selectedId ? state.detail : null;
  const busy = state.phase === 'loading' || state.phase === 'writing';
  const freshIdle = connection.phase === 'ready' && connection.status && ['Inactive', 'Expired', 'Stopped', 'Recovered', 'Failed'].includes(connection.status.sessionState);
  const allowed = !!freshIdle && !!catalog?.pilotEnabled && !busy && state.phase !== 'unknown' && !['ProfileConflict', 'ProfileMissing'].includes(state.error ?? '');
  const editable = allowed && !!detail?.editingEnabled;
  const breakSettingsEditable = allowed && !!detail?.cycleSettingsEditingEnabled;
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
  const errorKey = state.error === 'CatalogChanged' ? 'catalogChanged' : state.error === 'RuleAlreadyExists' ? 'ruleAlreadyExists' : state.error === 'ProfileConflict' ? 'profileConflict' : state.error === 'NameConflict' ? 'nameConflict' : state.error === 'LastProfile' ? 'lastProfile' : state.error === 'RuleLimitReached' ? 'breakRuleLimit' : state.error === 'PresetMissing' ? 'profileMissing' : state.error === 'InvalidInput' ? 'invalidRuleInput' : state.error === 'ProfileMissing' ? 'profileMissing' : state.phase === 'unknown' ? 'profileUnknown' : 'profileEditError';
  return <>
    {catalog && !catalog.pilotEnabled && <p className="profile-readonly-note" role="note">{t('editorPilotRequired')}</p>}
    <section className="profiles-layout" aria-label={t('profiles')}>
    <aside className="profiles-rail"><div className="profile-list-heading"><h2>{t('profiles')}</h2><button type="button" onClick={() => void reload()} disabled={busy}>{t('refreshProfiles')}</button></div>
      <div className="profile-choices">{catalog?.items.map(profile => <button type="button" key={profile.id} aria-pressed={profile.id === selectedId} disabled={state.phase === 'writing'} onClick={() => setSelectedId(profile.id)}><strong>{profile.name}</strong><span>{countLabel(profile.applicationRuleCount, t, 'singleApplicationRule', 'multipleApplicationRules')} · {countLabel(profile.websiteRuleCount, t, 'singleWebsiteRule', 'multipleWebsiteRules')}</span></button>)}</div>
      {catalog && catalog.total > 8 && <div className="catalog-pages"><button type="button" disabled={catalog.offset === 0 || busy || listing.catalogLoading} onClick={() => void dashboard.loadProfiles(Math.max(0, catalog.offset - 8))}>{t('previous')}</button><span>{catalog.offset + 1}–{Math.min(catalog.offset + 8, catalog.total)} / {catalog.total}</span><button type="button" disabled={catalog.offset + 8 >= catalog.total || busy || listing.catalogLoading} onClick={() => void dashboard.loadProfiles(catalog.offset + 8)}>{t('next')}</button></div>}
      <form className="new-profile" onSubmit={event => { event.preventDefault(); if (newName.trim()) void mutate('profile.create', { name: newName.trim(), accentHex: null }); }}><label htmlFor="new-profile-name">{t('newProfileName')}</label><input id="new-profile-name" value={newName} maxLength={80} onChange={event => setNewName(event.target.value)} disabled={!allowed} required/><button className="primary-action" type="submit" disabled={!allowed || !newName.trim()}>{t('createProfile')}</button></form>
    </aside>
    <div className="profile-workspace">
      {!freshIdle && <p className="notice">{t('profileReadOnlySession')}</p>}
      {state.error && <div className="notice" role="alert"><p>{t(errorKey)}</p><button type="button" onClick={() => void reload()} disabled={busy}>{t('reloadProfile')}</button></div>}
      {listing.catalogError && <p className="notice" role="alert">{t('profilesError')}</p>}
      {busy && <p role="status">{state.phase === 'writing' ? t('savingProfile') : t('profilesLoading')}</p>}
      {!detail && !busy && <p className="session-guidance">{t('selectProfile')}</p>}
      {detail && <>
        <details className="profile-identity-settings" key={detail.id}><summary className="profile-identity-summary"><span className="profile-monogram" style={{ borderColor: detail.accentHex ?? undefined }} aria-hidden="true">{detail.name.slice(0, 1).toUpperCase()}</span><span className="profile-settings-copy"><span className="profile-settings-name" role="heading" aria-level={2}>{detail.name}</span><small>{t('profileSettingsSummary')}</small></span></summary>
          <section className="profile-identity"><p className="profile-identity-detail">{detail.description ?? t('savedRulesDetail')}</p>
          <form className="rename-profile" onSubmit={event => { event.preventDefault(); void mutate('profile.rename', { name: name.trim() }); }}><label htmlFor="profile-name">{t('profileName')}</label><div><input id="profile-name" value={name} maxLength={80} onChange={event => setName(event.target.value)} disabled={!editable} required/><button type="submit" disabled={!editable || !name.trim() || name.trim() === detail.name}>{t('renameProfile')}</button></div></form>
          <fieldset className="profile-colors" disabled={!editable}><legend>{t('profileColor')}</legend>{colors.map((color, index) => { const selected = detail.accentHex?.toUpperCase() === color; return <button type="button" key={color} className="color-choice" aria-label={`${t('profileColor')} ${index + 1}`} aria-pressed={selected} onClick={() => void mutate('profile.accent', { accentHex: color })}><span className="profile-color-swatch" style={{ backgroundColor: color }} aria-hidden="true">{selected && <CheckIcon className="profile-color-check" aria-hidden="true" focusable="false"/>}</span></button>; })}<input type="color" aria-label={t('customColor')} value={detail.accentHex ?? '#9CD9C8'} onChange={event => void mutate('profile.accent', { accentHex: event.target.value })}/><button type="button" onClick={() => void mutate('profile.accent', { accentHex: null })}>{t('defaultColor')}</button></fieldset>
          <div className="profile-tools"><button type="button" disabled={!editable} onClick={() => void mutate('profile.duplicate', { language })}>{t('duplicateProfile')}</button><button className="danger-action" type="button" disabled={!editable || (catalog?.total ?? 0) <= 1} onClick={() => setDeleteReview(true)}>{t('deleteProfile')}</button></div>
          {deleteReview && <DeleteConfirmation name={detail.name} enabled={editable} t={t} cancel={() => setDeleteReview(false)} confirm={() => void mutate('profile.delete', { confirmed: true })}/>}
          </section>
        </details>
        <div className="rule-editors">
          <section className="rule-editor" aria-labelledby="app-rules-title">
            <h2 id="app-rules-title">{t('applicationsTitle')} <span>{detail.applicationTotal}</span></h2>
            <section className="saved-rule-section" aria-labelledby="saved-app-rules-title">
              <h3 id="saved-app-rules-title">{t('savedRulesTitle')}</h3>
              {detail.applicationTotal === 0
                ? <p className="rule-empty">{t('noApplicationRules')}</p>
                : <>
                  <ul className="saved-rules">{detail.applications.map(rule => <li key={rule.id}>{rule.presentation?.icon && <ApplicationIcon icon={rule.presentation.icon} name={rule.presentation.name}/>}<div><span className={`rule-action ${rule.action.toLowerCase()}`}>{t(rule.action)}</span><strong>{rule.presentation?.name ?? rule.value}</strong>{rule.presentation && rule.presentation.name !== rule.value && <small className="rule-target">{rule.value}</small>}{rule.presentation?.path && rule.presentation.path !== rule.value && <small className="rule-target">{rule.presentation.path}</small>}<small>{t(rule.matchKind)}</small>{rule.presentation && <small className={`rule-resolution ${rule.presentation.resolution.toLowerCase()}`}>{t(rule.presentation.resolution)}</small>}</div><button type="button" aria-label={`${t('removeRule')}: ${rule.value}`} disabled={!editable} onClick={() => void mutate('profile.application.remove', { ruleId: rule.id.replaceAll('-', '') })}>×</button></li>)}</ul>
                  <RulePages offset={detail.applicationOffset} total={detail.applicationTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, offset, detail.websiteOffset, detail.breakApplicationOffset, detail.breakWebsiteOffset)}/>
                </>}
            </section>
            {applications && <ApplicationsPicker applications={applications} enabled={editable} t={t} add={async (app, action) => { await mutate('profile.application.addFromCatalog', { action, catalogId: app.id }); }}/>}
            <details className="manual-application" open={!applications}><summary>{t('advancedApplicationRule')}</summary>
              <form onSubmit={event => { event.preventDefault(); if (appValue.trim()) void mutate('profile.application.add', { action: appAction, matchKind: kind, value: appValue.trim() }); }}>
                <label htmlFor="application-value">{t('applicationValue')}</label><input id="application-value" value={appValue} maxLength={1024} onChange={event => setAppValue(event.target.value)} placeholder={kind === 'FileName' ? 'app.exe' : kind === 'Sha256' ? 'SHA-256' : 'C:\\Apps\\app.exe'} disabled={!editable} required/>
                <div className="rule-add-options"><label>{t('ruleAction')}<select value={appAction} onChange={event => setAppAction(event.target.value as RuleAction)} disabled={!editable}><option value="Block">{t('Block')}</option><option value="Allow">{t('Allow')}</option></select></label><label>{t('matchKind')}<select value={kind} onChange={event => setKind(event.target.value as MatchKind)} disabled={!editable}>{(['FileName', 'ExactPath', 'Folder', 'Sha256'] as const).map(value => <option key={value} value={value}>{t(value)}</option>)}</select></label></div><button type="submit" disabled={!editable || !appValue.trim()}>{t('addApplicationRule')}</button>
              </form><p className="start-note">{t('manualApplicationsNote')}</p>
            </details>
          </section>
          <section className="rule-editor" aria-labelledby="site-rules-title">
            <h2 id="site-rules-title">{t('websitesTitle')} <span>{detail.websiteTotal}</span></h2>
            <section className="saved-rule-section" aria-labelledby="saved-site-rules-title">
              <h3 id="saved-site-rules-title">{t('savedRulesTitle')}</h3>
              {detail.websiteTotal === 0
                ? <p className="rule-empty">{t('noWebsiteRules')}</p>
                : <>
                  <ul className="saved-rules">{detail.websites.map(rule => <li key={rule.id}><div><span className={`rule-action ${rule.action.toLowerCase()}`}>{t(rule.action)}</span><strong>{rule.pattern}</strong></div><button type="button" aria-label={`${t('removeRule')}: ${rule.pattern}`} disabled={!editable} onClick={() => void mutate('profile.website.remove', { ruleId: rule.id.replaceAll('-', '') })}>×</button></li>)}</ul>
                  <RulePages offset={detail.websiteOffset} total={detail.websiteTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, offset, detail.breakApplicationOffset, detail.breakWebsiteOffset)}/>
                </>}
            </section>
            <form onSubmit={event => { event.preventDefault(); if (siteValue.trim()) void mutate('profile.website.add', { action: siteAction, pattern: siteValue.trim() }); }}><label htmlFor="website-pattern">{t('websitePattern')}</label><input id="website-pattern" value={siteValue} maxLength={1024} onChange={event => setSiteValue(event.target.value)} placeholder="*.example.com" disabled={!editable} required/><label className="site-action-select">{t('ruleAction')}<select value={siteAction} onChange={event => setSiteAction(event.target.value as RuleAction)} disabled={!editable}><option value="Block">{t('Block')}</option><option value="Allow">{t('Allow')}</option></select></label><button type="submit" disabled={!editable || !siteValue.trim()}>{t('addWebsiteRule')}</button></form>
            <p className="start-note">{t('websitePatternHelp')}</p>
          </section>
        </div>
        <details className="break-settings" aria-labelledby="break-settings-title" onToggle={event => { if (event.currentTarget.open && !listing.cycles && !listing.cyclesLoading) void dashboard.loadCycles(); }}>
          <summary><strong id="break-settings-title">{t('breakSettingsTitle')}</strong><span>{t('defaultCyclePreset')}: {detail.defaultCyclePresetName ?? t('chooseCycleEachTime')} · {t('breakApplicationsLabel')}: {detail.breakApplicationTotal} · {t('breakWebsitesLabel')}: {detail.breakWebsiteTotal}</span></summary>
          <p className="start-note">{t('breakSettingsDetail')}</p>
          <section className="profile-cycle-default" aria-label={t('defaultCyclePreset')}>
            <div className="profile-cycle-default-controls">
              <label htmlFor="profile-default-cycle-preset">{t('defaultCyclePreset')}</label>
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
          <h3 className="break-exception-heading">{t('breakRuleSummary')}</h3>
          <div className="break-rule-editors">
            <section aria-labelledby="break-applications-title">
              <h3 id="break-applications-title">{t('breakExceptionApps')}</h3>
              <form onSubmit={event => { event.preventDefault(); if (breakAppValue.trim()) void mutate('profile.break.application.add', { matchKind: breakMatchKind, value: breakAppValue.trim() }); }}>
                <label htmlFor="break-application-value">{t('applicationValue')}<input id="break-application-value" value={breakAppValue} maxLength={1024} onChange={event => setBreakAppValue(event.target.value)} placeholder={breakMatchKind === 'FileName' ? 'app.exe' : breakMatchKind === 'Sha256' ? 'SHA-256' : 'C:\\Apps\\app.exe'} disabled={!breakSettingsEditable} required/></label>
                <label htmlFor="break-application-kind">{t('matchKind')}<select id="break-application-kind" value={breakMatchKind} onChange={event => setBreakMatchKind(event.target.value as MatchKind)} disabled={!breakSettingsEditable}>{(['FileName', 'ExactPath', 'Folder', 'Sha256'] as const).map(value => <option key={value} value={value}>{t(value)}</option>)}</select></label>
                <button type="submit" disabled={!breakSettingsEditable || !breakAppValue.trim()}>{t('addBreakApplication')}</button>
              </form>
              <p className="start-note">{t('breakApplicationHelp')}</p>
              {detail.breakApplicationTotal === 0 && <p className="session-guidance">{t('noBreakExceptions')}</p>}
              <ul className="saved-rules">{detail.breakApplications.map(rule => <li key={rule.id}><div><span className="rule-action block">{t('Block')}</span><strong>{rule.value}</strong><small>{t(rule.matchKind)}</small></div><button type="button" aria-label={`${t('removeBreakException')}: ${rule.value}`} disabled={!breakSettingsEditable} onClick={() => void mutate('profile.break.application.remove', { ruleId: rule.id.replaceAll('-', '') })}>×</button></li>)}</ul>
              <RulePages offset={detail.breakApplicationOffset} total={detail.breakApplicationTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, detail.websiteOffset, offset, detail.breakWebsiteOffset)}/>
            </section>
            <section aria-labelledby="break-websites-title">
              <h3 id="break-websites-title">{t('breakExceptionSites')}</h3>
              <form className="break-website-form" onSubmit={event => { event.preventDefault(); if (breakWebsiteValue.trim()) void mutate('profile.break.website.add', { pattern: breakWebsiteValue.trim() }); }}>
                <label htmlFor="break-website-pattern">{t('websitePattern')}<input id="break-website-pattern" value={breakWebsiteValue} maxLength={1024} onChange={event => setBreakWebsiteValue(event.target.value)} placeholder="*.example.com" disabled={!breakSettingsEditable} required/></label>
                <button type="submit" disabled={!breakSettingsEditable || !breakWebsiteValue.trim()}>{t('addBreakWebsite')}</button>
              </form>
              <p className="start-note">{t('breakWebsiteHelp')}</p>
              {detail.breakWebsiteTotal === 0 && <p className="session-guidance">{t('noBreakExceptions')}</p>}
              <ul className="saved-rules">{detail.breakWebsites.map(rule => <li key={rule.id}><div><span className="rule-action block">{t('Block')}</span><strong>{rule.pattern}</strong></div><button type="button" aria-label={`${t('removeBreakException')}: ${rule.pattern}`} disabled={!breakSettingsEditable} onClick={() => void mutate('profile.break.website.remove', { ruleId: rule.id.replaceAll('-', '') })}>×</button></li>)}</ul>
              <RulePages offset={detail.breakWebsiteOffset} total={detail.breakWebsiteTotal} busy={busy} t={t} move={offset => void profiles.load(detail.id, detail.applicationOffset, detail.websiteOffset, detail.breakApplicationOffset, offset)}/>
            </section>
          </div>
          {!breakSettingsEditable && <p className="start-note">{t('cycleSettingsReadOnly')}</p>}
        </details>
      </>}
    </div>
    </section>
  </>;
}
function DeleteConfirmation({ name, enabled, t, cancel, confirm }: { name: string; enabled: boolean; t: Copy; cancel(): void; confirm(): void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const element = dialog.current; if (element && !element.open) element.showModal(); return () => element?.close(); }, []);
  return <dialog ref={dialog} className="delete-review" aria-labelledby="delete-title" aria-describedby="delete-consequences" onCancel={event => { event.preventDefault(); cancel(); }}><h3 id="delete-title">{t('deleteProfile')} · {name}</h3><p id="delete-consequences">{t('deleteConsequences')}</p><div className="review-actions"><button type="button" autoFocus onClick={cancel}>{t('cancel')}</button><button className="danger-action" type="button" disabled={!enabled} onClick={confirm}>{t('confirmDelete')}</button></div></dialog>;
}
function RulePages({ offset, total, busy, t, move }: { offset: number; total: number; busy: boolean; t: Copy; move(offset: number): void }) {
  if (total <= 6) return null;
  return <div className="catalog-pages"><button type="button" disabled={offset === 0 || busy} onClick={() => move(Math.max(0, offset - 6))}>{t('previous')}</button><span>{offset + 1}–{Math.min(offset + 6, total)} / {total}</span><button type="button" disabled={offset + 6 >= total || busy} onClick={() => move(offset + 6)}>{t('next')}</button></div>;
}
