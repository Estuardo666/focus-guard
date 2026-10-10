import { isSessionReplayIntent } from './replay';

const base = {
  sessionId: '11111111-1111-4111-8111-111111111111', available: true, replayable: true, unavailableReason: null,
  originalProfileName: 'Old name',
  profile: { id: '22222222-2222-4222-8222-222222222222', name: 'Current name', applicationRuleCount: 2,
    websiteRuleCount: 1, blockedApplicationCount: 1, blockedWebsiteCount: 1, accentHex: '#8AC8AC',
    defaultCyclePresetId: 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a01', defaultCyclePresetName: 'Pomodoro clásico',
    defaultCyclePresetConfiguration: { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4,
      terminationMode: 'CycleCount', cycleCount: 4, totalDurationSeconds: null } },
  durationMinutes: 25, mode: 'SimpleTimer', strict: true, enforcementEnabled: false,
  cyclePresetId: null, cycleConfiguration: null, objective: 'Read chapter four',
};

describe('session replay intent validation', () => {
  it('accepts a strict replay intent when all saved values are complete', () => {
    expect(isSessionReplayIntent(base)).toBe(true);
  });

  it('rejects a replayable strict intent without known consent or a complete profile', () => {
    expect(isSessionReplayIntent({ ...base, enforcementEnabled: null })).toBe(false);
    expect(isSessionReplayIntent({ ...base, profile: null })).toBe(false);
    expect(isSessionReplayIntent({ ...base, unavailableReason: 'StrictReviewRequired' })).toBe(false);
    expect(isSessionReplayIntent({ ...base, objective: 'o'.repeat(241) })).toBe(false);
    expect(isSessionReplayIntent({ ...base, profile: { ...base.profile, accentHex: '#ABC' } })).toBe(false);
  });
});
