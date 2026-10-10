import { useEffect, useState } from 'react';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import { isCycleConfiguration, type CycleConfiguration, type CycleInput } from './native/cycles';
import type es from './locales/es.json';
import { CyclePresetManager } from './CyclePresetManager';

const minutesText = (seconds: number) => String(Number((seconds / 60).toFixed(6)));

export function CycleFields({ dashboard, state, allowed, t, onChange, replayConfiguration = null, preferredPresetId = null, preferredPresetName = null, preferredPresetConfiguration = null }: {
  dashboard: DashboardClient; state: DashboardSnapshot; allowed: boolean; t(key: keyof typeof es): string;
  onChange(input: CycleInput | null): void; replayConfiguration?: CycleConfiguration | null;
  preferredPresetId?: string | null; preferredPresetName?: string | null; preferredPresetConfiguration?: CycleConfiguration | null;
}) {
  const [presetId, setPresetId] = useState(preferredPresetId ?? '');
  const [custom, setCustom] = useState(replayConfiguration !== null);
  const [values, setValues] = useState(() => replayConfiguration ? {
    focusSeconds: minutesText(replayConfiguration.focusSeconds), shortBreakSeconds: minutesText(replayConfiguration.shortBreakSeconds),
    longBreakSeconds: minutesText(replayConfiguration.longBreakSeconds), cyclesBeforeLongBreak: String(replayConfiguration.cyclesBeforeLongBreak),
    cycleCount: String(replayConfiguration.cycleCount ?? 4), totalDurationSeconds: minutesText(replayConfiguration.totalDurationSeconds ?? 7200),
  } : { focusSeconds: '25', shortBreakSeconds: '5', longBreakSeconds: '15', cyclesBeforeLongBreak: '4', cycleCount: '4', totalDurationSeconds: '120' });
  const [termination, setTermination] = useState<'CycleCount' | 'TotalDuration'>(replayConfiguration?.terminationMode ?? 'CycleCount');
  useEffect(() => {
    if (replayConfiguration || presetId && (presetId === preferredPresetId || state.cycles?.items.some(preset => preset.id === presetId))) return;
    if (preferredPresetId) setPresetId(preferredPresetId);
    else if (!presetId) setPresetId(state.cycles?.items[0]?.id ?? '');
    else if (state.cycles?.items.length) setPresetId(state.cycles.items[0]!.id);
  }, [state.cycles, presetId, replayConfiguration, preferredPresetId]);
  useEffect(() => {
    const durationValues = [values.focusSeconds, values.shortBreakSeconds, values.longBreakSeconds,
      ...(termination === 'TotalDuration' ? [values.totalDurationSeconds] : [])];
    const fieldsValid = durationValues.every(value => value.trim() !== '' && Number.isFinite(Number(value)))
      && Number.isInteger(Number(values.cyclesBeforeLongBreak))
      && (termination !== 'CycleCount' || Number.isInteger(Number(values.cycleCount)));
    const unchangedReplay = replayConfiguration !== null
      && values.focusSeconds === minutesText(replayConfiguration.focusSeconds)
      && values.shortBreakSeconds === minutesText(replayConfiguration.shortBreakSeconds)
      && values.longBreakSeconds === minutesText(replayConfiguration.longBreakSeconds)
      && values.cyclesBeforeLongBreak === String(replayConfiguration.cyclesBeforeLongBreak)
      && values.cycleCount === String(replayConfiguration.cycleCount ?? 4)
      && values.totalDurationSeconds === minutesText(replayConfiguration.totalDurationSeconds ?? 7200)
      && termination === replayConfiguration.terminationMode;
    const configuration: CycleConfiguration = unchangedReplay ? replayConfiguration : {
      focusSeconds: Math.round(Number(values.focusSeconds) * 60), shortBreakSeconds: Math.round(Number(values.shortBreakSeconds) * 60),
      longBreakSeconds: Math.round(Number(values.longBreakSeconds) * 60), cyclesBeforeLongBreak: Number(values.cyclesBeforeLongBreak),
      terminationMode: termination, cycleCount: termination === 'CycleCount' ? Number(values.cycleCount) : null,
      totalDurationSeconds: termination === 'TotalDuration' ? Math.round(Number(values.totalDurationSeconds) * 60) : null,
    };
    onChange(custom ? (unchangedReplay || fieldsValid) && isCycleConfiguration(configuration) ? { presetId: null, configuration } : null : presetId ? { presetId, configuration: null } : null);
  }, [custom, values, termination, presetId, onChange, replayConfiguration]);
  const preset = state.cycles?.items.find(item => item.id === presetId);
  const selectPreset = (id: string) => { setCustom(false); setPresetId(id); };
  return <div className="cycle-fields">
    {!custom && <><label>{t('cyclePreset')}<select aria-label={t('cyclePreset')} value={presetId} onChange={event => setPresetId(event.target.value)} disabled={!allowed || state.cyclesLoading}>{preferredPresetId && presetId === preferredPresetId && !state.cycles?.items.some(item => item.id === preferredPresetId) && <option value={preferredPresetId}>{preferredPresetName ?? t('defaultCyclePreset')}</option>}{state.cycles?.items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><button className="cycle-refresh" type="button" disabled={!allowed || state.cyclesLoading} onClick={() => void dashboard.loadCycles(state.cycles?.offset ?? 0)}>{t('refresh')}</button>
      {(preset?.configuration ?? (presetId === preferredPresetId ? preferredPresetConfiguration : null)) && <p className="start-note">{(preset?.configuration ?? preferredPresetConfiguration)!.focusSeconds / 60} {t('focusMinutes')} · {(preset?.configuration ?? preferredPresetConfiguration)!.shortBreakSeconds / 60} {t('shortBreakMinutes')} · {(preset?.configuration ?? preferredPresetConfiguration)!.longBreakSeconds / 60} {t('longBreakMinutes')} · {(preset?.configuration ?? preferredPresetConfiguration)!.terminationMode === 'CycleCount' ? `${(preset?.configuration ?? preferredPresetConfiguration)!.cycleCount} ${t('focusCycles')}` : `${(preset?.configuration ?? preferredPresetConfiguration)!.totalDurationSeconds! / 60} ${t('totalMinutes')}`}</p>}
      {state.cycles && state.cycles.total > 8 && <div className="catalog-pages"><button type="button" disabled={state.cycles.offset === 0 || state.cyclesLoading || !allowed} onClick={() => void dashboard.loadCycles(state.cycles!.offset - 8)}>{t('previous')}</button><span>{state.cycles.offset + 1}–{Math.min(state.cycles.offset + 8, state.cycles.total)} / {state.cycles.total}</span><button type="button" disabled={state.cycles.offset + 8 >= state.cycles.total || state.cyclesLoading || !allowed} onClick={() => void dashboard.loadCycles(state.cycles!.offset + 8)}>{t('next')}</button></div>}
    </>}
    {state.cyclesLoading && <p role="status">{t('cyclesLoading')}</p>}
    {state.cyclesError && <p className="notice" role="alert">{t('cyclesError')} <button type="button" disabled={state.cyclesLoading} onClick={() => void dashboard.loadCycles()}>{t('refresh')}</button></p>}
    <CyclePresetManager dashboard={dashboard} state={state} allowed={allowed} t={t} onSelected={selectPreset}/>
    <details open={custom} onToggle={event => setCustom(event.currentTarget.open)}><summary>{t('customCycle')}</summary><fieldset disabled={!allowed || !custom}><legend>{t('cycleDurations')}</legend><div className="cycle-grid">{(['focusSeconds', 'shortBreakSeconds', 'longBreakSeconds', 'cyclesBeforeLongBreak'] as const).map((key, i) => <label key={key}>{t((['focusMinutes', 'shortBreakMinutes', 'longBreakMinutes', 'longBreakEvery'] as const)[i]!)}<input type="number" min={key === 'cyclesBeforeLongBreak' ? 2 : 1} max={key === 'focusSeconds' ? 180 : key === 'shortBreakSeconds' ? 60 : key === 'longBreakSeconds' ? 120 : 12} step={key === 'cyclesBeforeLongBreak' ? '1' : 'any'} value={values[key]} onChange={event => setValues(previous => ({ ...previous, [key]: event.target.value }))} required/></label>)}</div>
      <div className="cycle-grid"><label>{t('cycleFinish')}<select aria-label={t('cycleFinish')} value={termination} onChange={event => setTermination(event.target.value as typeof termination)}><option value="CycleCount">{t('finishByCount')}</option><option value="TotalDuration">{t('finishByDuration')}</option></select></label><label>{termination === 'CycleCount' ? t('focusCycles') : t('totalMinutes')}<input type="number" min="1" max={termination === 'CycleCount' ? 24 : 43200} step="1" value={termination === 'CycleCount' ? values.cycleCount : values.totalDurationSeconds} onChange={event => setValues(previous => ({ ...previous, [termination === 'CycleCount' ? 'cycleCount' : 'totalDurationSeconds']: event.target.value }))} required/></label></div><p className="start-note">{t('cycleLimit')}</p>
    </fieldset></details>
  </div>;
}
