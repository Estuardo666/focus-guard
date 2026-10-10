export function formatRemaining(seconds: number): string {
  const value = Math.max(0, Math.floor(seconds));
  const days = Math.floor(value / 86400);
  const h = Math.floor((value % 86400) / 3600);
  const m = Math.floor((value % 3600) / 60);
  const s = value % 60;
  const parts = [h, m, s].map(part => String(part).padStart(2, '0'));
  return `${days ? `${days}d ` : ''}${parts.join(':')}`;
}

export function formatMoment(value: string | number, language: 'es' | 'en'): string {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
}
