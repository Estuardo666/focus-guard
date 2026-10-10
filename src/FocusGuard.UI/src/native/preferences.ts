const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(key => Object.hasOwn(v, key));
const guid = /^(?!0{32})[a-f0-9]{32}$/i;
export type UserPreferences = Readonly<{
  uiLanguage: 'es' | 'en'; theme: 'Dark' | 'Light'; reduceMotion: boolean;
  sessionEnd: 'Window' | 'Notification' | 'DimmedWindow' | 'None'; cyclePhaseNotifications: boolean;
  enforcementEnabled: boolean; lastDurationMinutes: number; lastSessionMode: 'SimpleTimer' | 'FocusCycle';
  lastCyclePresetId: string | null;
}>;
export function isUserPreferences(v: unknown): v is UserPreferences {
  return record(v) && exact(v, ['uiLanguage', 'theme', 'reduceMotion', 'sessionEnd', 'cyclePhaseNotifications', 'enforcementEnabled', 'lastDurationMinutes', 'lastSessionMode', 'lastCyclePresetId'])
    && (v.uiLanguage === 'es' || v.uiLanguage === 'en') && (v.theme === 'Dark' || v.theme === 'Light')
    && typeof v.reduceMotion === 'boolean' && ['Window', 'Notification', 'DimmedWindow', 'None'].includes(String(v.sessionEnd))
    && typeof v.cyclePhaseNotifications === 'boolean' && typeof v.enforcementEnabled === 'boolean'
    && Number.isSafeInteger(v.lastDurationMinutes) && (v.lastDurationMinutes as number) >= 1 && (v.lastDurationMinutes as number) <= 720
    && (v.lastSessionMode === 'SimpleTimer' || v.lastSessionMode === 'FocusCycle')
    && (v.lastCyclePresetId === null || typeof v.lastCyclePresetId === 'string' && guid.test(v.lastCyclePresetId));
}
