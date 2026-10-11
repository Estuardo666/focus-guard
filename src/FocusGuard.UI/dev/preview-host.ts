// Development-only simulated WebView2 host so the UI can be viewed in a normal browser.
// It answers the same bridge messages the real FocusGuard.Desktop host answers, with fixed sample data.
// Query parameters: ?state=active|strict  &theme=light  &lang=en
const params = new URLSearchParams(location.search);
const cycleMode = params.get('state') === 'cycle';
const token = '11111111111141118111111111111111';
const profileId = '22222222-2222-4222-8222-222222222222';
const sessionId = '44444444-4444-4444-8444-444444444444';
const listeners = new Set<(event: MessageEvent) => void>();
const iso = (ms: number) => new Date(Math.floor(ms / 1000) * 1000).toISOString().replace('.000Z', 'Z');
const profile = { id: profileId, name: 'Escritura profunda', applicationRuleCount: 3, websiteRuleCount: 5, blockedApplicationCount: 3, blockedWebsiteCount: 5, accentHex: '#B79CFF' };
let preferences = { uiLanguage: params.get('lang') === 'en' ? 'en' : 'es', theme: params.get('theme') === 'light' ? 'Light' : 'Dark', reduceMotion: false,
  sessionEnd: 'None', cyclePhaseNotifications: false, enforcementEnabled: true, lastDurationMinutes: 25, lastSessionMode: 'SimpleTimer', lastCyclePresetId: null };
let session: { start: number; end: number; strict: boolean } | null = params.get('state') === 'active' || cycleMode || params.get('state') === 'strict'
  ? cycleMode ? { start: Date.now() - 30 * 60_000, end: Date.now() - 30 * 60_000 + 115 * 60_000, strict: false } : { start: Date.now() - 9 * 60_000, end: Date.now() + 16 * 60_000, strict: params.get('state') === 'strict' } : null;

type Rule = { id: string; action: 'Block' | 'Allow'; matchKind?: string; value?: string; pattern?: string };
type StoredProfile = { id: string; name: string; accentHex: string | null; apps: Rule[]; sites: Rule[] };
let ruleCounter = 1;
const ruleId = () => `aaaaaaaa-aaaa-4aaa-8aaa-${String(ruleCounter++).padStart(12, '0')}`;
const rev = () => Array.from({ length: 64 }, () => 'ABCDEF0123456789'[Math.floor(Math.random() * 16)]).join('');
const store: StoredProfile[] = [
  { id: '22222222-2222-4222-8222-222222222222', name: 'Escritura profunda', accentHex: '#D6B7EB',
    apps: [{ id: ruleId(), action: 'Block', matchKind: 'FileName', value: 'Discord.exe' }, { id: ruleId(), action: 'Block', matchKind: 'FileName', value: 'Steam.exe' }, { id: ruleId(), action: 'Allow', matchKind: 'FileName', value: 'Obsidian.exe' }],
    sites: [{ id: ruleId(), action: 'Block', pattern: '*.youtube.com' }, { id: ruleId(), action: 'Block', pattern: 'x.com' }, { id: ruleId(), action: 'Block', pattern: '*.reddit.com' }, { id: ruleId(), action: 'Block', pattern: 'instagram.com' }, { id: ruleId(), action: 'Allow', pattern: 'docs.google.com' }] },
  { id: '33333333-3333-4333-8333-333333333333', name: 'Lectura', accentHex: '#96CFE6', apps: [], sites: [{ id: ruleId(), action: 'Block', pattern: '*.youtube.com' }] },
  { id: '33333333-3333-4333-8333-333333333334', name: 'Deep Work', accentHex: '#B6BCEC', apps: [{ id: ruleId(), action: 'Block', matchKind: 'FileName', value: 'Slack.exe' }], sites: [{ id: ruleId(), action: 'Block', pattern: 'news.ycombinator.com' }] },
  { id: '33333333-3333-4333-8333-333333333335', name: 'Social media', accentHex: null, apps: [], sites: [{ id: ruleId(), action: 'Block', pattern: 'instagram.com' }, { id: ruleId(), action: 'Block', pattern: 'tiktok.com' }] },
  { id: '33333333-3333-4333-8333-333333333336', name: 'Estudio', accentHex: '#9CD9C8', apps: [], sites: [] },
];
const dashed = (id: string) => id.length === 32 ? `${id.slice(0, 8)}-${id.slice(8, 12)}-${id.slice(12, 16)}-${id.slice(16, 20)}-${id.slice(20)}` : id;
const find = (id: unknown) => store.find(item => item.id.replaceAll('-', '') === String(id).replaceAll('-', '').toLowerCase());
const summary = (item: StoredProfile) => ({ id: item.id, name: item.name, accentHex: item.accentHex,
  applicationRuleCount: item.apps.length, websiteRuleCount: item.sites.length,
  blockedApplicationCount: item.apps.filter(rule => rule.action === 'Block').length, blockedWebsiteCount: item.sites.filter(rule => rule.action === 'Block').length });
