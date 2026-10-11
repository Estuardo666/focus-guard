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

  const preferences = { uiLanguage: 'es', theme: 'Dark', reduceMotion: false, sessionEnd: 'Window', cyclePhaseNotifications: true, enforcementEnabled: false, lastDurationMinutes: 25, lastSessionMode: 'SimpleTimer', lastCyclePresetId: null } as const;
  const click = async (element: Element | null) => act(async () => (element as HTMLElement).click());

  it('shows the save bar only after a change and discards back to the saved values', async () => {
    const dashboard = makeDashboard({ preferences, preferencesPhase: 'idle' } as Partial<DashboardSnapshot>);
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()}/>));
    expect(container.querySelector('.pref-save-bar')).toBeNull();
    await click(container.querySelector('input[name="pref-theme"][value="Light"]'));
    expect(container.querySelector('.pref-save-bar')?.textContent).toContain(es.unsavedChanges);
    await click([...container.querySelectorAll('.pref-save-bar button')].find(item => item.textContent === es.discardChanges)!);
    expect(container.querySelector<HTMLInputElement>('input[name="pref-theme"][value="Dark"]')!.checked).toBe(true);
  });

  it('saves theme, duration, session-end choice and switches through the service', async () => {
    const saved = { ...preferences, theme: 'Light', lastDurationMinutes: 50, sessionEnd: 'Notification', enforcementEnabled: true };
    const dashboard = makeDashboard({ preferences, preferencesPhase: 'idle' } as Partial<DashboardSnapshot>);
    (dashboard.savePreferences as ReturnType<typeof vi.fn>).mockResolvedValue(saved);
    const onSaved = vi.fn();
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={onSaved}/>));
    await click(container.querySelector('input[name="pref-theme"][value="Light"]'));
    await click([...container.querySelectorAll('.quick-durations button')].find(item => item.textContent === `50 ${es.minutesShort}`)!);
    await click(container.querySelector('input[name="pref-session-end"][value="Notification"]'));
    const blocking = container.querySelector<HTMLInputElement>(`input[role=switch][aria-label="${es.defaultBlocking}"]`)!;
    await click(blocking);
    await click([...container.querySelectorAll('.pref-save-bar button')].find(item => item.textContent === es.savePreferences)!);
    expect(dashboard.savePreferences).toHaveBeenCalledWith({ ...preferences, theme: 'Light', lastDurationMinutes: 50, sessionEnd: 'Notification', enforcementEnabled: true });
    expect(onSaved).toHaveBeenCalledWith(saved);
  });

  it('offers the local ambience palettes when the shell provides them', async () => {
    const dashboard = makeDashboard({ preferences, preferencesPhase: 'idle' } as Partial<DashboardSnapshot>);
    const change = vi.fn();
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()} palette="aurora" onPaletteChange={change}/>));
    const ember = container.querySelector('.pref-ambience [data-swatch=ember]')!;
    expect(ember.textContent).toBe(es.paletteEmber);
    await click(ember);
    expect(change).toHaveBeenCalledWith('ember');
    expect(dashboard.savePreferences).not.toHaveBeenCalled();
  });

  it('offers sign-in start only when the host can read it, and shows what the host stored', async () => {
    const dashboard = makeDashboard({ preferences, preferencesPhase: 'idle' } as Partial<DashboardSnapshot>);
    const startup = { read: vi.fn(async () => false), write: vi.fn(async (enabled: boolean) => enabled) };
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()} startup={startup}/>));
    const toggle = () => container.querySelector<HTMLInputElement>(`input[aria-label="${es.startAtSignIn}"]`);
    expect(toggle()?.checked).toBe(false);
    await click(toggle());
    expect(startup.write).toHaveBeenCalledWith(true);
    expect(toggle()?.checked).toBe(true);

    const unknown = { read: vi.fn(async () => null), write: vi.fn(async () => null) };
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()} startup={unknown}/>));
    expect(toggle()).toBeNull();
  });

  it('splits preferences into three tabbed pages and applies the orb and review choices at once', async () => {
    const dashboard = makeDashboard({ preferences, preferencesPhase: 'idle' } as Partial<DashboardSnapshot>);
    const orb = vi.fn(); const review = vi.fn();
    await act(async () => root.render(<PreferencesPanel dashboard={dashboard} t={key => es[key]} onSaved={vi.fn()} orbStyle="pearl" onOrbStyleChange={orb} reviewBeforeStart onReviewBeforeStartChange={review}/>));
    const tabs = [...container.querySelectorAll<HTMLButtonElement>('[role=tab]')];
    expect(tabs.map(tab => tab.textContent)).toEqual([es.appearance, es.prefGroupSessions, es.sessionEndPresentation]);
    const pages = [...container.querySelectorAll<HTMLElement>('[role=tabpanel]')];
    expect(pages.map(page => page.hidden)).toEqual([false, true, true]);
    expect([...container.querySelectorAll<HTMLInputElement>('input[name=pref-orb]')].map(input => input.value)).toEqual(['pearl', 'voice', 'lava', 'cloud']);
    await click(container.querySelector('input[name=pref-orb][value=lava]'));
    expect(orb).toHaveBeenCalledWith('lava');
    await click(tabs[1]!);
    expect(pages.map(page => page.hidden)).toEqual([true, false, true]);
    const reviewSwitch = container.querySelector<HTMLInputElement>(`input[role=switch][aria-label="${es.reviewBeforeStart}"]`)!;
    expect(reviewSwitch.checked).toBe(true);
    await click(reviewSwitch);
    expect(review).toHaveBeenCalledWith(false);
    expect(container.querySelector('.pref-save-bar')).toBeNull();
    expect(dashboard.savePreferences).not.toHaveBeenCalled();
  });
});
