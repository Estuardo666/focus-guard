import { useEffect, useState } from 'react';

let presentation: Promise<unknown> | null = null;
let dashboardView: typeof import('./DashboardStart').DashboardStart | null = null;
let compactView: typeof import('./CompactFocus').CompactFocus | null = null;
export const readStartupViews = () => ({ dashboard: dashboardView, compact: compactView });
export function prepareStartupPresentation() {
  return presentation ??= Promise.allSettled([
    import('./DashboardStart').then(module => { dashboardView = module.DashboardStart; }),
    import('./CompactFocus').then(module => { compactView = module.CompactFocus; }),
    typeof document === 'undefined' ? Promise.resolve() : document.fonts?.ready ?? Promise.resolve(),
  ]);
}

/** Reveal a settled first view once; reconnecting must never hide the person's work behind a loader. */
export function useStartupPresentation(required: boolean, dataReady: boolean, prepare = prepareStartupPresentation): boolean {
  const [assetsReady, setAssetsReady] = useState(!required);
  const [complete, setComplete] = useState(!required);
  useEffect(() => {
    if (!required) return;
    let current = true;
    const fallback = window.setTimeout(() => { if (current) setAssetsReady(true); }, 3000);
    void prepare().then(() => { if (current) setAssetsReady(true); }, () => { if (current) setAssetsReady(true); });
    return () => { current = false; window.clearTimeout(fallback); };
  }, [required, prepare]);
  useEffect(() => { if (assetsReady && dataReady) setComplete(true); }, [assetsReady, dataReady]);
  return complete;
}
