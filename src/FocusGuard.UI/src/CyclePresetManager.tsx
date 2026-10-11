import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import { isCycleConfiguration, type CycleConfiguration, type CyclePreset } from './native/cycles';
import type es from './locales/es.json';
import { exitDialog } from './ZenControls';

type Copy = (key: keyof typeof es) => string;
type Draft = { id: string | null; revision: string | null; name: string; config: CycleConfiguration };
const defaultConfig: CycleConfiguration = { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4, terminationMode: 'CycleCount', cycleCount: 4, totalDurationSeconds: null };
const configDraft = (preset: CyclePreset): Draft => ({ id: preset.id, revision: preset.revision ?? null, name: preset.name, config: { ...preset.configuration } });

export function CyclePresetManager({ dashboard, state, allowed, t, onSelected }: { dashboard: DashboardClient; state: DashboardSnapshot; allowed: boolean; t: Copy; onSelected(id: string): void }) {
  const native = useSyncExternalStore(dashboard.subscribe, dashboard.getSnapshot);
  const dialog = useRef<HTMLDialogElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleteReview, setDeleteReview] = useState(false);
  const [saveReview, setSaveReview] = useState(false);
  const [savedNotice, setSavedNotice] = useState(false);
  const [durationField, setDurationField] = useState<'count' | 'total'>('count');
  const presets = state.cycles?.items ?? [];
  const selected = presets.find(item => item.id === selectedId) ?? null;
  const editable = allowed && state.cycles?.editingEnabled === true && native.presetPhase !== 'unknown' && native.presetPhase !== 'writing';
  const busy = native.presetPhase === 'writing' || state.cyclesLoading;
  useEffect(() => { if (!selectedId && presets[0]) setSelectedId(presets[0].id); }, [selectedId, presets]);
  useEffect(() => { const element = dialog.current; if (open && element && !element.open) element.showModal(); if (!open && element?.open) element.close(); }, [open]);
  useEffect(() => { if (!open && trigger.current?.isConnected) trigger.current.focus(); }, [open]);
  const mutateSave = async () => {
    if (!draft || !editable || !draft.name.trim()) return;
    if (!isCycleConfiguration(draft.config)) return;
    if (!saveReview) { setSaveReview(true); return; }
    const result = await dashboard.saveCyclePreset(draft.id ?? crypto.randomUUID(), draft.revision, draft.name.trim(), draft.config);
    if (!result) {
      const error = dashboard.getSnapshot().presetError;
      setSaveReview(false);
      if (error === 'CyclePresetConflict') { await dashboard.loadCycles(state.cycles?.offset ?? 0); setDraft(null); }
      return;
    }
    await dashboard.loadCycles(state.cycles?.offset ?? 0);
    const id = result.id;
    if (id && dashboard.getSnapshot().cycles?.items.some(preset => preset.id === id)) { setSelectedId(id); onSelected(id); setDraft(null); }
    else { setDraft(null); setSavedNotice(true); }
    setSaveReview(false);
  };
  const mutateDelete = async () => {
    if (!selected || selected.isBuiltIn || !selected.revision || !editable) return;
    const result = await dashboard.deleteCyclePreset(selected.id, selected.revision);
    if (!result) {
      const error = dashboard.getSnapshot().presetError;
      setDeleteReview(false);
      if (error === 'CyclePresetConflict') await dashboard.loadCycles(state.cycles?.offset ?? 0);
      return;
    }
    await dashboard.loadCycles(state.cycles?.offset ?? 0);
    setDeleteReview(false); setSelectedId(''); setSavedNotice(false);
  };
  const startNew = (source?: CyclePreset) => {
    const config = source?.configuration ?? selected?.configuration ?? defaultConfig;
    setDraft({ id: null, revision: null, name: source ? `${source.name} · ${t('presetCopySuffix')}` : '', config: { ...config } });
    setDurationField(config.terminationMode === 'CycleCount' ? 'count' : 'total'); setDeleteReview(false); setSaveReview(false); setSavedNotice(false);
  };
  const startEdit = (preset: CyclePreset) => { if (preset.isBuiltIn || !preset.revision) return; setDraft(configDraft(preset)); setDurationField(preset.configuration.terminationMode === 'CycleCount' ? 'count' : 'total'); setDeleteReview(false); setSaveReview(false); setSavedNotice(false); };
  const setMinutes = (key: 'focusSeconds' | 'shortBreakSeconds' | 'longBreakSeconds', value: string) => setDraft(current => current ? ({ ...current, config: { ...current.config, [key]: Number(value) * 60 } }) : current);
  const setInteger = (key: 'cyclesBeforeLongBreak' | 'cycleCount' | 'totalDurationSeconds', value: string) => setDraft(current => current ? ({ ...current, config: { ...current.config, [key]: value === '' ? 0 : Number(key === 'totalDurationSeconds' ? value : value) * (key === 'totalDurationSeconds' ? 60 : 1) } }) : current);
  const setMode = (mode: 'CycleCount' | 'TotalDuration') => {
    setDurationField(mode === 'CycleCount' ? 'count' : 'total');
    setDraft(current => current ? ({ ...current, config: { ...current.config, terminationMode: mode, cycleCount: mode === 'CycleCount' ? 4 : null, totalDurationSeconds: mode === 'TotalDuration' ? 7200 : null } }) : current);
  };
  const error = native.presetError;
  const errorLabel = error === 'CyclePresetConflict' ? t('presetConflict') : error === 'CyclePresetNameConflict' ? t('presetNameConflict') : error === 'CyclePresetReferenced' ? t('presetReferenced') : error === 'CyclePresetBuiltIn' ? t('presetBuiltIn') : error === 'PresetEditorUnavailable' || error === 'Unauthorized' ? t('presetUnavailable') : error === 'OutcomeUnknown' ? t('presetUnknown') : error ? t('presetSaveError') : null;
  return <>
    <button type="button" className="cycle-refresh" disabled={!allowed || !state.cycles || busy} onClick={event => { trigger.current = event.currentTarget; setOpen(true); }} aria-haspopup="dialog">{t('managePresets')}</button>
    {open && <dialog ref={dialog} className="delete-review preset-manager" aria-labelledby="preset-manager-title" onCancel={event => { event.preventDefault(); exitDialog(dialog.current, () => { setOpen(false); setDraft(null); }); }}>
      <h2 id="preset-manager-title">{t('managePresets')}</h2>
      <p className="start-note">{t('presetManagerDetail')}</p>
      {!draft ? <>
        <fieldset className="preset-list" disabled={busy}>
          <legend className="sr-only">{t('cyclePreset')}</legend>
          {presets.map(preset => <label className={`preset-option${selectedId === preset.id ? ' selected' : ''}`} key={preset.id}>
            <input type="radio" name="cycle-preset" value={preset.id} checked={selectedId === preset.id} onChange={() => { setSelectedId(preset.id); setDeleteReview(false); }}/>
            <span className="preset-option-copy"><span>{preset.name}</span><small>{preset.isBuiltIn ? t('presetBuiltInTag') : t('presetCustomTag')}</small></span>
          </label>)}
        </fieldset>
        <div className="preset-actions">
          <button type="button" disabled={!editable || busy} onClick={() => startNew()}>{t('newPreset')}</button>
          {selected?.isBuiltIn && <button type="button" disabled={!editable || busy} onClick={() => startNew(selected)}>{t('duplicatePreset')}</button>}
          {selected && !selected.isBuiltIn && <><button type="button" disabled={!editable || !selected.revision || busy} onClick={() => startEdit(selected)}>{t('editPreset')}</button><button type="button" disabled={!editable || busy} onClick={() => startNew(selected)}>{t('duplicatePreset')}</button><button type="button" className="danger-action" disabled={!editable || !selected.revision || busy} onClick={() => setDeleteReview(true)}>{t('deletePreset')}</button></>}
          {state.cycles && state.cycles.total > 8 && <div className="catalog-pages"><button type="button" disabled={!state.cycles.offset || state.cyclesLoading || busy} onClick={() => void dashboard.loadCycles(Math.max(0, state.cycles!.offset - 8))}>{t('previous')}</button><span>{state.cycles.offset + 1}–{Math.min(state.cycles.offset + 8, state.cycles.total)} / {state.cycles.total}</span><button type="button" disabled={state.cycles.offset + 8 >= state.cycles.total || state.cyclesLoading || busy} onClick={() => void dashboard.loadCycles(state.cycles!.offset + 8)}>{t('next')}</button></div>}
        </div>
        {deleteReview && selected && <section className="preset-delete-review" aria-labelledby="preset-delete-title"><h3 id="preset-delete-title">{t('deletePreset')} · {selected.name}</h3><p>{t('presetDeleteConsequence')}</p><div className="review-actions"><button type="button" onClick={() => setDeleteReview(false)}>{t('cancel')}</button><button type="button" className="danger-action" disabled={!editable || busy} onClick={() => void mutateDelete()}>{t('confirmDelete')}</button></div></section>}
      </> : <>
        <form className="preset-form" onSubmit={event => { event.preventDefault(); void mutateSave(); }}>
          <label>{t('presetName')}<input autoFocus value={draft.name} maxLength={80} onChange={event => setDraft(current => current ? ({ ...current, name: event.target.value }) : current)} disabled={!editable || busy} required/></label>
          <fieldset disabled={!editable || busy}><legend>{t('cycleDurations')}</legend><div className="cycle-grid">
            <label>{t('focusMinutes')}<input type="number" min="1" max="180" value={draft.config.focusSeconds / 60 || ''} onChange={event => setMinutes('focusSeconds', event.target.value)}/></label>
            <label>{t('shortBreakMinutes')}<input type="number" min="1" max="60" value={draft.config.shortBreakSeconds / 60 || ''} onChange={event => setMinutes('shortBreakSeconds', event.target.value)}/></label>
            <label>{t('longBreakMinutes')}<input type="number" min="1" max="120" value={draft.config.longBreakSeconds / 60 || ''} onChange={event => setMinutes('longBreakSeconds', event.target.value)}/></label>
            <label>{t('longBreakEvery')}<input type="number" min="2" max="12" value={draft.config.cyclesBeforeLongBreak || ''} onChange={event => setInteger('cyclesBeforeLongBreak', event.target.value)}/></label>
            <label>{t('cycleFinish')}<select value={durationField} onChange={event => setMode(event.target.value === 'count' ? 'CycleCount' : 'TotalDuration')}><option value="count">{t('finishByCount')}</option><option value="total">{t('finishByDuration')}</option></select></label>
            {durationField === 'count' ? <label>{t('focusCycles')}<input type="number" min="1" max="24" value={draft.config.cycleCount ?? ''} onChange={event => setInteger('cycleCount', event.target.value)}/></label> : <label>{t('totalMinutes')}<input type="number" min="1" max="43200" value={(draft.config.totalDurationSeconds ?? 0) / 60 || ''} onChange={event => setInteger('totalDurationSeconds', event.target.value)}/></label>}
          </div></fieldset>
          {!isCycleConfiguration(draft.config) && <p className="notice" role="alert">{t('presetConfigurationInvalid')}</p>}
          <p className="start-note">{t('presetSavingDoesNotStart')}</p>
          {saveReview && <section className="preset-delete-review" aria-labelledby="preset-save-review-title"><h3 id="preset-save-review-title">{t(draft.id ? 'editPreset' : 'newPreset')} · {draft.name}</h3><p>{t('presetSavingDoesNotStart')}</p><div className="review-actions"><button type="button" onClick={() => setSaveReview(false)}>{t('cancel')}</button><button type="button" className="primary-action" disabled={!editable || busy} onClick={() => void mutateSave()}>{t('savePreset')}</button></div></section>}
          {savedNotice && <p role="status">{t('presetSavedRefreshSelection')}</p>}
          <div className="review-actions"><button type="button" disabled={busy} onClick={() => { setDraft(null); setSaveReview(false); }}>{t('cancel')}</button><button type="submit" className="primary-action" disabled={!editable || busy || !draft.name.trim() || !isCycleConfiguration(draft.config)}>{busy ? t('savingPreset') : t('savePreset')}</button></div>
        </form>
      </>}
      {errorLabel && <p className="notice" role="alert">{errorLabel}{error === 'OutcomeUnknown' && <button type="button" disabled={busy} onClick={() => void dashboard.loadCycles(state.cycles?.offset ?? 0)}>{t('refresh')}</button>}</p>}
      {native.presetPhase === 'unknown' && <p className="start-note">{t('presetUnknownNoRetry')}</p>}
      {state.cycles && state.cycles.editingEnabled !== true && <p className="start-note">{t('presetReadOnlyHost')}</p>}
      <div className="review-actions"><button type="button" autoFocus={!draft} disabled={busy} onClick={() => exitDialog(dialog.current, () => { setOpen(false); setDraft(null); setDeleteReview(false); })}>{t('close')}</button></div>
    </dialog>}
  </>;
}
