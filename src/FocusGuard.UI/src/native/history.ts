import { utcMilliseconds } from './protocol';
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(key => Object.hasOwn(v, key));
const count = (v: unknown, maximum: number): v is number => Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= maximum;
const guid = (v: unknown): v is string => typeof v === 'string' && /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
export type CapturedItem = Readonly<{ id: string; text: string; sourceKind: 'Website' | 'Application'; sourceLabel: string; capturedAtUtc: string; resolution: 'Pending'; openable: boolean }>;
export type CycleHistoryMetrics = Readonly<{ completedFocusCycles: number; completedBreaks: number }>;
export type SessionSummary = Readonly<{ sessionId: string; outcome: 'Completed' | 'Stopped'; profileName: string; plannedMinutes: number; focusedMinutes: number; blockedAttempts: number; streakDays: number; endedAtUtc: string; objective: string; reflection: string; captures: readonly CapturedItem[]; capturesTruncated: boolean; cycleMetrics?: CycleHistoryMetrics | null }>;
export type CaptureActionResult = Readonly<{ sessionId: string; captureId: string; action: 'Opened' | 'Dismissed' }>;
export type SessionReflectionSaved = Readonly<{ sessionId: string; reflection: string }>;
export function isSessionSummary(v: unknown): v is SessionSummary {
  const fields = ['sessionId', 'outcome', 'profileName', 'plannedMinutes', 'focusedMinutes', 'blockedAttempts', 'streakDays', 'endedAtUtc', 'objective', 'reflection', 'captures', 'capturesTruncated'];
  if (!record(v) || !(exact(v, fields) || exact(v, [...fields, 'cycleMetrics']))
    || !guid(v.sessionId) || !['Completed', 'Stopped'].includes(String(v.outcome)) || typeof v.profileName !== 'string' || !v.profileName.trim() || v.profileName.length > 80
    || !count(v.plannedMinutes, 720) || v.plannedMinutes < 1 || !count(v.focusedMinutes, 43200) || !count(v.blockedAttempts, 100000) || !count(v.streakDays, 100000)
    || utcMilliseconds(v.endedAtUtc) === null || typeof v.objective !== 'string' || v.objective.length > 240
    || typeof v.reflection !== 'string' || v.reflection.length > 1000
    || typeof v.capturesTruncated !== 'boolean' || !Array.isArray(v.captures) || v.captures.length > 24) return false;
  if (Object.hasOwn(v, 'cycleMetrics') && v.cycleMetrics !== null
    && (!record(v.cycleMetrics) || !exact(v.cycleMetrics, ['completedFocusCycles', 'completedBreaks'])
      || !count(v.cycleMetrics.completedFocusCycles, 24) || !count(v.cycleMetrics.completedBreaks, 24))) return false;
  const ids = new Set<string>();
  return v.captures.every(item => record(item) && exact(item, ['id', 'text', 'sourceKind', 'sourceLabel', 'capturedAtUtc', 'resolution', 'openable'])
    && guid(item.id) && !ids.has(item.id.toLowerCase()) && !!ids.add(item.id.toLowerCase())
    && typeof item.text === 'string' && item.text.trim().length > 0 && item.text.length <= 240
    && (item.sourceKind === 'Website' || item.sourceKind === 'Application') && typeof item.sourceLabel === 'string' && item.sourceLabel.length > 0 && item.sourceLabel.length <= 1024
    && utcMilliseconds(item.capturedAtUtc) !== null && item.resolution === 'Pending' && typeof item.openable === 'boolean');
}
export function isCaptureActionResult(v: unknown): v is CaptureActionResult {
  return record(v) && exact(v, ['sessionId', 'captureId', 'action']) && guid(v.sessionId) && guid(v.captureId) && (v.action === 'Opened' || v.action === 'Dismissed');
}
export function isSessionReflectionSaved(v: unknown): v is SessionReflectionSaved {
  return record(v) && exact(v, ['sessionId', 'reflection']) && guid(v.sessionId)
    && typeof v.reflection === 'string' && v.reflection.length <= 1000;
}
