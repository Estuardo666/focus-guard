import { useEffect, useRef, useState } from 'react';
import type { ConnectionSnapshot } from './native/protocol';

/**
 * Seconds left for display, counted down every second between service polls. The service value stays
 * authoritative: each new status resets the base, and a stale or non-running reading is shown as received.
 */
export function useLiveRemaining(snapshot: ConnectionSnapshot): number | null {
  const status = snapshot.status;
  const base = useRef<{ status: unknown; at: number }>({ status: null, at: 0 });
  if (base.current.status !== status) base.current = { status, at: performance.now() };
  const ticking = snapshot.phase === 'ready' && !!status && ['Active', 'Activating'].includes(status.sessionState) && status.remainingSeconds > 0;
  const [, setTick] = useState(0);
  useEffect(() => {
    if (!ticking) return;
    let timer = 0;
    const schedule = () => {
      window.clearTimeout(timer);
      if (document.hidden) return;
      // Align to the next displayed second, even when a service poll arrived between second boundaries.
      const elapsed = performance.now() - base.current.at;
      timer = window.setTimeout(() => { setTick(value => value + 1); schedule(); }, 1000 - elapsed % 1000);
    };
    const visible = () => { setTick(value => value + 1); schedule(); };
    schedule();
    document.addEventListener('visibilitychange', visible);
    return () => { window.clearTimeout(timer); document.removeEventListener('visibilitychange', visible); };
  }, [ticking, status]);
  if (!status) return null;
  if (!ticking) return status.remainingSeconds;
  const elapsed = Math.floor((performance.now() - base.current.at) / 1000);
  return Math.max(0, status.remainingSeconds - elapsed);
}
