import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { FocusOrb, minutesForAngle, supportsWebGL } from './FocusOrb';

let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  window.matchMedia = vi.fn().mockImplementation(() => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }));
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

it('maps ring angles to five-minute steps from the top, clockwise', () => {
  expect(minutesForAngle(0)).toBe(5);
  expect(minutesForAngle(Math.PI / 2)).toBe(30);
  expect(minutesForAngle(Math.PI)).toBe(60);
  expect(minutesForAngle(-Math.PI / 2)).toBe(90);
  expect(minutesForAngle(Math.PI * 2 - 0.01)).toBe(120);
});

it('exposes the duration dial as a keyboard slider bounded to the service duration limits', async () => {
  const changes: number[] = [];
  const render = (minutes: number) => act(async () => root.render(<FocusOrb mode="dial" minutes={minutes} onMinutesChange={value => changes.push(value)}
    label="Duración" valueText={`${minutes} min`}><span>{minutes}</span></FocusOrb>));
  await render(25);
  const slider = container.querySelector<HTMLElement>('[role=slider]')!;
  expect(slider.getAttribute('aria-label')).toBe('Duración');
  expect(slider.getAttribute('aria-valuenow')).toBe('25');
  expect(slider.getAttribute('aria-valuemin')).toBe('1');
  expect(slider.getAttribute('aria-valuemax')).toBe('720');
  expect(slider.getAttribute('aria-valuetext')).toBe('25 min');
  expect(slider.tabIndex).toBe(0);
  const press = (key: string) => act(async () => { slider.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true })); });
  await press('ArrowUp'); await press('ArrowLeft'); await press('PageUp'); await press('Home'); await press('End');
  expect(changes).toEqual([26, 24, 30, 1, 120]);
  await render(720);
  changes.length = 0;
  await press('ArrowUp');
  expect(changes).toEqual([720]);
});

it('keeps a disabled dial out of the tab order and ignores keys', async () => {
  const change = vi.fn();
  await act(async () => root.render(<FocusOrb mode="dial" minutes={25} disabled onMinutesChange={change} label="Duración" valueText="25 min"/>));
  const slider = container.querySelector<HTMLElement>('[role=slider]')!;
  expect(slider.tabIndex).toBe(-1);
  expect(slider.getAttribute('aria-disabled')).toBe('true');
  await act(async () => { slider.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true })); });
  expect(change).not.toHaveBeenCalled();
});

it('renders the countdown ring without a slider and falls back to a CSS orb without WebGL', async () => {
  expect(supportsWebGL()).toBe(false);
  await act(async () => root.render(<FocusOrb mode="countdown" progress={0.4}><p className="timer">00:10:00</p></FocusOrb>));
  expect(container.querySelector('[role=slider]')).toBeNull();
  expect(container.querySelector('.orb-fallback')).not.toBeNull();
  expect(container.querySelector('canvas')).toBeNull();
  expect(container.querySelector('.orb-ring')?.getAttribute('aria-hidden')).toBe('true');
  expect(container.querySelector('.timer')?.textContent).toBe('00:10:00');
});

it('draws a cycle as phase segments, dims spent time and marks now', async () => {
  const segments = [{ kind: 'focus' as const, start: 0, end: 0.5 }, { kind: 'short' as const, start: 0.5, end: 0.6 }, { kind: 'focus' as const, start: 0.6, end: 1 }];
  await act(async () => root.render(<FocusOrb mode="countdown" progress={0.45} segments={segments}/>));
  expect(container.querySelector('.orb-progress')).toBeNull();
  expect(Array.from(container.querySelectorAll('.orb-segments > g')).map(group => group.getAttribute('data-kind'))).toEqual(['focus', 'short', 'focus']);
  // 55 % elapsed: the first focus is spent (track only), the break and the last focus are still live.
  expect(container.querySelectorAll('.orb-seg-track')).toHaveLength(3);
  expect(container.querySelectorAll('.orb-seg-live')).toHaveLength(2);
  expect(container.querySelector('.orb-now')).not.toBeNull();
});
