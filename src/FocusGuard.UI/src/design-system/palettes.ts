// Visual "ambient" palettes. They only recolour the atmosphere, orb and accents; the semantic
// contrast tokens verified by scripts/verify-dist.mjs stay authoritative for text.
export const palettes = ['aurora', 'mist', 'ember', 'ocean'] as const;
export type Palette = (typeof palettes)[number];

const storageKey = 'focusguard.palette';

export function isPalette(value: unknown): value is Palette {
  return typeof value === 'string' && (palettes as readonly string[]).includes(value);
}

// A per-viewer convenience only: storage can be unavailable, so every access is guarded.
export function loadPalette(): Palette {
  try {
    const stored = globalThis.localStorage?.getItem(storageKey);
    return isPalette(stored) ? stored : 'aurora';
  } catch {
    return 'aurora';
  }
}

export function savePalette(value: Palette): void {
  try {
    globalThis.localStorage?.setItem(storageKey, value);
  } catch {
    // Ignore: the palette simply resets to the default next time.
  }
}

/** Reads the orb colours of the active palette from CSS custom properties. */
export function readOrbColors(element: Element): [string, string, string] {
  const style = getComputedStyle(element);
  const read = (name: string, fallback: string) => style.getPropertyValue(name).trim() || fallback;
  return [read('--orb-a', '#c7a6ff'), read('--orb-b', '#8ea6ff'), read('--orb-c', '#ff9fd0')];
}

// Orb material. Like the palette, a per-viewer look stored locally and mirrored on <html data-orb>.
export const orbStyles = ['pearl', 'voice', 'lava', 'cloud'] as const;
export type OrbStyle = (typeof orbStyles)[number];
const orbStorageKey = 'focusguard.orb';

export function isOrbStyle(value: unknown): value is OrbStyle {
  return typeof value === 'string' && (orbStyles as readonly string[]).includes(value);
}

export function loadOrbStyle(): OrbStyle {
  try {
    const stored = globalThis.localStorage?.getItem(orbStorageKey);
    return isOrbStyle(stored) ? stored : 'pearl';
  } catch {
    return 'pearl';
  }
}

export function saveOrbStyle(value: OrbStyle): void {
  try {
    globalThis.localStorage?.setItem(orbStorageKey, value);
  } catch {
    // Ignore: the orb simply resets to the pearl next time.
  }
}

// Whether to show the review step before a session. Strict sessions always show it; only ordinary ones can skip it.
const reviewStorageKey = 'focusguard.reviewBeforeStart';

export function loadReviewBeforeStart(): boolean {
  try {
    return globalThis.localStorage?.getItem(reviewStorageKey) !== 'false';
  } catch {
    return true;
  }
}

export function saveReviewBeforeStart(value: boolean): void {
  try {
    globalThis.localStorage?.setItem(reviewStorageKey, String(value));
  } catch {
    // Ignore: the review step simply stays on.
  }
}
