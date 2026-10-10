import { parseBridgeResponse } from './protocol';

const phase = { index: 1, kind: 'ShortBreak', focusCycleNumber: 1, remainingSeconds: 60, endUtc: '2040-01-01T12:02:00Z', startUtc: '2040-01-01T12:01:00Z' };
const status = { serviceReady: true, extensionReady: true, sessionState: 'Active', remainingSeconds: 180, sessionId: '11111111-1111-4111-8111-111111111111', profileId: '22222222-2222-4222-8222-222222222222', startUtc: '2040-01-01T12:00:00Z', endUtc: '2040-01-01T12:03:00Z', strictMode: false, enforcementConnected: true, mode: 'FocusCycle', currentPhase: phase, nextPhase: null, finalEndUtc: '2040-01-01T12:03:00Z' };
const response = (value: object) => ({ version: 1, requestId: '33333333333343338333333333333333', documentToken: '44444444444444448444444444444444', error: null, status: value });
it('decodes authoritative phase metadata and freezes the nested snapshot', () => {
  const parsed = parseBridgeResponse(response(status));
  expect(parsed?.status?.currentPhase?.kind).toBe('ShortBreak');
  expect(Object.isFrozen(parsed?.status?.currentPhase)).toBe(true);
  const { currentPhase: _current, nextPhase: _next, finalEndUtc: _final, ...legacy } = status;
  expect(parseBridgeResponse(response(legacy))).not.toBeNull();
});
it('rejects incoherent phase mode, scope, ordering, dates and partial metadata shapes', () => {
  expect(parseBridgeResponse(response({ ...status, mode: 'SimpleTimer' }))).toBeNull();
  expect(parseBridgeResponse(response({ ...status, currentPhase: { ...phase, index: 49 } }))).toBeNull();
  expect(parseBridgeResponse(response({ ...status, nextPhase: phase }))).toBeNull();
  expect(parseBridgeResponse(response({ ...status, currentPhase: { ...phase, endUtc: '2040-02-30T12:00:00Z' } }))).toBeNull();
  expect(parseBridgeResponse(response({ ...status, currentPhase: { ...phase, endUtc: '2040-01-01T12:04:00Z' } }))).toBeNull();
  const { nextPhase: _next, ...missing } = status;
  expect(parseBridgeResponse(response(missing))).toBeNull();
});
