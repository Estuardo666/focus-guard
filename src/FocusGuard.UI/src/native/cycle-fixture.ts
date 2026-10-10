import type { CycleReview } from './cycles';

export const configuration = { focusSeconds: 60, shortBreakSeconds: 60, longBreakSeconds: 120, cyclesBeforeLongBreak: 2, terminationMode: 'TotalDuration' as const, cycleCount: null, totalDurationSeconds: 150 };
export const cycleReview: CycleReview = { preparationId: '33333333333343338333333333333333', profileId: '22222222-2222-4222-8222-222222222222', profileName: 'Reviewed cycle profile', presetName: null, configuration, enforcementEnabled: false, applicationRuleCount: 1, websiteRuleCount: 1, breakApplicationRuleCount: 0, breakWebsiteRuleCount: 0, breakApplications: [], breakWebsites: [], rulesTruncated: false, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:02:30Z', expiresUtc: '2040-01-01T12:02:00Z', phases: [
  { index: 0, kind: 'Focus', focusCycleNumber: 1, startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:01:00Z' },
  { index: 1, kind: 'ShortBreak', focusCycleNumber: 1, startUtc: '2040-01-01T12:01:00Z', endUtc: '2040-01-01T12:02:00Z' },
  { index: 2, kind: 'Focus', focusCycleNumber: 2, startUtc: '2040-01-01T12:02:00Z', endUtc: '2040-01-01T12:02:30Z' },
], partialFinalPhase: true, extensionReady: false };