const detail = (item: StoredProfile, appOffset = 0, siteOffset = 0) => ({ id: item.id, name: item.name, description: null, accentHex: item.accentHex, revision: rev(),
  applications: item.apps.slice(appOffset, appOffset + 6).map(rule => ({ id: rule.id, action: rule.action, matchKind: rule.matchKind, value: rule.value })), applicationOffset: appOffset, applicationTotal: item.apps.length,
  websites: item.sites.slice(siteOffset, siteOffset + 6).map(rule => ({ id: rule.id, action: rule.action, pattern: rule.pattern })), websiteOffset: siteOffset, websiteTotal: item.sites.length,
  editingEnabled: !session, defaultCyclePresetId: null, defaultCyclePresetName: null, cycleSettingsRevision: rev(),
  breakApplications: [], breakApplicationOffset: 0, breakApplicationTotal: 0, breakWebsites: [], breakWebsiteOffset: 0, breakWebsiteTotal: 0, cycleSettingsEditingEnabled: !session });
const mutation = (item: StoredProfile | null) => ({ profileId: item?.id ?? null, revision: item ? rev() : null });
const classicConfig = { focusSeconds: 1500, shortBreakSeconds: 300, longBreakSeconds: 900, cyclesBeforeLongBreak: 4, terminationMode: 'CycleCount', cycleCount: 4, totalDurationSeconds: null };
const presets = [
  { id: 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a01', name: 'Pomodoro clásico', isBuiltIn: true, configuration: classicConfig },
  { id: 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a02', name: 'Enfoque profundo', isBuiltIn: true, configuration: { ...classicConfig, focusSeconds: 3000, shortBreakSeconds: 600, longBreakSeconds: 1200, cyclesBeforeLongBreak: 3, cycleCount: 3 } },
  { id: 'a2af4f65-2de8-4e89-a0bc-4c4db6e24a03', name: 'Flujo extendido', isBuiltIn: true, configuration: { ...classicConfig, focusSeconds: 5400, shortBreakSeconds: 900, longBreakSeconds: 1800, cyclesBeforeLongBreak: 2, cycleCount: 2 } },
];
let lastReview: { start: number; end: number } | null = null;

// Schedules, activity and history samples.
const scheduleSample = (id: string, name: string, days: number, startMinute: number, durationMinutes: number, enabled: boolean, enforcementEnabled: boolean, item: StoredProfile) => {
  const next = new Date(); next.setDate(next.getDate() + 1); next.setHours(Math.floor(startMinute / 60), startMinute % 60, 0, 0);
  return { id, name, days, startMinute, timeZoneId: 'SA Pacific Standard Time', timeZoneIanaId: 'America/Guayaquil', enabled, profileName: item.name, durationMinutes, enforcementEnabled,
    nextStartUtc: enabled ? iso(next.getTime()) : null, nextEndUtc: enabled ? iso(next.getTime() + durationMinutes * 60_000) : null, lastOccurrenceUtc: null, lastResult: enabled ? 'Started' : '',
    profileId: item.id, ...summaryCounts(item), revision: rev() };
};
const summaryCounts = (item: StoredProfile) => { const s = summary(item); return { applicationRuleCount: s.applicationRuleCount, websiteRuleCount: s.websiteRuleCount, blockedApplicationRuleCount: s.blockedApplicationCount, blockedWebsiteRuleCount: s.blockedWebsiteCount }; };
let schedules = [
  scheduleSample('66666666-6666-4666-8666-000000000001', 'Mañanas de escritura', 62, 8 * 60 + 30, 90, true, true, store[0]!),
  scheduleSample('66666666-6666-4666-8666-000000000002', 'Lectura nocturna', 65, 21 * 60, 45, false, false, store[1]!),
];
const scheduleList = () => ({ items: schedules, editingEnabled: !session, timeZoneId: 'SA Pacific Standard Time' });
function todaySample(startUtc: string) {
  const day = Date.parse(startUtc); const at = (h: number, m: number) => day + (h * 60 + m) * 60_000;
  if (day > Date.now()) return { focusSeconds: 0, completedSessions: 0, timeline: [], timelineTruncated: false };
  const span = (id: string, name: string, kind: string, a: number, b: number, completed: boolean) => ({ sessionId: id, profileName: name, kind, startUtc: iso(a), endUtc: iso(b), completed });
  const s1 = '77777777-7777-4777-8777-000000000001', s2 = '77777777-7777-4777-8777-000000000002', s3 = '77777777-7777-4777-8777-000000000003';
  const timeline = [
    span(s1, 'Escritura profunda', 'Focus', at(8, 30), at(8, 55), true), span(s1, 'Escritura profunda', 'ShortBreak', at(8, 55), at(9, 0), true),
    span(s1, 'Escritura profunda', 'Focus', at(9, 0), at(9, 25), true), span(s1, 'Escritura profunda', 'ShortBreak', at(9, 25), at(9, 30), true),
    span(s1, 'Escritura profunda', 'Focus', at(9, 30), at(9, 55), true),
    span(s2, 'Lectura', 'Focus', at(11, 10), at(11, 52), false),
    span(s3, 'Escritura profunda', 'Focus', at(15, 0), at(15, 50), true),
  ].filter(item => Date.parse(item.endUtc) <= Date.now());
  const focus = timeline.filter(item => item.kind === 'Focus').reduce((sum, item) => sum + (Date.parse(item.endUtc) - Date.parse(item.startUtc)) / 1000, 0);
  return { focusSeconds: focus, completedSessions: new Set(timeline.filter(item => item.completed).map(item => item.sessionId)).size, timeline, timelineTruncated: false };
}
const appCatalog = [['Discord', 'C:\\Users\\Me\\AppData\\Local\\Discord\\Discord.exe'], ['Steam', 'C:\\Program Files (x86)\\Steam\\steam.exe'], ['Spotify', 'C:\\Users\\Me\\AppData\\Roaming\\Spotify\\Spotify.exe'], ['Slack', 'C:\\Program Files\\Slack\\slack.exe'], ['WhatsApp', 'C:\\Program Files\\WindowsApps\\WhatsApp.exe'], ['Telegram', 'C:\\Users\\Me\\AppData\\Roaming\\Telegram Desktop\\Telegram.exe'], ['Microsoft Edge', 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe']]
  .map(([name, path], index) => ({ id: `bbbbbbbbbbbb4bbb8bbb${String(index + 1).padStart(12, '0')}`, name: name!, path: path!, icon: null }));

function cyclePhases(start: number) {
  const phases: { index: number; kind: string; focusCycleNumber: number; startUtc: string; endUtc: string; start: number; end: number }[] = [];
  let cursor = start;
  for (let i = 0; i < 7; i++) {
    const cycle = Math.floor(i / 2) + 1;
    const kind = i % 2 === 0 ? 'Focus' : cycle % 4 === 0 ? 'LongBreak' : 'ShortBreak';
    const seconds = kind === 'Focus' ? 1500 : kind === 'LongBreak' ? 900 : 300;
    phases.push({ index: i, kind, focusCycleNumber: cycle, startUtc: iso(cursor), endUtc: iso(cursor + seconds * 1000), start: cursor, end: cursor + seconds * 1000 });
    cursor += seconds * 1000;
  }
  return phases;
}
function bridgeStatus() {
  if (!session) return { serviceReady: true, extensionReady: true, sessionState: 'Inactive', remainingSeconds: 0, sessionId: null, profileId: null, startUtc: null, endUtc: null, strictMode: false, enforcementConnected: false, mode: 'SimpleTimer' };
  const remaining = Math.max(0, Math.round((session.end - Date.now()) / 1000));
  if (cycleMode) {
    const phases = cyclePhases(session.start);
    const now = Date.now();
    const current = phases.find(phase => phase.start <= now && now < phase.end) ?? phases.at(-1)!;
    const next = phases[current.index + 1] ?? null;
    const project = (phase: typeof current) => ({ index: phase.index, kind: phase.kind, focusCycleNumber: phase.focusCycleNumber, remainingSeconds: Math.max(0, Math.round((phase.end - now) / 1000)), endUtc: phase.endUtc, startUtc: phase.startUtc });
    return { serviceReady: true, extensionReady: true, sessionState: remaining > 0 ? 'Active' : 'Expired', remainingSeconds: remaining, sessionId, profileId,
      startUtc: iso(session.start), endUtc: iso(session.end), strictMode: false, enforcementConnected: remaining > 0, mode: 'FocusCycle',
      currentPhase: project(current), nextPhase: next ? { ...project(next), remainingSeconds: Math.round((next.end - next.start) / 1000) } : null, finalEndUtc: iso(session.end) };
  }
  return { serviceReady: true, extensionReady: true, sessionState: remaining > 0 ? 'Active' : 'Expired', remainingSeconds: remaining, sessionId, profileId,
    startUtc: iso(session.start), endUtc: iso(session.end), strictMode: session.strict, enforcementConnected: remaining > 0, mode: 'SimpleTimer' };
}

function result(method: string, body: Record<string, unknown>): unknown {
  switch (method) {
    case 'profiles.list': return { items: store.map(summary), offset: 0, total: store.length, pilotEnabled: true };
    case 'profile.get': { const item = find(body.profileId); if (!item) throw new Error('ProfileMissing'); return detail(item, Number(body.applicationOffset ?? 0), Number(body.websiteOffset ?? 0)); }
    case 'profile.create': { const item = { id: dashed(crypto.randomUUID()), name: String(body.name), accentHex: null, apps: [], sites: [] }; store.push(item); return mutation(item); }
    case 'profile.duplicate': { const source = find(body.profileId)!; const item = { ...source, id: dashed(crypto.randomUUID()), name: `${source.name} (copia)`, apps: [...source.apps], sites: [...source.sites] }; store.push(item); return mutation(item); }
    case 'profile.delete': { const index = store.findIndex(item => item === find(body.profileId)); store.splice(index, 1); return mutation(null); }
    case 'profile.rename': { const item = find(body.profileId)!; item.name = String(body.name); return mutation(item); }
    case 'profile.accent': { const item = find(body.profileId)!; item.accentHex = body.accentHex === null ? null : String(body.accentHex).toUpperCase(); return mutation(item); }
    case 'profile.website.add': { const item = find(body.profileId)!; item.sites.push({ id: ruleId(), action: body.action as 'Block', pattern: String(body.pattern) }); return mutation(item); }
    case 'profile.website.remove': { const item = find(body.profileId)!; item.sites = item.sites.filter(rule => rule.id.replaceAll('-', '') !== body.ruleId); return mutation(item); }
    case 'profile.application.add': { const item = find(body.profileId)!; item.apps.push({ id: ruleId(), action: body.action as 'Block', matchKind: String(body.matchKind), value: String(body.value) }); return mutation(item); }
    case 'profile.application.remove': { const item = find(body.profileId)!; item.apps = item.apps.filter(rule => rule.id.replaceAll('-', '') !== body.ruleId); return mutation(item); }
    case 'profile.application.addFromCatalog': { const item = find(body.profileId)!; const app = appCatalog.find(entry => entry.id === body.catalogId)!; item.apps.push({ id: ruleId(), action: body.action as 'Block', matchKind: 'FileName', value: app.path.split('\\').at(-1)! }); return mutation(item); }
    case 'applications.list': { const query = String(body.query ?? '').toLowerCase(); const offset = Number(body.offset ?? 0); const matches = appCatalog.filter(app => app.name.toLowerCase().includes(query)); return { items: matches.slice(offset, offset + 6), offset, total: matches.length, query: String(body.query ?? '') }; }
    case 'schedules.list': return scheduleList();
    case 'schedule.save': { const item = find(body.profileId)!; const next = scheduleSample(dashed(String(body.scheduleId)), String(body.name), Number(body.days), Number(body.startMinute), Number(body.durationMinutes), !!body.enabled, !!body.enforcementEnabled, item); const index = schedules.findIndex(entry => entry.id === next.id); if (index >= 0) schedules[index] = next; else schedules.push(next); return scheduleList(); }
    case 'schedule.enable': { schedules = schedules.map(entry => entry.id.replaceAll('-', '') === body.scheduleId ? { ...scheduleSample(entry.id, entry.name, entry.days, entry.startMinute, entry.durationMinutes, !!body.enabled, entry.enforcementEnabled, find(entry.profileId)!) } : entry); return scheduleList(); }
    case 'schedule.delete': schedules = schedules.filter(entry => entry.id.replaceAll('-', '') !== body.scheduleId); return scheduleList();
    case 'today.get': return todaySample(String(body.startUtc));
    case 'session.summary': return { sessionId: dashed(String(body.sessionId)), outcome: 'Completed', profileName: 'Escritura profunda', plannedMinutes: 85, focusedMinutes: 75, blockedAttempts: 6, streakDays: 4,
      endedAtUtc: iso(Date.now() - 3600_000), objective: 'Terminar el capítulo 3', reflection: '', captures: [], capturesTruncated: false, cycleMetrics: { completedFocusCycles: 3, completedBreaks: 2 } };
    case 'cycles.list': return { items: presets, offset: 0, total: presets.length };
    case 'preferences.get': return preferences;
    case 'preferences.set': preferences = { ...preferences, ...(body.preferences as object ?? body) }; return preferences;
    case 'session.prepareNormal': {
      const start = Date.now() + 1000; const end = start + Number(body.durationSeconds) * 1000; lastReview = { start, end };
      return { preparationId: '55555555555545558555555555555555', profileId, profileName: profile.name, durationSeconds: Number(body.durationSeconds), enforcementEnabled: !!body.enforcementEnabled,
        applicationRuleCount: 3, websiteRuleCount: 5, blockedApplicationCount: 3, blockedWebsiteCount: 5, startUtc: iso(start), endUtc: iso(end), expiresUtc: iso(start + 120_000), extensionReady: true };
    }
    case 'session.startNormal': {
      const review = lastReview ?? { start: Date.now(), end: Date.now() + 25 * 60_000 };
      session = { ...review, strict: false };
      return { sessionId, startUtc: iso(review.start), endUtc: iso(review.end) };
    }
    case 'session.stopNormal': session = null; return { sessionId, action: 'Stopped' };
    case 'session.intent.get': return { sessionId: dashed(String(body.sessionId)), available: true, replayable: true, unavailableReason: null, originalProfileName: profile.name, profile,
      durationMinutes: cycleMode ? 115 : 25, mode: cycleMode ? 'FocusCycle' : 'SimpleTimer', strict: session?.strict ?? false, enforcementEnabled: true, cyclePresetId: cycleMode ? presets[0]!.id : null, cycleConfiguration: cycleMode ? classicConfig : null, objective: 'Terminar el capítulo 3' };
    case 'app.open': case 'app.compact': case 'app.quit': case 'surface.close': case 'surface.ready': console.info('[preview shell]', method, body); return { accepted: true };
    default: throw new Error('Unavailable');
  }
}

const transport = {
  addEventListener(_type: string, listener: (event: MessageEvent) => void) { listeners.add(listener); },
  removeEventListener(_type: string, listener: (event: MessageEvent) => void) { listeners.delete(listener); },
  postMessage(message: Record<string, unknown>) {
    const emit = (data: unknown) => setTimeout(() => listeners.forEach(listener => listener(new MessageEvent('message', { data }))), 60);
    if (message.method === 'status.get') { emit({ version: 1, requestId: message.requestId, documentToken: token, error: null, status: bridgeStatus() }); return; }
    try { emit({ version: 1, requestId: message.requestId, documentToken: token, error: null, result: result(String(message.method), (message.params ?? {}) as Record<string, unknown>) }); }
    catch { emit({ version: 1, requestId: message.requestId, documentToken: token, error: 'Unavailable', result: null }); }
  },
};
(globalThis as unknown as { chrome: unknown }).chrome = { webview: transport };
// ?surface=tray|toast|phase|summary|block previews the desktop popups: the host announces the surface before host.initialize.
const surfaceParam = params.get('surface');
const surfaceMessages: Record<string, { surface: string; payload: Record<string, unknown> }> = {
  tray: { surface: 'tray', payload: {} },
  toast: { surface: 'toast', payload: { kind: 'sessionEnd', sessionId: '77777777-7777-4777-8777-000000000001' } },
  phase: { surface: 'toast', payload: { kind: 'phase', endedKind: 'Focus', endedCycle: 1, startedKind: 'ShortBreak', startedCycle: null, startedEndUtc: iso(Date.now() + 300_000) } },
  summary: { surface: 'summary', payload: { sessionId: '77777777-7777-4777-8777-000000000001', dimmed: false } },
  block: { surface: 'block', payload: { target: 'C:\\Program Files\\Discord\\Discord.exe', startUtc: iso(Date.now() - 600_000), endUtc: iso(Date.now() + 900_000), strict: false } },
};
if (surfaceParam && surfaceMessages[surfaceParam]) {
  const announced = surfaceMessages[surfaceParam];
  setTimeout(() => listeners.forEach(listener => listener(new MessageEvent('message', { data: { type: 'host.surface', version: 1, documentToken: token, surface: announced.surface, payload: announced.payload } }))), 100);
}setTimeout(() => listeners.forEach(listener => listener(new MessageEvent('message', { data: { type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Preview', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' } }))), 120);

export {};
