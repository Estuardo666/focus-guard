export function formatRemaining(seconds: number): string {
  const value = Math.max(0, Math.floor(seconds));
  const days = Math.floor(value / 86400);
  const h = Math.floor((value % 86400) / 3600);
  const m = Math.floor((value % 3600) / 60);
  const s = value % 60;
  const parts = [h, m, s].map(part => String(part).padStart(2, '0'));
  return `${days ? `${days}d ` : ''}${parts.join(':')}`;
}

/** Compact clock for the orb: m:ss under an hour, h:mm:ss from one hour, and days when present. */
export function formatClock(seconds: number): string {
  const value = Math.max(0, Math.floor(seconds));
  if (value >= 3600) return formatRemaining(value);
  return `${String(Math.floor(value / 60)).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`;
}

export function formatMoment(value: string | number, language: 'es' | 'en'): string {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}
