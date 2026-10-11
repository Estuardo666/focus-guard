import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ProfileEditor } from './ProfileEditor';
import { createProfilesClient, type ProfilesClient, type ProfileDetail } from './native/profiles';
import { createDashboardClient, type DashboardClient } from './native/dashboard';
import type { ConnectionSnapshot, WebViewTransport } from './native/protocol';
import es from './locales/es.json';
import en from './locales/en.json';
const token = '11111111111141118111111111111111'; const id = '22222222-2222-4222-8222-222222222222';
const detail: ProfileDetail = { id, name: 'Saved profile', description: 'Real saved description', accentHex: '#EFD9D5', revision: 'A'.repeat(64), applications: [{ id: '33333333-3333-4333-8333-333333333333', action: 'Allow', matchKind: 'Folder', value: 'C:\\Work' }], applicationOffset: 0, applicationTotal: 1, websites: [{ id: '44444444-4444-4444-8444-444444444444', action: 'Block', pattern: '*.example.com' }], websiteOffset: 0, websiteTotal: 1, editingEnabled: true, defaultCyclePresetId: null, defaultCyclePresetName: null, cycleSettingsRevision: 'B'.repeat(64), breakApplications: [{ id: '55555555-5555-4555-8555-555555555555', matchKind: 'FileName', value: 'game.exe' }], breakApplicationOffset: 0, breakApplicationTotal: 1, breakWebsites: [{ id: '66666666-6666-4666-8666-666666666666', pattern: 'games.example' }], breakWebsiteOffset: 0, breakWebsiteTotal: 1, cycleSettingsEditingEnabled: true };
const connection: ConnectionSnapshot = { phase: 'ready', error: null, updatedAt: 1, status: { serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0, sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' } };
let root: Root; let container: HTMLDivElement; let profiles: ProfilesClient; let dashboard: DashboardClient;
function fixture(pilot = true) {
  const target = new EventTarget(); const sent: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(message) { sent.push(message as Record<string, unknown>); } };
  profiles = createProfilesClient(transport); dashboard = createDashboardClient(transport); profiles.start(); dashboard.start();
  const emit = (data: unknown) => target.dispatchEvent(new MessageEvent('message', { data }));
  emit({ type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' });
  const reply = (result: unknown, error: string | null = null) => { const request = sent.at(-1)!; emit({ version: 1, requestId: request.requestId, documentToken: token, error, result }); };
  reply({ items: [{ id, name: detail.name, applicationRuleCount: 1, websiteRuleCount: 1, blockedApplicationCount: 0, blockedWebsiteCount: 1 }], offset: 0, total: 2, pilotEnabled: pilot });
  return { sent, reply };
}
const render = async (value = connection) => act(async () => root.render(<ProfileEditor profiles={profiles} dashboard={dashboard} connection={value} language="es" t={key => es[key]}/>));
const button = (name: string) => Array.from(container.querySelectorAll<HTMLButtonElement>('button')).find(element => element.textContent === name)!;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); profiles.stop(); dashboard.stop(); container.remove(); });
async function loaded(pilot = true) { const f = fixture(pilot); await render(); await act(async () => f.reply({ ...detail, editingEnabled: pilot })); return f; }
async function openBreaks() { await act(async () => container.querySelector<HTMLButtonElement>('#break-settings-title')!.click()); return container.querySelector<HTMLElement>('.break-settings')!; }
async function openProfileSettings() { const panel = container.querySelector<HTMLDetailsElement>('.profile-identity-settings')!; await act(async () => { panel.open = true; }); return panel; }
it('names profile reload separately from service status refresh', async () => {
  const f = await loaded();
  const previousListRequests = f.sent.filter(message => message.method === 'profiles.list').length;
  const refreshProfiles = button(es.refreshProfiles);
  expect(refreshProfiles.textContent).toBe('Actualizar perfiles');
  expect(refreshProfiles.textContent).not.toBe(es.refresh);
  expect(en.refreshProfiles).toBe('Refresh profiles');
  expect(en.refreshProfiles).not.toBe(en.refresh);
  await act(async () => refreshProfiles.click());
  expect(f.sent.filter(message => message.method === 'profiles.list')).toHaveLength(previousListRequests + 1);
});
it('does not duplicate an unresolved application label when it equals the saved value', async () => {
  const f = fixture(); await render();
  const value = 'FocusGuard.TestTarget.exe';
  const rule: ProfileDetail['applications'][number] = {
    id: '33333333-3333-4333-8333-333333333333', action: 'Block', matchKind: 'FileName', value,
    presentation: { name: value, path: null, icon: null, resolution: 'Unresolved' }
  };
  await act(async () => f.reply({ ...detail, applications: [rule], applicationTotal: 1 }));
  const saved = container.querySelector('[aria-labelledby="app-rules-title"] .saved-rules li')!;
  expect(saved.querySelector('strong')?.textContent).toBe(value);
  expect(saved.querySelectorAll('small.rule-target')).toHaveLength(0);
  expect(saved.textContent).toContain(es.Unresolved);
});
it('retains the exact stored target when the friendly presentation label differs', async () => {
  const f = fixture(); await render();
  const value = 'C:/Work/work.exe';
  const rule: ProfileDetail['applications'][number] = {
    id: '33333333-3333-4333-8333-333333333333', action: 'Block', matchKind: 'ExactPath', value,
    presentation: { name: 'Work app', path: value, icon: null, resolution: 'ExactMatch' }
  };
  await act(async () => f.reply({ ...detail, applications: [rule], applicationTotal: 1 }));
  const saved = container.querySelector('[aria-labelledby="app-rules-title"] .saved-rules li')!;
  expect(saved.querySelector('strong')?.textContent).toBe('Work app');
  expect(Array.from(saved.querySelectorAll('small.rule-target')).map(node => node.textContent)).toEqual([value]);
});it('shows saved rule actions and matching kind and never invents installed application entries', async () => {
  await loaded(); expect(container.textContent).toContain('Permitir'); expect(container.textContent).toContain('Bloquear'); expect(container.textContent).toContain('Carpeta'); expect(container.textContent).toContain('C:\\Work'); expect(container.textContent).toContain('Real saved description'); expect(container.textContent).toContain(es.savedRulesTitle); expect(container.textContent).not.toContain('Adobe');
});
it('keeps rule editors before compact profile settings and opens metadata through a native disclosure', async () => {
  await loaded();
  const settings = container.querySelector<HTMLDetailsElement>('.profile-identity-settings')!;
  expect(settings.open).toBe(false);
  expect(settings.querySelector('summary')?.textContent).toContain('Saved profile');
  expect(settings.querySelector('summary')?.textContent).toContain(es.customizeProfile);
  const workspace = container.querySelector('.profile-workspace')!;
  expect(Array.from(workspace.children).indexOf(settings)).toBeLessThan(Array.from(workspace.children).findIndex(child => child.matches('.rule-editors')));
  expect(container.querySelector('.profile-identity #profile-name')).not.toBeNull();
  await openProfileSettings();
  expect(settings.open).toBe(true);
  expect(button(es.duplicateProfile).disabled).toBe(false);
});

