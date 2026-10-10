import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { PreferencesPanel } from './PreferencesPanel';
import type { DashboardClient, DashboardSnapshot } from './native/dashboard';
import es from './locales/es.json';

function makeDashboard(state: Partial<DashboardSnapshot>) {
  const snapshot = Object.freeze({ preferences: null, preferencesLoading: false, preferencesError: null, preferencesPhase: 'idle', ...state }) as unknown as DashboardSnapshot;
  return {
    subscribe: () => () => undefined,
    getSnapshot: () => snapshot,
    loadPreferences: vi.fn(async () => undefined),
    savePreferences: vi.fn(async () => null),
  } as unknown as DashboardClient;
}

describe('PreferencesPanel', () => {
  let root: Root;
  let container: HTMLDivElement;
  beforeEach(() => {
    Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

  it('retries a failed startup read when the tab opens and names it a read failure, not a save failure', async () => {
    const dashboard = makeDashboard({ preferencesError: 'BridgeUnavailable' });
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()}/>));
    expect(dashboard.loadPreferences).toHaveBeenCalledTimes(1);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(es.preferencesLoadError);
    expect(container.textContent).not.toContain(es.preferencesSaveError);
  });

  it('does not start a second read while one is already running', async () => {
    const dashboard = makeDashboard({ preferencesLoading: true });
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()}/>));
    expect(dashboard.loadPreferences).not.toHaveBeenCalled();
  });

  it('keeps the save wording for a failed save', async () => {
    const preferences = { uiLanguage: 'es', theme: 'Dark', reduceMotion: false, sessionEnd: 'Window', cyclePhaseNotifications: true, enforcementEnabled: false, lastDurationMinutes: 25, lastSessionMode: 'SimpleTimer', lastCyclePresetId: null };
    const dashboard = makeDashboard({ preferences, preferencesError: 'SaveFailed', preferencesPhase: 'error' } as Partial<DashboardSnapshot>);
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()}/>));
    expect(dashboard.loadPreferences).not.toHaveBeenCalled();
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(es.preferencesSaveError);
  });
});
