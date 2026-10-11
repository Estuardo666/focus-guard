import { act, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { FocusOrb } from './FocusOrb';

const counters = vi.hoisted(() => ({ mounts: 0, disposals: 0, still: false }));
vi.mock('./FocusOrbScene', () => ({ default: ({ still }: { still: boolean }) => {
  counters.still = still;
  useEffect(() => { counters.mounts++; return () => { counters.disposals++; }; }, []);
  return <canvas/>;
} }));

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.stubGlobal('WebGL2RenderingContext', class {});
  window.matchMedia = vi.fn().mockImplementation(() => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }));
  counters.mounts = counters.disposals = 0;
});
afterEach(() => { vi.unstubAllGlobals(); delete document.documentElement.dataset.resourceConstrained; delete document.documentElement.dataset.hostHidden; delete document.documentElement.dataset.hostInactive; });

it('releases the decorative scene under RAM pressure and recovers without losing the dial or timer content', async () => {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const change = vi.fn();
  try {
    await act(async () => root.render(<FocusOrb mode="dial" minutes={25} label="Duración" valueText="25 min" onMinutesChange={change}><p className="timer">25:00</p></FocusOrb>));
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    expect(container.querySelector('canvas')).not.toBeNull();
    await act(async () => { document.documentElement.dataset.hostInactive = 'true'; });
    expect(counters.still).toBe(true);
    expect(counters.disposals).toBe(0);
    expect(container.querySelector('canvas')).not.toBeNull();
    await act(async () => { document.documentElement.dataset.hostInactive = 'false'; });
    expect(counters.still).toBe(false);
    const slider = container.querySelector<HTMLElement>('[role=slider]')!;
    await act(async () => { document.documentElement.dataset.resourceConstrained = 'true'; });
    expect(container.querySelector('canvas')).toBeNull();
    expect(container.querySelector('.orb-fallback')).not.toBeNull();
    expect(counters.disposals).toBe(1);
    expect(container.querySelector('[role=slider]')).toBe(slider);
    expect(container.querySelector('.timer')?.textContent).toBe('25:00');
    await act(async () => slider.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true })));
    expect(change).toHaveBeenCalledWith(26);
    await act(async () => { document.documentElement.dataset.resourceConstrained = 'false'; });
    expect(container.querySelector('canvas')).not.toBeNull();
    expect(counters.mounts).toBe(2);
    await act(async () => { document.documentElement.dataset.hostHidden = 'true'; });
    expect(container.querySelector('canvas')).toBeNull();
    expect(counters.disposals).toBe(2);
    await act(async () => { document.documentElement.dataset.hostHidden = 'false'; });
    expect(container.querySelector('canvas')).not.toBeNull();
  } finally { await act(async () => root.unmount()); container.remove(); }
});

it('does not allocate a GPU scene when the window opens with little memory', async () => {
  document.documentElement.dataset.resourceConstrained = 'true';
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => root.render(<FocusOrb mode="countdown" progress={0.5}><span>10:00</span></FocusOrb>));
    expect(counters.mounts).toBe(0);
    expect(container.querySelector('.orb-fallback')).not.toBeNull();
    expect(container.querySelector('.orb-ring')).not.toBeNull();
    expect(container.textContent).toBe('10:00');
  } finally { await act(async () => root.unmount()); }
});
