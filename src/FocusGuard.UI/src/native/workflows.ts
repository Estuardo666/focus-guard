import { utcMilliseconds } from './protocol';

export type TodaySpan = Readonly<{ sessionId: string; profileName: string; kind: 'Focus' | 'ShortBreak' | 'LongBreak'; startUtc: string; endUtc: string; completed: boolean }>;
export type TodaySummary = Readonly<{ focusSeconds: number; completedSessions: number; timeline: readonly TodaySpan[]; timelineTruncated: boolean }>;
export type ScheduleSummary = Readonly<{ id: string; name: string; days: number; startMinute: number; timeZoneId: string; timeZoneIanaId?: string | null; enabled: boolean; profileName: string; durationMinutes: number; enforcementEnabled: boolean; nextStartUtc: string | null; lastOccurrenceUtc: string | null; lastResult: string; profileId?: string | null; applicationRuleCount?: number; websiteRuleCount?: number; blockedApplicationRuleCount?: number; blockedWebsiteRuleCount?: number; revision?: string | null; nextEndUtc?: string | null }>;
export type ScheduleList = Readonly<{ items: readonly ScheduleSummary[]; editingEnabled?: boolean; timeZoneId?: string }>;
const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(key => Object.hasOwn(v, key));
const count = (v: unknown, max = 10000): v is number => Number.isSafeInteger(v) && (v as number) >= 0 && (v as number) <= max;
const guid = (v: unknown): v is string => typeof v === 'string' && /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
const name = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0 && v.length <= 80;
const date = (v: unknown) => v === null || utcMilliseconds(v) !== null;

export function isTodaySummary(v: unknown): v is TodaySummary {
  if (!record(v) || !exact(v, ['focusSeconds', 'completedSessions', 'timeline', 'timelineTruncated']) || !count(v.focusSeconds, 172800) || !count(v.completedSessions) || typeof v.timelineTruncated !== 'boolean' || !Array.isArray(v.timeline) || v.timeline.length > 128 || v.timelineTruncated && v.timeline.length !== 128) return false;
  let previousStart = -Infinity;
  for (const span of v.timeline) {
    if (!record(span) || !exact(span, ['sessionId', 'profileName', 'kind', 'startUtc', 'endUtc', 'completed']) || !guid(span.sessionId) || !name(span.profileName)
      || !['Focus', 'ShortBreak', 'LongBreak'].includes(String(span.kind)) || typeof span.completed !== 'boolean') return false;
    const start = utcMilliseconds(span.startUtc), end = utcMilliseconds(span.endUtc);
    if (start === null || end === null || end <= start || start < previousStart) return false;
    previousStart = start;
  }
  return true;
}

const scheduleBaseKeys = ['id', 'name', 'days', 'startMinute', 'timeZoneId', 'enabled', 'profileName', 'durationMinutes', 'enforcementEnabled', 'nextStartUtc', 'lastOccurrenceUtc', 'lastResult'];
const scheduleExtendedKeys = [...scheduleBaseKeys, 'profileId', 'applicationRuleCount', 'websiteRuleCount', 'blockedApplicationRuleCount', 'blockedWebsiteRuleCount', 'revision'];
const optionalScheduleKeys = ['nextEndUtc', 'timeZoneIanaId'];
const exactScheduleShape = (item: Record<string, unknown>) => {
  const optional = optionalScheduleKeys.filter(key => Object.hasOwn(item, key));
  return exact(item, [...scheduleBaseKeys, ...optional]) || exact(item, [...scheduleExtendedKeys, ...optional]);
};
const validNextEnd = (item: Record<string, unknown>) => {
  if (!Object.hasOwn(item, 'nextEndUtc')) return true;
  if (!date(item.nextEndUtc)) return false;
  if (item.nextEndUtc === null) return item.nextStartUtc === null;
  const start = utcMilliseconds(item.nextStartUtc), end = utcMilliseconds(item.nextEndUtc);
  return start !== null && end !== null && end > start;
};
const validTimeZoneIanaId = (value: unknown) => {
  if (value === null) return true;
  if (typeof value !== 'string' || value.length > 100) return false;
  try { new Intl.DateTimeFormat('en-US', { timeZone: value }); return true; } catch { return false; }
};

export function isScheduleList(v: unknown): v is ScheduleList {
  if (!record(v) || !(exact(v, ['items']) || exact(v, ['items', 'editingEnabled', 'timeZoneId'])) || !Array.isArray(v.items) || v.items.length > 12
    || Object.hasOwn(v, 'editingEnabled') && typeof v.editingEnabled !== 'boolean'
    || Object.hasOwn(v, 'timeZoneId') && (typeof v.timeZoneId !== 'string' || !v.timeZoneId.length || v.timeZoneId.length > 100)) return false;
  const ids = new Set<string>();
  const itemsValid = v.items.every(item => record(item) && exactScheduleShape(item)
    && guid(item.id) && !ids.has(item.id.toLowerCase()) && !!ids.add(item.id.toLowerCase()) && name(item.name) && count(item.days, 127) && item.days > 0
    && count(item.startMinute, 1439) && typeof item.timeZoneId === 'string' && item.timeZoneId.length > 0 && item.timeZoneId.length <= 128
    && typeof item.enabled === 'boolean' && name(item.profileName) && count(item.durationMinutes, 720) && item.durationMinutes >= 1
    && typeof item.enforcementEnabled === 'boolean' && date(item.nextStartUtc) && date(item.lastOccurrenceUtc)
    && typeof item.lastResult === 'string' && item.lastResult.length <= 32 && validNextEnd(item)
    && (!Object.hasOwn(item, 'profileId') || item.profileId === null || guid(item.profileId))
    && (!Object.hasOwn(item, 'applicationRuleCount') || count(item.applicationRuleCount))
    && (!Object.hasOwn(item, 'websiteRuleCount') || count(item.websiteRuleCount))
    && (!Object.hasOwn(item, 'blockedApplicationRuleCount') || count(item.blockedApplicationRuleCount) && count(item.applicationRuleCount) && item.blockedApplicationRuleCount <= item.applicationRuleCount)
    && (!Object.hasOwn(item, 'blockedWebsiteRuleCount') || count(item.blockedWebsiteRuleCount) && count(item.websiteRuleCount) && item.blockedWebsiteRuleCount <= item.websiteRuleCount)
    && (!Object.hasOwn(item, 'timeZoneIanaId') || validTimeZoneIanaId(item.timeZoneIanaId))
    && (!Object.hasOwn(item, 'revision') || item.revision === null || typeof item.revision === 'string' && /^[0-9A-F]{64}$/.test(item.revision)));
  return itemsValid && (v.editingEnabled !== true || v.items.every(item => record(item) && typeof item.revision === 'string' && typeof item.profileId === 'string'));
}
