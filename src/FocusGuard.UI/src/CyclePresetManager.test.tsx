import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { CyclePresetManager } from './CyclePresetManager';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import type { CycleConfiguration, CyclePreset } from './native/cycles';
import es from './locales/es.json';

const config: CycleConfiguration = {
  focusSeconds: 1500,
  shortBreakSeconds: 300,
  longBreakSeconds: 900,
  cyclesBeforeLongBreak: 4,
  terminationMode: 'CycleCount',
  cycleCount: 4,
  totalDurationSeconds: null
};
const presets: CyclePreset[] = [
  { id: '22222222-2222-4222-8222-222222222222', name: 'Pomodoro', isBuiltIn: true, configuration: config },
  { id: '33333333-3333-4333-8333-333333333333', name: 'Sesión larga', isBuiltIn: false, configuration: config, revision: 'A'.repeat(64) }
];
let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

it('exposes cycle preset choice as a labelled native radio group', async () => {
  const nativeSnapshot = { presetPhase: 'idle', presetError: null } as unknown as DashboardSnapshot;
  const dashboard = {
    subscribe: () => () => undefined,
    getSnapshot: () => nativeSnapshot
  } as unknown as DashboardClient;
  const state = { cycles: { items: presets, offset: 0, total: presets.length, editingEnabled: true }, cyclesLoading: false } as unknown as DashboardSnapshot;

  await act(async () => root.render(<CyclePresetManager dashboard={dashboard} state={state} allowed t={key => es[key]} onSelected={() => undefined}/>));
  await act(async () => container.querySelector<HTMLButtonElement>('.cycle-refresh')!.click());

  const group = container.querySelector<HTMLFieldSetElement>('fieldset.preset-list')!;
  const radios = Array.from(group.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
  expect(group.querySelector('legend')?.textContent).toBe(es.cyclePreset);
  expect(radios.map(radio => radio.name)).toEqual(['cycle-preset', 'cycle-preset']);
  expect(radios.map(radio => radio.checked)).toEqual([true, false]);
  expect(radios[0]!.closest('label')?.textContent).toContain(presets[0]!.name);
  expect(radios[1]!.closest('label')?.textContent).toContain(presets[1]!.name);
  expect(container.querySelector('[role="listbox"], [role="option"]')).toBeNull();

  await act(async () => radios[1]!.click());
  expect(radios.map(radio => radio.checked)).toEqual([false, true]);
});
