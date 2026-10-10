import { isCycleConfiguration, isCycleInput, isCyclePage, isCycleReview } from './cycles';
import { configuration, cycleReview } from './cycle-fixture';
it('accepts a finite custom plan with an authoritative truncated final phase', () => {
  expect(isCycleConfiguration(configuration)).toBe(true);
  expect(isCycleInput({ presetId: null, configuration })).toBe(true);
  expect(isCycleReview(cycleReview)).toBe(true);
});
it('requires bounded break-exception labels to match their counts unless the Host marks truncation', () => {
  const withRules = { ...cycleReview, breakApplicationRuleCount: 1, breakWebsiteRuleCount: 1,
    breakApplications: ['game.exe'], breakWebsites: ['games.example'] };
  expect(isCycleReview(withRules)).toBe(true);
  expect(isCycleReview({ ...withRules, breakApplications: [] })).toBe(false);
  expect(isCycleReview({ ...withRules, breakApplications: Array(13).fill('game.exe'), rulesTruncated: true })).toBe(false);
  expect(isCycleReview({ ...withRules, rulesTruncated: true, breakApplications: ['game.exe'] })).toBe(true);
});
it('rejects conflicting termination inputs, strict injection, malformed dates and phase gaps', () => {
  expect(isCycleConfiguration({ ...configuration, cycleCount: 4 })).toBe(false);
  expect(isCycleConfiguration({ ...configuration, strictMode: true })).toBe(false);
  expect(isCycleConfiguration({ ...configuration, focusSeconds: 59 })).toBe(false);
  expect(isCycleInput({ presetId: cycleReview.profileId, configuration })).toBe(false);
  expect(isCycleReview({ ...cycleReview, partialFinalPhase: false })).toBe(false);
  expect(isCycleReview({ ...cycleReview, endUtc: '2040-02-30T12:00:00Z' })).toBe(false);
  expect(isCycleReview({ ...cycleReview, expiresUtc: '2040-01-01T12:02:31Z' })).toBe(false);
  expect(isCycleReview({ ...cycleReview, phases: cycleReview.phases.map((phase, i) => i === 1 ? { ...phase, startUtc: '2040-01-01T12:01:01Z' } : phase) })).toBe(false);
  expect(isCycleReview({ ...cycleReview, phases: cycleReview.phases.map((phase, i) => i === 1 ? { ...phase, kind: 'LongBreak' } : phase) })).toBe(false);
});
it('bounds and validates saved preset pages without accepting duplicate identities', () => {
  const item = { id: cycleReview.profileId, name: 'Saved cycle', isBuiltIn: false, configuration };
  expect(isCyclePage({ items: [item], offset: 0, total: 1 })).toBe(true);
  expect(isCyclePage({ items: [item, item], offset: 0, total: 2 })).toBe(false);
  expect(isCyclePage({ items: [item], offset: 1, total: 1 })).toBe(false);
});
