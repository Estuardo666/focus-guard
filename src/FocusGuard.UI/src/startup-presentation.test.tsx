import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { useStartupPresentation } from './startup-presentation';

it('holds the first view until assets and data settle, then preserves it through a reconnection', async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const container = document.createElement('div');
  const root = createRoot(container);
  let loaded!: () => void;
  const assets = new Promise<void>(resolve => { loaded = resolve; });
  const prepare = () => assets;
  const View = ({ ready }: { ready: boolean }) => <span>{useStartupPresentation(true, ready, prepare) ? 'controls' : 'zen'}</span>;
  try {
    await act(async () => root.render(<View ready={false}/>));
    expect(container.textContent).toBe('zen');
    await act(async () => root.render(<View ready/>));
    expect(container.textContent).toBe('zen');
    await act(async () => loaded());
    expect(container.textContent).toBe('controls');
    await act(async () => root.render(<View ready={false}/>));
    expect(container.textContent).toBe('controls');
  } finally { await act(async () => root.unmount()); }
});

it('reveals recovery controls after stalled assets and releases its timeout on closing', async () => {
  vi.useFakeTimers();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const container = document.createElement('div');
  const root = createRoot(container);
  const prepare = () => new Promise<void>(() => {});
  const View = () => <span>{useStartupPresentation(true, true, prepare) ? 'recovery' : 'zen'}</span>;
  try {
    await act(async () => root.render(<View/>));
    await act(async () => { vi.advanceTimersByTime(3000); });
    expect(container.textContent).toBe('recovery');
  } finally { await act(async () => root.unmount()); expect(vi.getTimerCount()).toBe(0); vi.useRealTimers(); }
});
