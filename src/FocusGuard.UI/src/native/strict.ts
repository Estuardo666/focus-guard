import { MAXIMUM_REMAINING_SECONDS, utcMilliseconds } from './protocol';
import { isCycleConfiguration, isCycleReview, type CycleConfiguration, type CyclePhase } from './cycles';

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) =>
  Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const guidN = (value: unknown): value is string => typeof value === 'string'
  && /^(?!0{32})[a-f0-9]{32}$/i.test(value);
const guidD = (value: unknown): value is string => typeof value === 'string'
  && /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
const name = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= 80;
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0 && (value as number) <= 10000;
const labels = (value: unknown): value is readonly string[] => Array.isArray(value) && value.length <= 12
  && value.every(item => typeof item === 'string' && item.length > 0 && item.length <= 512);

export type StrictReview = Readonly<{
  preparationId: string; profileId: string; profileName: string; mode: 'SimpleTimer' | 'FocusCycle'; strict: true;
  durationSeconds: number; enforcementEnabled: boolean; applicationRuleCount: number; websiteRuleCount: number;
  blockedApplicationCount: number; blockedWebsiteCount: number; blockedApplications: readonly string[];
  blockedWebsites: readonly string[]; breakApplicationRuleCount: number; breakWebsiteRuleCount: number;
  breakApplications: readonly string[]; breakWebsites: readonly string[]; rulesTruncated: boolean;
  presetName: string | null; presetId: string | null; configuration: CycleConfiguration | null;
  startUtc: string; endUtc: string; expiresUtc: string; phases: readonly CyclePhase[];
  partialFinalPhase: boolean; extensionReady: boolean; recoveryDelaySeconds: 900;
  recoveryCode: string; confirmationText: string; recoveryHelperPath: string; recoveryHelperAvailable: boolean;
}>;

export type RecoveryInfo = Readonly<{ executablePath: string; available: boolean; recoveryDelaySeconds: 900 }>;

export function isRecoveryInfo(value: unknown): value is RecoveryInfo {
  return record(value) && exact(value, ['executablePath', 'available', 'recoveryDelaySeconds'])
    && typeof value.executablePath === 'string' && value.executablePath.length <= 260
    && /^[A-Za-z]:[\\/]/.test(value.executablePath) && /FocusGuard\.Recovery\.exe$/i.test(value.executablePath)
    && typeof value.available === 'boolean' && value.recoveryDelaySeconds === 900;
}

export function isStrictReview(value: unknown): value is StrictReview {
  if (!record(value) || !exact(value, ['preparationId', 'profileId', 'profileName', 'mode', 'strict', 'durationSeconds',
    'enforcementEnabled', 'applicationRuleCount', 'websiteRuleCount', 'blockedApplicationCount', 'blockedWebsiteCount',
    'blockedApplications', 'blockedWebsites', 'breakApplicationRuleCount', 'breakWebsiteRuleCount', 'breakApplications',
    'breakWebsites', 'rulesTruncated', 'presetName', 'presetId', 'configuration', 'startUtc', 'endUtc', 'expiresUtc',
    'phases', 'partialFinalPhase', 'extensionReady', 'recoveryDelaySeconds', 'recoveryCode', 'confirmationText',
    'recoveryHelperPath', 'recoveryHelperAvailable'])
    || !guidN(value.preparationId) || !guidD(value.profileId) || !name(value.profileName)
    || !(value.mode === 'SimpleTimer' || value.mode === 'FocusCycle') || value.strict !== true
    || !Number.isSafeInteger(value.durationSeconds) || (value.durationSeconds as number) < 60
    || (value.durationSeconds as number) > MAXIMUM_REMAINING_SECONDS || typeof value.enforcementEnabled !== 'boolean'
    || !count(value.applicationRuleCount) || !count(value.websiteRuleCount) || !count(value.blockedApplicationCount)
    || !count(value.blockedWebsiteCount) || (value.blockedApplicationCount as number) > (value.applicationRuleCount as number)
    || (value.blockedWebsiteCount as number) > (value.websiteRuleCount as number)
    || !labels(value.blockedApplications) || !labels(value.blockedWebsites)
    || !count(value.breakApplicationRuleCount) || !count(value.breakWebsiteRuleCount)
    || !labels(value.breakApplications) || !labels(value.breakWebsites)
    || typeof value.rulesTruncated !== 'boolean' || !(value.presetName === null || name(value.presetName))
    || !(value.presetId === null || guidD(value.presetId))
    || typeof value.partialFinalPhase !== 'boolean' || typeof value.extensionReady !== 'boolean'
    || value.recoveryDelaySeconds !== 900 || typeof value.recoveryCode !== 'string'
    || !/^(?:[A-F0-9]{5}-){7}[A-F0-9]{5}$/.test(value.recoveryCode)
    || value.confirmationText !== value.profileName || typeof value.recoveryHelperPath !== 'string'
    || value.recoveryHelperPath.length > 260 || !/^[A-Za-z]:[\\/]/.test(value.recoveryHelperPath)
    || !/FocusGuard\.Recovery\.exe$/i.test(value.recoveryHelperPath) || typeof value.recoveryHelperAvailable !== 'boolean') return false;

  if (!Array.isArray(value.phases)) return false;
  const start = utcMilliseconds(value.startUtc), end = utcMilliseconds(value.endUtc), expiry = utcMilliseconds(value.expiresUtc);
  if (start === null || end === null || expiry === null || end <= start || expiry <= start || expiry > end
    || expiry - start > 120000 || end - start !== (value.durationSeconds as number) * 1000) return false;

  if (value.mode === 'SimpleTimer') return (value.durationSeconds as number) <= 43200 && value.configuration === null
    && value.presetName === null && value.presetId === null && value.phases.length === 0
    && value.breakApplicationRuleCount === 0 && value.breakWebsiteRuleCount === 0
    && value.breakApplications.length === 0 && value.breakWebsites.length === 0 && value.partialFinalPhase === false;

  if (!isCycleConfiguration(value.configuration)
    || (value.presetName === null) !== (value.presetId === null)) return false;
  return isCycleReview({
    preparationId: value.preparationId,
    profileId: value.profileId,
    profileName: value.profileName,
    presetName: value.presetName,
    configuration: value.configuration,
    enforcementEnabled: value.enforcementEnabled,
    applicationRuleCount: value.applicationRuleCount,
    websiteRuleCount: value.websiteRuleCount,
    breakApplicationRuleCount: value.breakApplicationRuleCount,
    breakWebsiteRuleCount: value.breakWebsiteRuleCount,
    breakApplications: value.breakApplications,
    breakWebsites: value.breakWebsites,
    rulesTruncated: value.rulesTruncated,
    startUtc: value.startUtc,
    endUtc: value.endUtc,
    expiresUtc: value.expiresUtc,
    phases: value.phases,
    partialFinalPhase: value.partialFinalPhase,
    extensionReady: value.extensionReady,
  });
}
