import { act } from 'react'; import { createRoot } from 'react-dom/client';
import { ApplicationsPicker } from './ApplicationsPicker';
import { createApplicationsClient } from './native/applications'; import type { WebViewTransport } from './native/protocol'; import es from './locales/es.json'; import en from './locales/en.json';
it('shows name/path and adds an opaque catalog entry with the chosen Allow action, never a caller path', async () => {
  vi.useFakeTimers(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const target = new EventTarget(); const sent: Record<string, unknown>[] = []; const add = vi.fn(async () => undefined);
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(v) { sent.push(v as Record<string, unknown>); } };
  const client = createApplicationsClient(transport); client.start(); const token = '22222222222242228222222222222222';
  target.dispatchEvent(new MessageEvent('message', { data: { type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' } }));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    await act(async () => root.render(<ApplicationsPicker applications={client} enabled t={key => es[key]} add={add}/>)); await act(async () => vi.advanceTimersByTime(251));
    const disclosure = container.querySelector<HTMLDetailsElement>('.applications-picker')!;
    expect(disclosure.open).toBe(false); expect(sent).toHaveLength(0);
    expect(disclosure.querySelector('summary')?.textContent).toContain(es.catalogDisclosureHint);
    expect(en.catalogDisclosureHint).toBe('Search detected apps and add one as a rule.');
    const searchLabel = container.querySelector<HTMLLabelElement>('label[for="catalog-search"]')!;
    expect(searchLabel.classList.contains('sr-only')).toBe(false); expect(searchLabel.textContent).toContain(es.searchApplications);
    await act(async () => disclosure.querySelector('summary')!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })));
    await act(async () => vi.advanceTimersByTime(251));
    expect(disclosure.open).toBe(true);
    expect(sent).toHaveLength(1);
    const app = { id: '11111111111141118111111111111111', name: 'Known app', path: 'C:\\Apps\\known.exe', icon: null };
    await act(async () => target.dispatchEvent(new MessageEvent('message', { data: { version: 1, requestId: sent.at(-1)!.requestId, documentToken: token, error: null, result: { items: [app], offset: 0, total: 1, query: '' } } })));
    expect(container.textContent).toContain('Known app'); expect(container.textContent).toContain(app.path);
    await act(async () => { const select = container.querySelector('select')!; select.value = 'Allow'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    await act(async () => container.querySelector<HTMLButtonElement>('.catalog-apps button')!.click()); expect(add).toHaveBeenCalledWith(app, 'Allow');
    const firstSearchCount = sent.length;
    await act(async () => disclosure.querySelector('summary')!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })));
    await act(async () => disclosure.querySelector('summary')!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })));
    await act(async () => vi.advanceTimersByTime(251));
    expect(sent).toHaveLength(firstSearchCount);
    await act(async () => root.render(<ApplicationsPicker applications={client} enabled={false} t={key => es[key]} add={add}/>)); expect(container.querySelector<HTMLButtonElement>('.catalog-apps button')!.disabled).toBe(true);
  } finally { await act(async () => root.unmount()); client.stop(); container.remove(); vi.useRealTimers(); }
});

it('searches straight away inline and marks applications that are already rules instead of offering them again', async () => {
  vi.useFakeTimers(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const target = new EventTarget(); const sent: Record<string, unknown>[] = []; const add = vi.fn(async () => undefined);
  const transport: WebViewTransport = { addEventListener: target.addEventListener.bind(target), removeEventListener: target.removeEventListener.bind(target), postMessage(v) { sent.push(v as Record<string, unknown>); } };
  const client = createApplicationsClient(transport); client.start(); const token = '22222222222242228222222222222222';
  target.dispatchEvent(new MessageEvent('message', { data: { type: 'host.initialize', version: 1, documentToken: token, title: 'FocusGuard', description: 'Local', readyLabel: 'Ready', bridgeUnavailableLabel: 'Unavailable' } }));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  try {
    const known = { id: '11111111111141118111111111111111', name: 'Known app', path: 'C:\\Apps\\known.exe', icon: null };
    const other = { id: '33333333333343338333333333333333', name: 'Other app', path: 'C:\\Apps\\other.exe', icon: null };
    await act(async () => root.render(<ApplicationsPicker applications={client} enabled inline isAdded={app => app.id === known.id} t={key => es[key]} add={add}/>));
    expect(container.querySelector('details')).toBeNull();
    await act(async () => vi.advanceTimersByTime(251));
    expect(sent).toHaveLength(1);
    await act(async () => target.dispatchEvent(new MessageEvent('message', { data: { version: 1, requestId: sent.at(-1)!.requestId, documentToken: token, error: null, result: { items: [known, other], offset: 0, total: 2, query: '' } } })));
    const rows = container.querySelectorAll('.catalog-apps li');
    expect(rows[0]!.querySelector('button')).toBeNull();
    expect(rows[0]!.textContent).toContain(es.catalogAdded);
    await act(async () => rows[1]!.querySelector<HTMLButtonElement>('button')!.click());
    expect(add).toHaveBeenCalledWith(other, 'Block');
  } finally { await act(async () => root.unmount()); client.stop(); container.remove(); vi.useRealTimers(); }
});