it('keeps profile colors compact inside 44px labeled targets with a visible selected mark', async () => {
  await loaded();
  await openProfileSettings();
  const choices = Array.from(container.querySelectorAll<HTMLButtonElement>('.profile-colors .color-choice'));
  expect(choices).toHaveLength(9);
  const selected = choices.find(choice => choice.getAttribute('aria-pressed') === 'true');
  expect(selected?.getAttribute('aria-label')).toBe(`${es.profileColor} 1`);
  expect(selected?.querySelector('.profile-color-swatch')?.getAttribute('aria-hidden')).toBe('true');
  expect(selected?.querySelector('svg.profile-color-check')).not.toBeNull();
  expect(container.querySelector<HTMLInputElement>('.profile-colors input[type="color"]')?.getAttribute('aria-label'))
    .toBe(es.customColor);
});
it('explains empty application and website rules before offering more rules', async () => {
  const f = fixture(); await render();
  await act(async () => f.reply({ ...detail, applications: [], applicationTotal: 0, websites: [], websiteTotal: 0 }));
  expect(container.textContent).toContain(es.noApplicationRules);
  expect(container.textContent).toContain(es.noWebsiteRules);
  expect(container.querySelectorAll('.saved-rule-section h3')).toHaveLength(2);
});
it('keeps break-only blocks in their own tab and lists their exact targets when opened', async () => {
  await loaded();
  const tab = container.querySelector<HTMLButtonElement>('#break-settings-title')!;
  expect(tab.getAttribute('role')).toBe('tab');
  expect(tab.getAttribute('aria-selected')).toBe('false');
  expect(tab.textContent).toContain(es.breaksTab);
  expect(tab.querySelector('.rule-tab-count')?.textContent).toBe('2');
  expect(container.querySelector<HTMLElement>('.break-settings')!.hidden).toBe(true);
  const panel = await openBreaks();
  expect(panel.hidden).toBe(false);
  expect(tab.getAttribute('aria-selected')).toBe('true');
  expect(panel.textContent).toContain(es.breakSettingsDetail);
  expect(panel.textContent).toContain('game.exe');
  expect(panel.textContent).toContain('games.example');
  expect(panel.querySelector('.break-rule-editors option[value="Allow"]')).toBeNull();
  expect(panel.querySelector('label[for="break-application-value"]')?.contains(panel.querySelector('#break-application-value'))).toBe(true);
  expect(panel.querySelector('label[for="break-website-pattern"]')?.contains(panel.querySelector('#break-website-pattern'))).toBe(true);
});
it('writes break exceptions with their own cycle-settings revision and does not retry conflicts', async () => {
  const f = await loaded();
  await openBreaks();
  const value = container.querySelector<HTMLInputElement>('#break-application-value')!;
  await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(value, 'later.exe'); value.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => value.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('profile.break.application.add');
  expect(f.sent.at(-1)?.params).toMatchObject({ matchKind: 'FileName', value: 'later.exe', cycleSettingsRevision: detail.cycleSettingsRevision });
  expect(f.sent.at(-1)?.params).not.toHaveProperty('revision');
  const mutationCount = f.sent.filter(message => message.method === 'profile.break.application.add').length;
  await act(async () => f.reply(null, 'ProfileConflict'));
  expect(container.textContent).toContain('Este perfil cambió en otra ventana');
  expect(f.sent.filter(message => message.method === 'profile.break.application.add')).toHaveLength(mutationCount);
});
it('shows the per-profile default cycle preset and saves it with the cycle-settings revision', async () => {
  const f = await loaded();
  await openBreaks();
  const presetId = 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a01';
  await act(async () => f.reply({ items: [{ id: presetId, name: 'Pomodoro clásico', isBuiltIn: true,
    configuration: { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4,
      terminationMode: 'CycleCount', cycleCount: 4, totalDurationSeconds: null } }], offset: 0, total: 1, editingEnabled: false }));
  const select = container.querySelector<HTMLSelectElement>('#profile-default-cycle-preset')!;
  await act(async () => { select.value = presetId; select.dispatchEvent(new Event('change', { bubbles: true })); });
  await act(async () => button(es.saveDefaultCyclePreset).click());
  expect(f.sent.at(-1)?.method).toBe('profile.cycle.defaultPreset.set');
  expect(f.sent.at(-1)?.params).toMatchObject({ presetId: presetId.replaceAll('-', ''), cycleSettingsRevision: detail.cycleSettingsRevision });
  expect(f.sent.at(-1)?.params).not.toHaveProperty('revision');
  await act(async () => f.reply(null, 'ProfileConflict'));
  expect(container.textContent).toContain('Este perfil cambió en otra ventana');
});
it('sends chosen application action and matching kind with the displayed revision', async () => {
  const f = await loaded();
  const value = container.querySelector<HTMLInputElement>('#application-value')!;
  await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(value, 'allowed.exe'); value.dispatchEvent(new Event('input', { bubbles: true })); });
  const selects = container.querySelectorAll<HTMLSelectElement>('.rule-add-options select');
  await act(async () => { selects[0]!.value = 'Allow'; selects[0]!.dispatchEvent(new Event('change', { bubbles: true })); selects[1]!.value = 'FileName'; selects[1]!.dispatchEvent(new Event('change', { bubbles: true })); });
  await act(async () => value.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('profile.application.add'); expect(f.sent.at(-1)?.params).toMatchObject({ action: 'Allow', matchKind: 'FileName', value: 'allowed.exe', revision: detail.revision });
  await act(async () => f.reply(null, 'InvalidInput'));
});
it('requires deletion review and explicit confirmation, with a safe cancel action', async () => {
  const f = await loaded();
  await openProfileSettings();
  await act(async () => button('Eliminar perfil').click());
  expect(container.querySelector('dialog')?.hasAttribute('open')).toBe(true);
  expect(container.textContent).toContain('No se cancela una programación'); expect(f.sent.filter(message => message.method === 'profile.delete')).toHaveLength(0);
  await act(async () => button('Volver').click()); expect(container.querySelector('dialog')).toBeNull();
  await act(async () => button('Eliminar perfil').click()); await act(async () => button('Eliminar definitivamente').click());
  expect(f.sent.at(-1)?.params).toMatchObject({ confirmed: true, revision: detail.revision }); await act(async () => f.reply(null, 'LastProfile'));
});
it('readonly, stale and pending recovery disable all mutations while retaining rule information', async () => {
  await loaded(false); await openProfileSettings(); expect(button('Duplicar perfil').disabled).toBe(true); expect(container.textContent).toContain('*.example.com');
  const readOnlyNote = container.querySelector<HTMLElement>('.profile-readonly-note');
  expect(container.firstElementChild).toBe(readOnlyNote);
  expect(readOnlyNote?.getAttribute('role')).toBe('note');
  expect(readOnlyNote?.textContent).toContain(es.editorPilotRequired);
  expect(container.querySelector('.profiles-rail')?.contains(readOnlyNote)).toBe(false);
  expect(container.textContent).toContain('Esta ventana está en modo de solo lectura.');
  expect(container.textContent).toContain('abre FocusGuard en modo piloto');
  expect(container.textContent).toContain('FocusGuard clásico');
  await render({ ...connection, phase: 'stale' }); expect(button('Crear perfil').disabled).toBe(true);
  await render({ ...connection, status: { ...connection.status!, sessionState: 'RecoveryPending', strictMode: true } }); expect(button('Eliminar perfil').disabled).toBe(true);
});
it('shows an edit conflict and prevents more writes until the current profile is reloaded', async () => {
  const f = await loaded(); await openProfileSettings(); await act(async () => button('Duplicar perfil').click()); await act(async () => f.reply(null, 'ProfileConflict'));
  expect(container.textContent).toContain('Este perfil cambió en otra ventana'); expect(button('Duplicar perfil').disabled).toBe(true);
});

