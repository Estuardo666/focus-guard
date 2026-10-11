import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { CountdownTimerIcon } from '@radix-ui/react-icons';
import { CardSlider, presetGlyph, Stepper } from './ZenControls';
import { plannedPhases, type PlannedPhase } from './cycle-schedule';
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
  const [hoverKind, setHoverKind] = useState<PlannedPhase['kind'] | null>(null);
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
  const shown = preset?.configuration ?? (presetId === preferredPresetId ? preferredPresetConfiguration : null);
  const cards: { id: string; name: string; configuration: CycleConfiguration | null }[] = [
    ...(preferredPresetId && !state.cycles?.items.some(item => item.id === preferredPresetId) ? [{ id: preferredPresetId, name: preferredPresetName ?? t('defaultCyclePreset'), configuration: preferredPresetConfiguration }] : []),
    ...(state.cycles?.items ?? []).map(item => ({ id: item.id, name: item.name, configuration: item.configuration })),
  ];
  const customConfiguration = {
    focus: Number(values.focusSeconds), short: Number(values.shortBreakSeconds), long: Number(values.longBreakSeconds),
    every: Number(values.cyclesBeforeLongBreak), count: Number(values.cycleCount), total: Number(values.totalDurationSeconds),
  };
  const set = (key: keyof typeof values) => (value: string) => setValues(previous => ({ ...previous, [key]: value }));
  const tiles = [
    { key: 'focusSeconds', label: t('focusMinutes'), tone: 'focus', kind: 'focus', min: 1, max: 180, step: 5, unit: t('minutesShort') },
    { key: 'shortBreakSeconds', label: t('shortBreakMinutes'), tone: 'short', kind: 'short', min: 1, max: 60, step: 1, unit: t('minutesShort') },
    { key: 'longBreakSeconds', label: t('longBreakMinutes'), tone: 'long', kind: 'long', min: 1, max: 120, step: 5, unit: t('minutesShort') },
    { key: 'cyclesBeforeLongBreak', label: t('longBreakEvery'), tone: 'every', kind: 'long', min: 2, max: 12, step: 1, unit: '×' },
  ] as const;
  return <div className="cycle-fields">
    <span className="setup-section-label" id="cycle-preset-label">{t('cyclePreset')}</span>
    <CardSlider className="cycle-cards" labelledBy="cycle-preset-label" previous={t('previous')} next={t('next')} goTo={index => t('goToCard').replace('{index}', String(index))}>
      {cards.map(card => { const checked = !custom && card.id === presetId; return <label key={card.id} className="choice-card" data-checked={checked}>
        <input type="radio" name="cycle-preset" value={card.id} checked={checked} disabled={!allowed || state.cyclesLoading} onChange={() => selectPreset(card.id)}/>
        <span className="choice-card-icon" aria-hidden="true">{presetGlyph(card.id, card.name) ?? <CountdownTimerIcon focusable="false"/>}</span>
        <span className="choice-card-name">{card.name}</span>
        {card.configuration && <CycleTimeline configuration={card.configuration} compact/>}
      </label>; })}
    </CardSlider>
    {!custom && shown && <p className="cycle-summary">{shown.focusSeconds / 60} {t('focusMinutes')} · {shown.shortBreakSeconds / 60} {t('shortBreakMinutes')} · {shown.longBreakSeconds / 60} {t('longBreakMinutes')} · {shown.terminationMode === 'CycleCount' ? `${shown.cycleCount} ${t('focusCycles')}` : `${shown.totalDurationSeconds! / 60} ${t('totalMinutes')}`}</p>}
    {state.cycles && state.cycles.total > 8 && <div className="catalog-pages"><button type="button" disabled={state.cycles.offset === 0 || state.cyclesLoading || !allowed} onClick={() => void dashboard.loadCycles(state.cycles!.offset - 8)}>{t('previous')}</button><span>{state.cycles.offset + 1}–{Math.min(state.cycles.offset + 8, state.cycles.total)} / {state.cycles.total}</span><button type="button" disabled={state.cycles.offset + 8 >= state.cycles.total || state.cyclesLoading || !allowed} onClick={() => void dashboard.loadCycles(state.cycles!.offset + 8)}>{t('next')}</button></div>}
    {state.cyclesLoading && <p role="status">{t('cyclesLoading')}</p>}
    {state.cyclesError && <p className="notice" role="alert">{t('cyclesError')} <button type="button" disabled={state.cyclesLoading} onClick={() => void dashboard.loadCycles()}>{t('refresh')}</button></p>}
    <details className="cycle-custom" open={custom} onToggle={event => setCustom(event.currentTarget.open)}><summary>{t('customCycle')}</summary>
      <fieldset disabled={!allowed || !custom}><legend className="sr-only">{t('cycleDurations')}</legend>
        <div className="cycle-tiles">{tiles.map(tile => <div key={tile.key} className="cycle-tile" data-tone={tile.tone} data-active={hoverKind === tile.kind ? 'true' : undefined} onPointerEnter={() => setHoverKind(tile.kind)} onPointerLeave={() => setHoverKind(null)} onFocus={() => setHoverKind(tile.kind)} onBlur={() => setHoverKind(null)}>
          <label htmlFor={`cycle-${tile.key}`}>{tile.label}</label>
          <Stepper id={`cycle-${tile.key}`} value={values[tile.key]} min={tile.min} max={tile.max} step={tile.step} onChange={set(tile.key)} unit={tile.unit} decrease={t('decrease')} increase={t('increase')}/>
        </div>)}</div>
        <div className="cycle-finish cycle-tile" data-tone="finish">
          <span className="cycle-finish-label" id="cycle-finish-label">{t('cycleFinish')}</span>
          <div className="segmented" role="radiogroup" aria-labelledby="cycle-finish-label">{(['CycleCount', 'TotalDuration'] as const).map(mode => <label key={mode} data-checked={termination === mode}><input type="radio" name="cycle-finish" value={mode} checked={termination === mode} onChange={() => setTermination(mode)}/>{termination === mode && <motion.span layoutId="cycle-finish-pill" className="segment-pill" aria-hidden="true" transition={{ type: 'spring', duration: 0.4, bounce: 0 }}/>}<span className="segment-label">{t(mode === 'CycleCount' ? 'finishByCount' : 'finishByDuration')}</span></label>)}</div>
          <div className="cycle-finish-value"><label htmlFor="cycle-finish-value">{termination === 'CycleCount' ? t('focusCycles') : t('totalMinutes')}</label>
            <Stepper id="cycle-finish-value" value={termination === 'CycleCount' ? values.cycleCount : values.totalDurationSeconds} min={1} max={termination === 'CycleCount' ? 24 : 43200} step={termination === 'CycleCount' ? 1 : 15} onChange={set(termination === 'CycleCount' ? 'cycleCount' : 'totalDurationSeconds')} unit={termination === 'CycleCount' ? '×' : t('minutesShort')} decrease={t('decrease')} increase={t('increase')}/></div>
        </div>
        {[customConfiguration.focus, customConfiguration.short, customConfiguration.long, customConfiguration.every].every(value => Number.isFinite(value) && value > 0) && <CycleTimeline configuration={{
          focusSeconds: customConfiguration.focus * 60, shortBreakSeconds: customConfiguration.short * 60, longBreakSeconds: customConfiguration.long * 60,
          cyclesBeforeLongBreak: Math.max(2, Math.round(customConfiguration.every)), terminationMode: termination,
          cycleCount: termination === 'CycleCount' ? Math.max(1, Math.min(24, Math.round(customConfiguration.count) || 1)) : null,
          totalDurationSeconds: termination === 'TotalDuration' ? Math.max(1, customConfiguration.total || 1) * 60 : null }} activeKind={hoverKind} onHoverKind={setHoverKind} t={t}/>}
        <p className="start-note">{t('cycleLimit')}</p>
      </fieldset>
    </details>
    <CyclePresetManager dashboard={dashboard} state={state} allowed={allowed} t={t} onSelected={selectPreset}/>
  </div>;
}

