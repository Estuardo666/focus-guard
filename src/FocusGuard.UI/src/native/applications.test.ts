import { createApplicationsClient, decodeIcon, isApplicationPage } from './applications';
import type { WebViewTransport } from './protocol';
const app = { id: '11111111111141118111111111111111', name: 'Known app', path: 'C:\\Apps\\work.exe', icon: null };
const page = { items: [app], offset: 0, total: 1, query: '' };
it('bounds catalogs and icon bytes and converts BGRA to RGBA without image URLs', () => {
  expect(isApplicationPage(page)).toBe(true); expect(isApplicationPage({ ...page, items: [{ ...app, path: '\\\\server\\share\\app.exe' }] })).toBe(false);
  expect(isApplicationPage({ ...page, items: [app, app], total: 2 })).toBe(false);
  const bytes = new Uint8Array(4096); bytes.set([1, 2, 3, 4]);
  const icon = { width: 32, height: 32, bgra: btoa(String.fromCharCode(...bytes)) };
  expect(Array.from(decodeIcon(icon)!.slice(0, 4))).toEqual([3, 2, 1, 4]); expect(decodeIcon({ ...icon, width: 4096 })).toBeNull(); expect(decodeIcon({ ...icon, bgra: 'bad' })).toBeNull();
});
it('requires exact response correlation and ignores stale document catalog data', async () => {
  const target = new EventTarget(); const messages: Record<string, unknown>[] = [];
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(v) { messages.push(v as Record<string, unknown>); } };
  const client = createApplicationsClient(transport); client.start();
  const token = '22222222222242228222222222222222';
  target.dispatchEvent(new MessageEvent('message', { data: { type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' } }));
  const search = client.search(''); const request = messages.at(-1)!;
  target.dispatchEvent(new MessageEvent('message', { data: { version: 1, requestId: token, documentToken: token, error: null, result: page } })); expect(client.getSnapshot().page).toBeNull();
  target.dispatchEvent(new MessageEvent('message', { data: { version: 1, requestId: request.requestId, documentToken: token, error: null, result: page } })); await search;
  expect(client.getSnapshot().page).toEqual(page); client.stop();
});
