type Copy = (key: 'hoursShort' | 'minutesShort' | 'lessThanOneMinute') => string;

export function formatFocusSeconds(seconds: number, t: Copy): string {
  const wholeSeconds = Math.max(0, Math.floor(seconds));
  if (wholeSeconds > 0 && wholeSeconds < 60) return t('lessThanOneMinute');
  const hours = Math.floor(wholeSeconds / 3600);
  const minutes = Math.floor(wholeSeconds % 3600 / 60);
  return hours ? `${hours} ${t('hoursShort')} ${minutes} ${t('minutesShort')}` : `${minutes} ${t('minutesShort')}`;
}

export function formatSummaryFocusedMinutes(focusedMinutes: number, plannedMinutes: number, t: Copy): string {
  if (plannedMinutes === 1 && focusedMinutes === 0) return t('lessThanOneMinute');
  return `${focusedMinutes} ${t('minutesShort')}`;
}
