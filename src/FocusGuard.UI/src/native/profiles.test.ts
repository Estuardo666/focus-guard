import { createProfilesClient, isProfileDetail, isRulePresentation, type ProfileDetail } from './profiles';
import type { WebViewTransport } from './protocol';
const token = '11111111111141118111111111111111';
const id = '22222222-2222-4222-8222-222222222222';
const detail: ProfileDetail = { id, name: 'Saved profile', description: null, accentHex: '#AABBCC', revision: 'A'.repeat(64), applications: [{ id: '33333333-3333-4333-8333-333333333333', action: 'Allow', matchKind: 'FileName', value: 'work.exe' }], applicationOffset: 0, applicationTotal: 1, websites: [{ id: '44444444-4444-4444-8444-444444444444', action: 'Block', pattern: '*.example.com' }], websiteOffset: 0, websiteTotal: 1, editingEnabled: true, defaultCyclePresetId: null, defaultCyclePresetName: null, cycleSettingsRevision: 'B'.repeat(64), breakApplications: [{ id: '55555555-5555-4555-8555-555555555555', matchKind: 'FileName', value: 'game.exe' }], breakApplicationOffset: 0, breakApplicationTotal: 1, breakWebsites: [{ id: '66666666-6666-4666-8666-666666666666', pattern: 'games.example' }], breakWebsiteOffset: 0, breakWebsiteTotal: 1, cycleSettingsEditingEnabled: true };
function fixture() {
  const target = new EventTarget(); const messages: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(message) { messages.push(message as Record<string, unknown>); } };
  const client = createProfilesClient(transport); client.start();
  const emit = (data: unknown) => target.dispatchEvent(new MessageEvent('message', { data }));
  const initialize = (documentToken = token) => emit({ type: 'host.initialize', version: 1, documentToken, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  const reply = (result: unknown, error: string | null = null, request = messages.at(-1)!) => emit({ version: 1, requestId: request.requestId, documentToken: request.documentToken, error, result });
  initialize(); return { client, messages, emit, initialize, reply };
}
beforeEach(() => vi.useFakeTimers()); afterEach(() => vi.useRealTimers());
it('loads paged rules and passes the full revision and an operation id with an explicit action', async () => {
  const f = fixture(); const loading = f.client.load(id, 0, 0); f.reply(detail); await loading;
  const editing = f.client.mutate('profile.website.add', { action: 'Allow', pattern: 'work.example' });
  expect(f.messages.at(-1)?.params).toMatchObject({ profileId: id.replaceAll('-', ''), revision: detail.revision, action: 'Allow', pattern: 'work.example' });
  expect((f.messages.at(-1)?.params as Record<string, unknown>).operationId).toMatch(/^[0-9a-f]{32}$/);
  f.reply({ profileId: id, revision: 'B'.repeat(64) }); await editing;
  expect(f.client.getSnapshot().phase).toBe('loading'); f.client.stop();
});
it('loads all four bounded rule pages and uses the separate cycle-settings revision for break edits', async () => {
  const f = fixture(); const loading = f.client.load(id, 6, 0, 12, 6); f.reply({ ...detail, applicationOffset: 6, applicationTotal: 7, breakApplicationOffset: 12, breakApplicationTotal: 13, breakWebsiteOffset: 6, breakWebsiteTotal: 7 }); await loading;
  expect(f.messages[0]?.params).toEqual({ profileId: id.replaceAll('-', ''), applicationOffset: 6, websiteOffset: 0, breakApplicationOffset: 12, breakWebsiteOffset: 6 });
  const editing = f.client.mutate('profile.break.application.add', { matchKind: 'FileName', value: 'later.exe' });
  expect(f.messages.at(-1)?.params).toMatchObject({ profileId: id.replaceAll('-', ''), cycleSettingsRevision: detail.cycleSettingsRevision, matchKind: 'FileName', value: 'later.exe' });
  expect(f.messages.at(-1)?.params).not.toHaveProperty('revision');
  f.reply({ profileId: id, revision: detail.revision }); await editing; f.client.stop();
});
it('never retries an unknown write and a failed reconciliation remains blocked', async () => {
  const f = fixture(); const loading = f.client.load(id); f.reply(detail); await loading;
  const editing = f.client.mutate('profile.rename', { name: 'New name' }); await vi.advanceTimersByTimeAsync(4001); await editing;
  expect(f.client.getSnapshot().phase).toBe('unknown');
  await f.client.mutate('profile.rename', { name: 'New name' });
  expect(f.messages.filter(message => message.method === 'profile.rename')).toHaveLength(1);
  const reload = f.client.load(id); f.reply(null, 'ServiceUnavailable'); await reload;
  expect(f.client.getSnapshot().phase).toBe('unknown');
  const verified = f.client.load(id); f.reply({ ...detail, name: 'New name', revision: 'B'.repeat(64) }); await verified;
  expect(f.client.getSnapshot().phase).toBe('idle'); f.client.stop();
});
it('rejects forged correlation and invalid enum/shape rather than rendering or accepting them', async () => {
  const f = fixture(); const loading = f.client.load(id);
  f.emit({ version: 1, requestId: token, documentToken: token, error: null, result: detail });
  expect(f.client.getSnapshot().detail).toBeNull();
  f.reply({ ...detail, applications: [{ ...detail.applications[0], action: 'Launch' }] });
  await vi.advanceTimersByTimeAsync(4001); await loading;
  expect(f.client.getSnapshot().phase).toBe('error'); expect(f.client.getSnapshot().detail).toBeNull(); f.client.stop();
});
it('cancels a write on document change as uncertain and ignores its late reply', async () => {
  const f = fixture(); const loading = f.client.load(id); f.reply(detail); await loading;
  const editing = f.client.mutate('profile.accent', { accentHex: null }); const request = f.messages.at(-1)!;
  f.initialize('55555555555545558555555555555555'); await editing;
  f.reply({ profileId: id, revision: 'B'.repeat(64) }, null, request);
  expect(f.client.getSnapshot().phase).toBe('unknown'); expect(f.client.getSnapshot().detail).toBeNull(); f.client.stop();
});
it('read-only detail cannot send a rule edit', async () => {
  const f = fixture(); const loading = f.client.load(id); f.reply({ ...detail, editingEnabled: false }); await loading;
  await f.client.mutate('profile.application.remove', { ruleId: token });
  expect(f.messages).toHaveLength(1); expect(f.client.getSnapshot().error).toBe('Unauthorized'); f.client.stop();
});
it('read-only cycle settings cannot send a break-rule edit even if normal profile editing is enabled', async () => {
  const f = fixture(); const loading = f.client.load(id); f.reply({ ...detail, cycleSettingsEditingEnabled: false }); await loading;
  await f.client.mutate('profile.break.website.add', { pattern: 'blocked.example' });
  expect(f.messages).toHaveLength(1); expect(f.client.getSnapshot().error).toBe('Unauthorized'); f.client.stop();
});
it('bounds data, rejects duplicate ids and preserves explicit Block/Allow and all matching kinds', () => {
  expect(isProfileDetail(detail)).toBe(true);
  expect(isProfileDetail({ ...detail, revision: 'bad' })).toBe(false);
  expect(isProfileDetail({ ...detail, applications: [detail.applications[0], detail.applications[0]], applicationTotal: 2 })).toBe(false);
  expect(isProfileDetail({ ...detail, applicationOffset: -1 })).toBe(false);
  expect(isProfileDetail({ ...detail, invoke: 'shell' })).toBe(false);
  for (const matchKind of ['FileName', 'Folder', 'ExactPath', 'Sha256']) expect(isProfileDetail({ ...detail, applications: [{ ...detail.applications[0], matchKind }] })).toBe(true);
});
it('validates optional presentation without changing saved rule identity/value and rejects fabricated resolutions', () => {
  const presentation = { name: 'Known app', path: 'C:\\Apps\\work.exe', icon: null, resolution: 'ExactMatch' };
  expect(isRulePresentation(presentation)).toBe(true);
  expect(isRulePresentation({ ...presentation, resolution: 'InstalledAndBlocked' })).toBe(false);
  expect(isRulePresentation({ ...presentation, resolution: 'Ambiguous' })).toBe(false);
  expect(isRulePresentation({ name: 'shared.exe', path: null, icon: null, resolution: 'Ambiguous' })).toBe(true);
  expect(isProfileDetail({ ...detail, applications: [{ ...detail.applications[0], presentation }] })).toBe(true);
});
