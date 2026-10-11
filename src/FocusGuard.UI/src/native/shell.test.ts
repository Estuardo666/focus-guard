import { createShellClient, parseHostSurface } from './shell';

const token = '11111111111141118111111111111111';
const session = '33333333-3333-4333-8333-333333333333';

function fixture() {
  const target = new EventTarget();
  const messages: Record<string, unknown>[] = [];
  const transport = {
    addEventListener: target.addEventListener.bind(target),
    removeEventListener: target.removeEventListener.bind(target),
    postMessage(message: unknown) { messages.push(message as Record<string, unknown>); },
  };
  const emit = (value: unknown) => target.dispatchEvent(new MessageEvent('message', { data: value }));
  const initialize = (documentToken = token) => emit({ type: 'host.initialize', version: 1, documentToken, title: 'FocusGuard',
    description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  const surface = (name: string, payload: Record<string, unknown>, documentToken = token) =>
    emit({ type: 'host.surface', version: 1, documentToken, surface: name, payload });
  const client = createShellClient(transport);
  client.start();
  return { client, messages, emit, initialize, surface };
}

beforeEach(() => {
  vi.stubGlobal('crypto', { randomUUID: () => '22222222-2222-4222-8222-222222222222' });
});

afterEach(() => vi.unstubAllGlobals());

it('accepts only exact, validated surface descriptions', () => {
  const base = { type: 'host.surface', version: 1, documentToken: token };
  expect(parseHostSurface({ ...base, surface: 'tray', payload: {} })).toEqual({ surface: 'tray', documentToken: token });
  expect(parseHostSurface({ ...base, surface: 'summary', payload: { sessionId: session, dimmed: true } })).toMatchObject({ surface: 'summary', dimmed: true });
  expect(parseHostSurface({ ...base, surface: 'block', payload: { target: 'Steam.exe', startUtc: '2026-10-10T09:00:00Z', endUtc: '2026-10-10T09:25:00Z', strict: false } }))
    .toMatchObject({ surface: 'block', target: 'Steam.exe' });
  expect(parseHostSurface({ ...base, surface: 'toast', payload: { kind: 'phase', endedKind: 'Focus', endedCycle: 1, startedKind: 'ShortBreak', startedCycle: null, startedEndUtc: '2026-10-10T09:30:00Z' } }))
    .toMatchObject({ surface: 'toast', payload: { kind: 'phase', startedKind: 'ShortBreak' } });

  expect(parseHostSurface({ ...base, surface: 'tray', payload: { extra: 1 } })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'browser', payload: {} })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'summary', payload: { sessionId: 'not-a-guid', dimmed: false } })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'block', payload: { target: '', startUtc: '2026-10-10T09:00:00Z', endUtc: '2026-10-10T09:25:00Z', strict: false } })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'block', payload: { target: 'x', startUtc: '2026-10-10T09:00:00-05:00', endUtc: '2026-10-10T09:25:00Z', strict: false } })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'toast', payload: { kind: 'phase', endedKind: null, endedCycle: null, startedKind: null, startedCycle: null, startedEndUtc: null } })).toBeNull();
  expect(parseHostSurface({ ...base, surface: 'toast', payload: { kind: 'sessionEnd', sessionId: session, html: '<b>' } })).toBeNull();
  expect(parseHostSurface({ ...base, version: 2, surface: 'tray', payload: {} })).toBeNull();
});

it('is the main window when the host initializes without announcing a surface', () => {
  const { client, initialize } = fixture();
  expect(client.getSnapshot().mode).toBe('unknown');
  initialize();
  expect(client.getSnapshot()).toMatchObject({ mode: 'main', surface: null });
});

it('becomes a popup only when the announced surface carries the same document token', () => {
  const popup = fixture();
  popup.surface('summary', { sessionId: session, dimmed: false });
  popup.initialize();
  expect(popup.client.getSnapshot()).toMatchObject({ mode: 'surface', surface: { surface: 'summary', sessionId: session } });

  const stale = fixture();
  stale.surface('tray', {}, 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
  stale.initialize();
  expect(stale.client.getSnapshot()).toMatchObject({ mode: 'main', surface: null });
});

it('follows navigation only for the current document and known sections', () => {
  const { client, emit, initialize } = fixture();
  initialize();
  emit({ type: 'host.navigate', version: 1, documentToken: token, section: 'preferences' });
  expect(client.getSnapshot().navigation).toEqual({ section: 'preferences', sequence: 1 });
  emit({ type: 'host.navigate', version: 1, documentToken: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', section: 'activity' });
  emit({ type: 'host.navigate', version: 1, documentToken: token, section: 'https://example.com' });
  expect(client.getSnapshot().navigation).toEqual({ section: 'preferences', sequence: 1 });
  emit({ type: 'host.navigate', version: 1, documentToken: token, section: 'preferences' });
  expect(client.getSnapshot().navigation?.sequence).toBe(2);
});

it('sends shell commands only after initialization, bound to the document token', () => {
  const { client, messages, initialize } = fixture();
  expect(client.quit()).toBe(false);
  initialize();
  expect(client.open('activity')).toBe(true);
  expect(client.openCompact()).toBe(true);
  expect(client.close()).toBe(true);
  expect(client.quit()).toBe(true);
  expect(client.ready()).toBe(true);
  expect(messages.map(message => [message.method, message.params, message.documentToken])).toEqual([
    ['app.open', { section: 'activity' }, token],
    ['app.compact', {}, token],
    ['surface.close', {}, token],
    ['app.quit', {}, token],
    ['surface.ready', {}, token],
  ]);
});

it('outside the desktop host there is nothing to wait for', () => {
  const client = createShellClient(null);
  client.start();
  expect(client.getSnapshot().mode).toBe('main');
  expect(client.close()).toBe(false);
});

it('reads and writes sign-in start through a matched response for the current document', async () => {
  const { client, messages, emit, initialize } = fixture();
  initialize();
  const reading = client.startup();
  const request = messages.at(-1)!;
  expect([request.method, request.params]).toEqual(['app.startup.get', {}]);
  emit({ version: 1, requestId: 'ffffffffffffffffffffffffffffffff', documentToken: token, error: null, result: { accepted: true, startupEnabled: true } });
  emit({ version: 1, requestId: request.requestId, documentToken: token, error: null, result: { accepted: true, startupEnabled: false } });
  await expect(reading).resolves.toBe(false);

  const writing = client.startup(true);
  const write = messages.at(-1)!;
  expect([write.method, write.params]).toEqual(['app.startup.set', { enabled: true }]);
  emit({ version: 1, requestId: write.requestId, documentToken: token, error: 'WindowUnavailable', result: null });
  await expect(writing).resolves.toBeNull();
});
