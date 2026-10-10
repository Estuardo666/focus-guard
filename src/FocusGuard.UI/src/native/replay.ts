import { isCycleConfiguration, type CycleConfiguration } from './cycles';

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) =>
  Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
const name = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= 80;

export type ReplayProfile = Readonly<{
  id: string; name: string; applicationRuleCount: number; websiteRuleCount: number;
  blockedApplicationCount: number; blockedWebsiteCount: number; accentHex?: string | null;
  defaultCyclePresetId?: string | null; defaultCyclePresetName?: string | null;
  defaultCyclePresetConfiguration?: CycleConfiguration | null;
}>;

export type SessionReplayIntent = Readonly<{
  sessionId: string; available: boolean; replayable: boolean; unavailableReason: string | null;
  originalProfileName: string | null; profile: ReplayProfile | null; durationMinutes: number | null;
  mode: 'SimpleTimer' | 'FocusCycle' | null; strict: boolean | null; enforcementEnabled: boolean | null;
  cyclePresetId: string | null; cycleConfiguration: CycleConfiguration | null; objective: string | null;
}>;

function replayProfile(value: unknown): value is ReplayProfile {
  const base = ['id', 'name', 'applicationRuleCount', 'websiteRuleCount', 'blockedApplicationCount', 'blockedWebsiteCount'];
  const defaultFields = ['defaultCyclePresetId', 'defaultCyclePresetName'];
  const hasAccent = record(value) && Object.hasOwn(value, 'accentHex');
  const hasDefaultPreset = record(value) && Object.hasOwn(value, 'defaultCyclePresetId');
  const hasDefaultConfiguration = record(value) && Object.hasOwn(value, 'defaultCyclePresetConfiguration');
  const shapes = [base, [...base, 'accentHex'], [...base, ...defaultFields],
    [...base, ...defaultFields, 'defaultCyclePresetConfiguration'], [...base, 'accentHex', ...defaultFields],
    [...base, 'accentHex', ...defaultFields, 'defaultCyclePresetConfiguration']];
  if (!record(value) || !shapes.some(keys => exact(value, keys)) || !guid(value.id) || !name(value.name)) return false;
  if (hasAccent && !(value.accentHex === null || typeof value.accentHex === 'string' && /^#[0-9a-f]{6}$/i.test(value.accentHex))) return false;
  if (hasDefaultPreset) {
    if (value.defaultCyclePresetId === null) {
      if (value.defaultCyclePresetName !== null || hasDefaultConfiguration && value.defaultCyclePresetConfiguration !== null) return false;
    } else if (typeof value.defaultCyclePresetId !== 'string' || !guid(value.defaultCyclePresetId)
      || !name(value.defaultCyclePresetName)
      || hasDefaultConfiguration && !(value.defaultCyclePresetConfiguration === null || isCycleConfiguration(value.defaultCyclePresetConfiguration))) return false;
  }
  const counts = [value.applicationRuleCount, value.websiteRuleCount, value.blockedApplicationCount, value.blockedWebsiteCount];
  return counts.every(count => Number.isSafeInteger(count) && (count as number) >= 0 && (count as number) <= 10000)
    && (value.blockedApplicationCount as number) <= (value.applicationRuleCount as number)
    && (value.blockedWebsiteCount as number) <= (value.websiteRuleCount as number);
}

export function isSessionReplayIntent(value: unknown): value is SessionReplayIntent {
  if (!record(value) || !exact(value, ['sessionId', 'available', 'replayable', 'unavailableReason', 'originalProfileName', 'profile',
    'durationMinutes', 'mode', 'strict', 'enforcementEnabled', 'cyclePresetId', 'cycleConfiguration', 'objective'])
    || !guid(value.sessionId) || typeof value.available !== 'boolean' || typeof value.replayable !== 'boolean'
    || !(value.unavailableReason === null || typeof value.unavailableReason === 'string' && ['IntentUnavailable', 'MissingProfile', 'StrictReviewRequired', 'EnforcementConsentUnknown', 'CycleConfigurationUnavailable'].includes(value.unavailableReason))) return false;
  if (!value.available) return !value.replayable && value.unavailableReason === 'IntentUnavailable'
    && value.originalProfileName === null && value.profile === null && value.durationMinutes === null && value.mode === null
    && value.strict === null && value.enforcementEnabled === null && value.cyclePresetId === null && value.cycleConfiguration === null
    && value.objective === null;
  if (!(value.originalProfileName === null || name(value.originalProfileName))
    || !(value.profile === null || replayProfile(value.profile))
    || !(value.durationMinutes === null || Number.isSafeInteger(value.durationMinutes) && (value.durationMinutes as number) >= 1 && (value.durationMinutes as number) <= 720)
    || !(value.mode === null || value.mode === 'SimpleTimer' || value.mode === 'FocusCycle')
    || !(value.strict === null || typeof value.strict === 'boolean')
    || !(value.enforcementEnabled === null || typeof value.enforcementEnabled === 'boolean')
    || !(value.cyclePresetId === null || guid(value.cyclePresetId))
    || !(value.cycleConfiguration === null || isCycleConfiguration(value.cycleConfiguration))
    || !(value.objective === null || typeof value.objective === 'string' && value.objective.length <= 240)) return false;
  const complete = value.profile !== null && value.durationMinutes !== null && value.mode !== null && value.strict !== null
    && value.enforcementEnabled !== null && value.objective !== null && (value.mode === 'SimpleTimer'
      ? value.cyclePresetId === null && value.cycleConfiguration === null
      : value.cycleConfiguration !== null);
  return !value.replayable || complete && value.unavailableReason === null;
}
