import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { InfoTip, Stepper } from './ZenControls';
import { formatClock } from './formatting';

let root: Root;
let container: HTMLDivElement;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });

it('shows minutes and seconds under an hour and hours only when there are hours', () => {
  expect(formatClock(0)).toBe('00:00');
  expect(formatClock(59)).toBe('00:59');
  expect(formatClock(25 * 60)).toBe('25:00');
  expect(formatClock(3599)).toBe('59:59');
  expect(formatClock(3600)).toBe('01:00:00');
  expect(formatClock(2 * 86400 + 5)).toBe('2d 00:00:05');
});

it('keeps an info tip as the trigger description and toggles it without stealing other controls', async () => {
  await act(async () => root.render(<InfoTip label="Más información" text="Detalle largo"/>));
  const trigger = container.querySelector<HTMLButtonElement>('.info-tip-trigger')!;
  const bubble = container.querySelector<HTMLElement>('[role=tooltip]')!;
  expect(trigger.getAttribute('aria-describedby')).toBe(bubble.id);
  expect(trigger.type).toBe('button');
  expect(bubble.textContent).toBe('Detalle largo');
  await act(async () => trigger.click());
  expect(trigger.getAttribute('aria-expanded')).toBe('true');
  await act(async () => { trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); });
  expect(trigger.getAttribute('aria-expanded')).toBe('false');
});

it('steps snap to the grid, stay within limits and keep a typeable number field', async () => {
  function Harness() { const [value, setValue] = useState('24'); return <><Stepper value={value} min={1} max={30} step={5} onChange={setValue} decrease="Menos" increase="Más"/><output>{value}</output></>; }
  await act(async () => root.render(<Harness/>));
  const [less, more] = Array.from(container.querySelectorAll<HTMLButtonElement>('.stepper-button'));
  const value = () => container.querySelector('output')!.textContent;
  await act(async () => more!.click());
  expect(value()).toBe('25');
  await act(async () => more!.click());
  expect(value()).toBe('30');
  expect(more!.disabled).toBe(true);
  await act(async () => less!.click());
  expect(value()).toBe('25');
  const input = container.querySelector<HTMLInputElement>('input[type=number]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '7'); input.dispatchEvent(new Event('input', { bubbles: true })); });
  expect(value()).toBe('7');
});

it('plays a dialog exit before closing it, and closes at once without animation support', async () => {
  const { exitDialog } = await import('./ZenControls');
  vi.useFakeTimers();
  try {
    const dialog = document.createElement('dialog'); dialog.setAttribute('open', ''); document.body.append(dialog);
    const after = vi.fn();
    exitDialog(dialog, after);
    expect(after).toHaveBeenCalledTimes(1);
    expect(dialog.dataset.closing).toBeUndefined();
    after.mockClear();
    Object.assign(dialog, { animate: () => undefined });
    exitDialog(dialog, after);
    expect(dialog.dataset.closing).toBe('true');
    expect(after).not.toHaveBeenCalled();
    exitDialog(dialog, after);
    vi.advanceTimersByTime(170);
    expect(after).toHaveBeenCalledTimes(1);
    dialog.remove();
  } finally { vi.useRealTimers(); }
});
