import { isSessionReflectionSaved, isSessionSummary } from './history';

const summary = {
  sessionId: '11111111-1111-4111-8111-111111111111', outcome: 'Completed', profileName: 'Focus',
  plannedMinutes: 25, focusedMinutes: 25, blockedAttempts: 2, streakDays: 1,
  endedAtUtc: '2026-10-06T18:00:00Z', objective: 'Read chapter four', reflection: 'Finished the draft.',
  captures: [], capturesTruncated: false,
};

describe('local session journal summary fields', () => {
  it('accepts bounded objective and reflection text in the session summary', () => {
    expect(isSessionSummary(summary)).toBe(true);
    expect(isSessionSummary({ ...summary, objective: 'o'.repeat(241) })).toBe(false);
    expect(isSessionSummary({ ...summary, reflection: 'r'.repeat(1001) })).toBe(false);
    expect(isSessionSummary({ ...summary, other: 'unrecognized' })).toBe(false);
  });

  it('accepts known cycle metrics while preserving legacy summaries without them', () => {
    expect(isSessionSummary(summary)).toBe(true);
    expect(isSessionSummary({ ...summary, cycleMetrics: null })).toBe(true);
    expect(isSessionSummary({ ...summary, cycleMetrics: { completedFocusCycles: 4, completedBreaks: 3 } })).toBe(true);
    expect(isSessionSummary({ ...summary, cycleMetrics: { completedFocusCycles: -1, completedBreaks: 3 } })).toBe(false);
    expect(isSessionSummary({ ...summary, cycleMetrics: { completedFocusCycles: 4, completedBreaks: 25 } })).toBe(false);
  });

  it('validates a reflection-save acknowledgment against its session', () => {
    expect(isSessionReflectionSaved({ sessionId: summary.sessionId, reflection: 'Saved note' })).toBe(true);
    expect(isSessionReflectionSaved({ sessionId: summary.sessionId, reflection: 'r'.repeat(1001) })).toBe(false);
    expect(isSessionReflectionSaved({ sessionId: 'bad', reflection: '' })).toBe(false);
  });
});
