import type { CycleConfiguration } from './native/cycles';

export type PlannedPhase = Readonly<{ kind: 'focus' | 'short' | 'long'; seconds: number }>;
export type RingSegment = Readonly<{ kind: PlannedPhase['kind']; start: number; end: number }>;

/** The phases a configuration produces, in order (the last phase of a total-duration plan may be cut short). */
export function plannedPhases(configuration: CycleConfiguration): PlannedPhase[] {
  const phases: PlannedPhase[] = [];
  const total = configuration.terminationMode === 'TotalDuration' ? configuration.totalDurationSeconds ?? 0 : Infinity;
  const cycles = configuration.terminationMode === 'CycleCount' ? configuration.cycleCount ?? 1 : 24;
  let elapsed = 0;
  for (let cycle = 1; cycle <= cycles && elapsed < total; cycle++) {
    const focus = Math.min(configuration.focusSeconds, total - elapsed);
    phases.push({ kind: 'focus', seconds: focus });
    elapsed += focus;
    if (cycle === cycles || elapsed >= total) break;
    const long = cycle % configuration.cyclesBeforeLongBreak === 0;
    const rest = Math.min(long ? configuration.longBreakSeconds : configuration.shortBreakSeconds, total - elapsed);
    phases.push({ kind: long ? 'long' : 'short', seconds: rest });
    elapsed += rest;
  }
  return phases;
}

/** The same phases as fractions of the whole session, for drawing a segmented ring. */
export function ringSegments(configuration: CycleConfiguration): RingSegment[] {
  const phases = plannedPhases(configuration);
  const total = phases.reduce((sum, phase) => sum + phase.seconds, 0);
  if (total <= 0) return [];
  let cursor = 0;
  return phases.map(phase => { const start = cursor / total; cursor += phase.seconds; return { kind: phase.kind, start, end: cursor / total }; });
}
