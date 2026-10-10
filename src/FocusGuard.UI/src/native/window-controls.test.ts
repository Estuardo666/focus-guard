import { createWindowControlsClient } from './window-controls';
import { parseHostWindowState } from './protocol';

const token = '11111111111141118111111111111111';
const response = (request: Record<string, unknown>, result: unknown, error: string | null = null) => ({
  version: 1,
  requestId: request.requestId,
  documentToken: request.documentToken,
  error,
  result,
});

function fixture() {
  const target = new EventTarget();
  const messages: Record<string, unknown>[] = [];
  const transport = {
    addEventListener: target.addEventListener.bind(target),
    removeEventListener: target.removeEventListener.bind(target),
    postMessage(message: unknown) { messages.push(message as Record<string, unknown>); },
  };
  const emit = (value: unknown) => target.dispatchEvent(new MessageEvent('message', { data: value }));
  const initialize = () => emit({ type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard',
    description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
const state = (compact: boolean, alwaysOnTop: boolean, detailsExpanded = true, documentToken = token) => emit({
    type: 'host.windowState', version: 1, documentToken, compact, alwaysOnTop, detailsExpanded,
  });
  const client = createWindowControlsClient(transport);
  return { client, messages, emit, initialize, state };
}

beforeEach(() => {
  vi.stubGlobal('crypto', { randomUUID: () => '22222222-2222-4222-8222-222222222222' });
});

afterEach(() => vi.unstubAllGlobals());

it('accepts only a versioned, current-document window state message', () => {
  const value = { type: 'host.windowState', version: 1, documentToken: token, compact: true, alwaysOnTop: false, detailsExpanded: true };
  expect(parseHostWindowState(value)).toEqual(value);
  expect(parseHostWindowState({ ...value, extra: true })).toBeNull();
  expect(parseHostWindowState({ ...value, documentToken: '00000000000000000000000000000000' })).toBeNull();
  expect(parseHostWindowState({ ...value, compact: 'true' })).toBeNull();
});

it('synchronizes native hotkey state and sends compact and always-on-top commands', async () => {
  const f = fixture();
  f.client.start();
  f.initialize();
  f.state(false, false);
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'ready', compact: false, alwaysOnTop: false, detailsExpanded: true });

  const compact = f.client.setCompactMode(true);
  const compactRequest = f.messages.at(-1)!;
  expect(compactRequest).toMatchObject({ method: 'window.compact.set', params: { enabled: true }, documentToken: token });
  f.state(true, false);
  f.emit(response(compactRequest, { compact: true, alwaysOnTop: false, detailsExpanded: true }));
  expect(await compact).toBe(true);
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'ready', compact: true, detailsExpanded: true });

  const topmost = f.client.setAlwaysOnTop(true);
  const topmostRequest = f.messages.at(-1)!;
  expect(topmostRequest).toMatchObject({ method: 'window.alwaysOnTop.set', params: { enabled: true } });
  f.state(true, true);
  f.emit(response(topmostRequest, { compact: true, alwaysOnTop: true, detailsExpanded: true }));
  expect(await topmost).toBe(true);
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'ready', compact: true, alwaysOnTop: true });

  const collapsed = f.client.setDetailsExpanded(false);
  const detailsRequest = f.messages.at(-1)!;
  expect(detailsRequest).toMatchObject({ method: 'window.compactDetails.set', params: { enabled: false } });
  f.state(true, true, false);
  f.emit(response(detailsRequest, { compact: true, alwaysOnTop: true, detailsExpanded: false }));
  expect(await collapsed).toBe(true);
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'ready', compact: true, detailsExpanded: false });
  f.client.stop();
});

it('ignores window state from a stale document token', () => {
  const f = fixture();
  f.client.start();
  f.initialize();
  f.state(false, false, true, '33333333333343338333333333333333');
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'waiting', compact: false, alwaysOnTop: false, detailsExpanded: true });
  f.client.stop();
});

it('surfaces a failed native view change for retry', async () => {
  const f = fixture();
  f.client.start();
  f.initialize();
  f.state(false, false);
  const change = f.client.setCompactMode(true);
  const request = f.messages.at(-1)!;
  f.emit(response(request, null, 'WindowUnavailable'));

  expect(await change).toBe(false);
  expect(f.client.getSnapshot()).toMatchObject({ phase: 'error', error: 'WindowUnavailable', compact: false });
  f.client.stop();
});
