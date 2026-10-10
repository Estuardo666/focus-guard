import { isScheduleList } from './workflows';

const baseSchedule = {
  id: '33333333-3333-4333-8333-333333333333', name: 'Morning', days: 62, startMinute: 600,
  timeZoneId: 'SA Pacific Standard Time', timeZoneIanaId: 'America/Bogota', enabled: true, profileName: 'Saved profile', durationMinutes: 1,
  enforcementEnabled: false, nextStartUtc: '2026-10-02T10:00:00Z', lastOccurrenceUtc: null, lastResult: '',
  profileId: '22222222-2222-4222-8222-222222222222', applicationRuleCount: 0, websiteRuleCount: 0,
  blockedApplicationRuleCount: 0, blockedWebsiteRuleCount: 0, revision: 'A'.repeat(64)
};

it('accepts legacy schedule summaries and service-calculated end times', () => {
  const legacySchedule: Record<string, unknown> = { ...baseSchedule };
  delete legacySchedule.timeZoneIanaId;
  const list = { items: [legacySchedule], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' };
  expect(isScheduleList(list)).toBe(true);
  expect(isScheduleList({ ...list, items: [{ ...baseSchedule, nextEndUtc: '2026-10-02T10:01:00Z' }] })).toBe(true);
});

it('rejects a scheduled end that does not follow the reported start', () => {
  expect(isScheduleList({ items: [{ ...baseSchedule, nextEndUtc: '2026-10-02T09:59:00Z' }], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' })).toBe(false);
  expect(isScheduleList({ items: [{ ...baseSchedule, nextEndUtc: null }], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' })).toBe(false);
  expect(isScheduleList({ items: [{ ...baseSchedule, timeZoneIanaId: 'Missing/Zone' }], editingEnabled: true, timeZoneId: 'SA Pacific Standard Time' })).toBe(false);
});
