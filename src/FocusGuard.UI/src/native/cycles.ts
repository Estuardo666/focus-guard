import { utcMilliseconds } from './protocol';

export type CycleConfiguration = Readonly<{ focusSeconds: number; shortBreakSeconds: number; longBreakSeconds: number; cyclesBeforeLongBreak: number; terminationMode: 'CycleCount' | 'TotalDuration'; cycleCount: number | null; totalDurationSeconds: number | null }>;
export type CyclePreset = Readonly<{ id: string; name: string; isBuiltIn: boolean; configuration: CycleConfiguration; revision?: string }>;
export type CyclePage = Readonly<{ items: readonly CyclePreset[]; offset: number; total: number; editingEnabled?: boolean }>;
export type CyclePresetMutation = Readonly<{ id: string | null; name: string | null; isBuiltIn: boolean | null; configuration: CycleConfiguration | null; revision: string | null; action: 'Saved' | 'Duplicated' | 'Deleted' }>;
export type CyclePhase = Readonly<{ index: number; kind: 'Focus' | 'ShortBreak' | 'LongBreak'; focusCycleNumber: number; startUtc: string; endUtc: string }>;
export type CycleReview = Readonly<{ preparationId: string; profileId: string; profileName: string; presetName: string | null; configuration: CycleConfiguration; enforcementEnabled: boolean; applicationRuleCount: number; websiteRuleCount: number; breakApplicationRuleCount: number; breakWebsiteRuleCount: number; breakApplications: readonly string[]; breakWebsites: readonly string[]; rulesTruncated: boolean; startUtc: string; endUtc: string; expiresUtc: string; phases: readonly CyclePhase[]; partialFinalPhase: boolean; extensionReady: boolean }>;
export type CycleInput = Readonly<{ presetId: string | null; configuration: CycleConfiguration | null }>;
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(key => Object.hasOwn(v, key));
const integer = (v: unknown, min: number, max: number): v is number => Number.isSafeInteger(v) && (v as number) >= min && (v as number) <= max;
const name = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 80;
const guid = (v: unknown, dashed = true): v is string => typeof v === 'string' && (dashed ? /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i : /^(?!0{32})[a-f0-9]{32}$/i).test(v);
export function isCycleConfiguration(v: unknown): v is CycleConfiguration {
  return record(v) && exact(v, ['focusSeconds', 'shortBreakSeconds', 'longBreakSeconds', 'cyclesBeforeLongBreak', 'terminationMode', 'cycleCount', 'totalDurationSeconds'])
    && integer(v.focusSeconds, 60, 10800) && integer(v.shortBreakSeconds, 60, 3600) && integer(v.longBreakSeconds, 60, 7200) && integer(v.cyclesBeforeLongBreak, 2, 12)
    && (v.terminationMode === 'CycleCount' ? integer(v.cycleCount, 1, 24) && v.totalDurationSeconds === null : v.terminationMode === 'TotalDuration' && integer(v.totalDurationSeconds, 60, 2592000) && v.cycleCount === null);
}
export function isCycleInput(v: CycleInput): boolean {
  return v.presetId === null ? isCycleConfiguration(v.configuration) : guid(v.presetId) && v.configuration === null;
}
export function sameCycleConfiguration(a: CycleConfiguration, b: CycleConfiguration): boolean {
  return (Object.keys(a) as (keyof CycleConfiguration)[]).every(key => a[key] === b[key]);
}
export function isCyclePage(v: unknown): v is CyclePage {
  if (!record(v) || !(exact(v, ['items', 'offset', 'total']) || exact(v, ['items', 'offset', 'total', 'editingEnabled'])) || !integer(v.offset, 0, 10000) || !integer(v.total, 0, 10000) || Object.hasOwn(v, 'editingEnabled') && typeof v.editingEnabled !== 'boolean' || !Array.isArray(v.items) || v.items.length > 8 || v.items.length > Math.max(0, v.total - v.offset)) return false;
  const ids = new Set<string>();
  const itemsValid = v.items.every(item => record(item) && (exact(item, ['id', 'name', 'isBuiltIn', 'configuration']) || exact(item, ['id', 'name', 'isBuiltIn', 'configuration', 'revision'])) && guid(item.id) && !ids.has(item.id.toLowerCase()) && !!ids.add(item.id.toLowerCase()) && name(item.name) && typeof item.isBuiltIn === 'boolean' && isCycleConfiguration(item.configuration) && (!Object.hasOwn(item, 'revision') || typeof item.revision === 'string' && /^[0-9A-F]{64}$/.test(item.revision)));
  return itemsValid && (v.editingEnabled !== true || v.items.every(item => record(item) && typeof item.revision === 'string'));
}
export function isCyclePresetMutation(v: unknown): v is CyclePresetMutation {
  if (!record(v) || !exact(v, ['id', 'name', 'isBuiltIn', 'configuration', 'revision', 'action']) || !['Saved', 'Duplicated', 'Deleted'].includes(String(v.action))) return false;
  if (v.action === 'Deleted') return v.id === null && v.name === null && v.isBuiltIn === null && v.configuration === null && v.revision === null;
  return guid(v.id) && name(v.name) && v.isBuiltIn === false && isCycleConfiguration(v.configuration) && typeof v.revision === 'string' && /^[0-9A-F]{64}$/.test(v.revision);
}
export function isCycleReview(v: unknown): v is CycleReview {
  if (!record(v) || !exact(v, ['preparationId', 'profileId', 'profileName', 'presetName', 'configuration', 'enforcementEnabled', 'applicationRuleCount', 'websiteRuleCount', 'breakApplicationRuleCount', 'breakWebsiteRuleCount', 'breakApplications', 'breakWebsites', 'rulesTruncated', 'startUtc', 'endUtc', 'expiresUtc', 'phases', 'partialFinalPhase', 'extensionReady'])
    || !guid(v.preparationId, false) || !guid(v.profileId) || !name(v.profileName) || !(v.presetName === null || name(v.presetName)) || !isCycleConfiguration(v.configuration)
    || typeof v.enforcementEnabled !== 'boolean' || typeof v.partialFinalPhase !== 'boolean' || typeof v.extensionReady !== 'boolean' || typeof v.rulesTruncated !== 'boolean'
    || !['applicationRuleCount', 'websiteRuleCount', 'breakApplicationRuleCount', 'breakWebsiteRuleCount'].every(key => integer(v[key], 0, 10000))
    || !Array.isArray(v.breakApplications) || v.breakApplications.length > 12 || !v.breakApplications.every(item => typeof item === 'string' && item.trim().length > 0 && item.length <= 512)
    || !Array.isArray(v.breakWebsites) || v.breakWebsites.length > 12 || !v.breakWebsites.every(item => typeof item === 'string' && item.trim().length > 0 && item.length <= 512)
    || !v.rulesTruncated && (v.breakApplications.length !== v.breakApplicationRuleCount || v.breakWebsites.length !== v.breakWebsiteRuleCount)
    || !Array.isArray(v.phases) || v.phases.length < 1 || v.phases.length > 49) return false;
  const start = utcMilliseconds(v.startUtc), end = utcMilliseconds(v.endUtc), expiry = utcMilliseconds(v.expiresUtc);
  if (start === null || end === null || expiry === null || end <= start || expiry <= start || expiry > end || expiry - start > 120000) return false;
  const config = v.configuration;
  if (config.terminationMode === 'CycleCount' && v.phases.length !== config.cycleCount! * 2 - 1 || config.terminationMode === 'TotalDuration' && end - start !== config.totalDurationSeconds! * 1000) return false;
  let cursor = start, partial = false;
  for (let i = 0; i < v.phases.length; i++) {
    const phase: unknown = v.phases[i]; const cycle = Math.floor(i / 2) + 1;
    const kind = i % 2 === 0 ? 'Focus' : cycle % config.cyclesBeforeLongBreak === 0 ? 'LongBreak' : 'ShortBreak';
    const duration = (kind === 'Focus' ? config.focusSeconds : kind === 'LongBreak' ? config.longBreakSeconds : config.shortBreakSeconds) * 1000;
    if (!record(phase) || !exact(phase, ['index', 'kind', 'focusCycleNumber', 'startUtc', 'endUtc']) || phase.index !== i || phase.kind !== kind || phase.focusCycleNumber !== cycle || utcMilliseconds(phase.startUtc) !== cursor) return false;
    const phaseEnd = utcMilliseconds(phase.endUtc);
    if (phaseEnd === null || phaseEnd <= cursor || phaseEnd > end || phaseEnd - cursor > duration) return false;
    partial = phaseEnd - cursor < duration;
    if (kind === 'Focus' && cycle > 24 && !partial) return false;
    if (partial && (i !== v.phases.length - 1 || config.terminationMode !== 'TotalDuration')) return false;
    cursor = phaseEnd;
  }
  return cursor === end && partial === v.partialFinalPhase;
}
