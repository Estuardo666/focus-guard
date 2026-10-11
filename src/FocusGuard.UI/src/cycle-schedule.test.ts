import { plannedPhases, ringSegments } from './cycle-schedule';
import { presetGlyph } from './ZenControls';

const classic = { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4, terminationMode: 'CycleCount' as const, cycleCount: 4, totalDurationSeconds: null };

it('plans focus and breaks in order, with a long break every N cycles and no trailing break', () => {
  expect(plannedPhases(classic).map(phase => phase.kind)).toEqual(['focus', 'short', 'focus', 'short', 'focus', 'short', 'focus']);
  const withLong = plannedPhases({ ...classic, cyclesBeforeLongBreak: 2 });
  expect(withLong.map(phase => phase.kind)).toEqual(['focus', 'short', 'focus', 'long', 'focus', 'short', 'focus']);
  expect(withLong.reduce((sum, phase) => sum + phase.seconds, 0)).toBe(4 * 1500 + 2 * 300 + 900);
});

it('cuts the last phase of a total-duration plan at the total', () => {
  const phases = plannedPhases({ ...classic, terminationMode: 'TotalDuration', cycleCount: null, totalDurationSeconds: 3900 });
  expect(phases.reduce((sum, phase) => sum + phase.seconds, 0)).toBe(3900);
  expect(phases.at(-1)).toEqual({ kind: 'focus', seconds: 300 });
});

it('turns the plan into ring fractions that cover the whole circle without gaps', () => {
  const segments = ringSegments(classic);
  expect(segments[0]).toEqual({ kind: 'focus', start: 0, end: 1500 / 6900 });
  expect(segments.at(-1)!.end).toBeCloseTo(1, 10);
  segments.slice(1).forEach((segment, index) => expect(segment.start).toBe(segments[index]!.end));
});

it('gives the built-in presets recognisable glyphs and falls back to the plain orb', () => {
  expect(presetGlyph('a2af4f65-2de8-4e89-a0bc-4c4db6e24a01', 'Pomodoro clásico')).toBe('🍅');
  expect(presetGlyph('a2af4f65-2de8-4e89-a0bc-4c4db6e24a02', 'Deep focus')).toBe('🌊');
  expect(presetGlyph('a2af4f65-2de8-4e89-a0bc-4c4db6e24a03', 'Flujo extendido')).toBe('🌀');
  expect(presetGlyph('11111111-1111-4111-8111-111111111111', 'Mi pomodoro')).toBe('🍅');
  expect(presetGlyph('11111111-1111-4111-8111-111111111111', 'Escritura')).toBeNull();
});