/** The whole session as proportional bars (focus, short and long breaks) with the total; widths animate as values change. */
function CycleTimeline({ configuration, compact = false, activeKind = null, onHoverKind, t }: {
  configuration: CycleConfiguration; compact?: boolean; activeKind?: PlannedPhase['kind'] | null; onHoverKind?(kind: PlannedPhase['kind'] | null): void;
  t?(key: keyof typeof es): string;
}) {
  const phases = plannedPhases(configuration);
  const total = phases.reduce((sum, phase) => sum + phase.seconds, 0);
  const shownPhases = compact ? phases.slice(0, 7) : phases;
  const hours = Math.floor(total / 3600), minutes = Math.round((total % 3600) / 60);
  const names = t ? { focus: t('focusMinutes'), short: t('shortBreakMinutes'), long: t('longBreakMinutes') } : null;
  return <span className={`cycle-timeline${compact ? ' compact' : ''}`} data-highlight={activeKind ?? undefined} aria-hidden="true">
    <span className="cycle-bars">{shownPhases.map((phase, index) => <span key={index} className="cycle-bar" data-kind={phase.kind}
      data-active={activeKind === phase.kind ? 'true' : undefined} title={names ? `${names[phase.kind]} · ${Math.round(phase.seconds / 60)}` : undefined}
      onPointerEnter={onHoverKind ? () => onHoverKind(phase.kind) : undefined} onPointerLeave={onHoverKind ? () => onHoverKind(null) : undefined}
      style={{ flexGrow: phase.seconds }}/>)}</span>
    <span className="cycle-total">{hours ? `${hours} h ` : ''}{minutes ? `${minutes} min` : ''}</span>
  </span>;
}