it('groups saved rules by effect so blocked and always-allowed targets read separately', async () => {
  await loaded();
  const sites = container.querySelector('[aria-labelledby="site-rules-title"]')!;
  const apps = container.querySelector('[aria-labelledby="app-rules-title"]')!;
  expect(sites.querySelector('.rule-group:not(.allowed) h4')?.textContent).toBe(es.blockedGroup);
  expect(sites.querySelector('.rule-group:not(.allowed) .saved-rules')?.textContent).toContain('*.example.com');
  expect(apps.querySelector('.rule-group.allowed h4')?.textContent).toBe(es.allowedGroup);
  expect(apps.querySelector('.rule-group.allowed .saved-rules')?.textContent).toContain('C:\\Work');
  expect(apps.querySelector('.rule-group:not(.allowed)')).toBeNull();
});
it('adds a website with the action picked on the segmented control and the displayed revision', async () => {
  const f = await loaded();
  const input = container.querySelector<HTMLInputElement>('#website-pattern')!;
  await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(input, 'docs.example.com'); input.dispatchEvent(new Event('input', { bubbles: true })); });
  const allow = container.querySelector<HTMLInputElement>('.rule-quick-add input[type=radio][value=Allow]')!;
  expect(allow.closest('[role=radiogroup]')?.getAttribute('aria-label')).toBe(es.ruleAction);
  await act(async () => allow.click());
  expect(allow.checked).toBe(true);
  await act(async () => input.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('profile.website.add');
  expect(f.sent.at(-1)?.params).toMatchObject({ action: 'Allow', pattern: 'docs.example.com', revision: detail.revision });
});
it('summarises what the selected profile blocks in its header', async () => {
  await loaded();
  const summary = container.querySelector('.profile-hero summary')!;
  expect(summary.querySelector('.profile-blocked-line')?.textContent).toContain('1');
  expect(summary.textContent).toContain('Saved profile');
});

