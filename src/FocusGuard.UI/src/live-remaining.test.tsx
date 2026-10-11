import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { useLiveRemaining } from './live-remaining';
import type { ConnectionSnapshot } from './native/protocol';

it('updates once per displayed second, cancels hidden work and catches up on return without changing service authority', async () => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'performance'] });
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const snapshot = { phase: 'ready', status: { sessionState: 'Active', remainingSeconds: 600 } } as ConnectionSnapshot;
  const container = document.createElement('div');
  const root = createRoot(container);
  let renders = 0;
  let hidden = false;
  const descriptor = Object.getOwnPropertyDescriptor(document, 'hidden');
  Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
  const View = () => { renders++; return <span>{useLiveRemaining(snapshot)}</span>; };
  try {
    await act(async () => root.render(<View/>));
    expect(container.textContent).toBe('600');
    await act(async () => { vi.advanceTimersByTime(999); });
    expect(renders).toBe(1);
    await act(async () => { vi.advanceTimersByTime(1); });
    expect(container.textContent).toBe('599');
    expect(renders).toBe(2);
    hidden = true;
    await act(async () => document.dispatchEvent(new Event('visibilitychange')));
    expect(vi.getTimerCount()).toBe(0);
    const before = renders;
    await act(async () => { vi.advanceTimersByTime(30_000); });
    expect(renders).toBe(before);
    hidden = false;
    await act(async () => document.dispatchEvent(new Event('visibilitychange')));
    expect(container.textContent).toBe('569');
    expect(snapshot.status!.remainingSeconds).toBe(600);
  } finally {
    await act(async () => root.unmount());
    if (descriptor) Object.defineProperty(document, 'hidden', descriptor); else Reflect.deleteProperty(document, 'hidden');
    vi.useRealTimers();
  }
});