it('shows one rule kind at a time and moves between tabs with the arrow keys', async () => {
  await loaded();
  const sites = container.querySelector<HTMLElement>('#site-rules-title-panel')!;
  const apps = container.querySelector<HTMLElement>('#app-rules-title-panel')!;
  expect(sites.hidden).toBe(false); expect(apps.hidden).toBe(true);
  expect(container.querySelector('[role=tablist]')?.getAttribute('aria-label')).toBe(es.profileRulesLabel);
  const sitesTab = container.querySelector<HTMLButtonElement>('#site-rules-title')!;
  expect(sitesTab.querySelector('.rule-tab-count')?.textContent).toBe('1');
  await act(async () => sitesTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })));
  expect(sites.hidden).toBe(true); expect(apps.hidden).toBe(false);
  expect(document.activeElement?.id).toBe('app-rules-title');
  await act(async () => container.querySelector<HTMLButtonElement>('#app-rules-title')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true })));
  expect(sites.hidden).toBe(false);
});
it('lists every profile as a card with its rule counts and creates a new one from the last card', async () => {
  const f = await loaded();
  const cards = container.querySelectorAll<HTMLButtonElement>('button.profile-card');
  expect(cards).toHaveLength(1);
  expect(cards[0]!.getAttribute('aria-pressed')).toBe('true');
  expect(cards[0]!.querySelector('.profile-card-name')?.textContent).toBe('Saved profile');
  const name = container.querySelector<HTMLInputElement>('#new-profile-name')!;
  expect(name.closest('.profile-card.new')).not.toBeNull();
  await act(async () => { const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!; setter.call(name, 'Lectura'); name.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => name.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  expect(f.sent.at(-1)?.method).toBe('profile.create');
  expect(f.sent.at(-1)?.params).toMatchObject({ name: 'Lectura' });
});
