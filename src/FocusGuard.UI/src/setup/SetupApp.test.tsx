import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { SetupApp } from './SetupApp';
import { parseHostMessage } from './bridge';
import { setupCopy } from './copy';

const t = setupCopy.en;
let root: Root;
let container: HTMLDivElement;
let sent: Record<string, unknown>[];
let target: EventTarget;

function render() {
  target = new EventTarget();
  sent = [];
  const transport = {
    addEventListener: target.addEventListener.bind(target),
    removeEventListener: target.removeEventListener.bind(target),
    postMessage(message: unknown) { sent.push(message as Record<string, unknown>); },
  };
  act(() => root.render(<SetupApp transport={transport} language="en"/>));
}
const host = (data: unknown) => act(() => { target.dispatchEvent(new MessageEvent('message', { data })); });
const button = (name: string) => [...container.querySelectorAll('button')].find(item => item.textContent?.includes(name));

beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); });

it('accepts only the exact host messages', () => {
  expect(parseHostMessage({ type: 'setup.openState', opening: true, failed: false })).not.toBeNull();
  expect(parseHostMessage({ type: 'setup.openState', opening: true, failed: true })).toBeNull();
  expect(parseHostMessage({ type: 'setup.openState', opening: false, failed: false, command: 'x' })).toBeNull();
  expect(parseHostMessage({ type: 'setup.state', version: '0.1.142', installed: true })).not.toBeNull();
  expect(parseHostMessage({ type: 'setup.state', version: '<b>', installed: true })).toBeNull();
  expect(parseHostMessage({ type: 'setup.state', version: '0.1.142', installed: true, extra: 1 })).toBeNull();
  expect(parseHostMessage({ type: 'setup.blockers', apps: ['FocusGuard UI'] })).not.toBeNull();
  expect(parseHostMessage({ type: 'setup.blockers', apps: [''] })).toBeNull();
  expect(parseHostMessage({ type: 'setup.done', operation: 'format', ok: true, exitCode: 0 })).toBeNull();
  expect(parseHostMessage({ type: 'setup.run', command: 'x' })).toBeNull();
});

it('says hello, shows the version and installs only after explicit consent', () => {
  render();
  expect(sent).toEqual([{ type: 'setup.hello' }]);
  host({ type: 'setup.state', version: '0.1.142', installed: false });
  expect(container.textContent).toContain(t.version.replace('{version}', '0.1.142'));
  const install = button(t.install)!;
  expect(install.disabled).toBe(true);
  act(() => { container.querySelector<HTMLInputElement>('.setup-consent input')!.click(); });
  expect(install.disabled).toBe(false);
  act(() => { install.click(); });
  expect(sent.at(-1)).toEqual({ type: 'setup.install', consent: true });
  expect(container.querySelector('h1')?.textContent).toBe(t.installing);
  expect(button(t.close)).toBeUndefined();
  expect((container.querySelector(`button[aria-label="${t.close}"]`) as HTMLButtonElement).disabled).toBe(true);
});

it('asks before closing programs and reports the outcome', () => {
  render();
  host({ type: 'setup.state', version: '0.1.142', installed: true });
  expect(container.querySelector('h1')?.textContent).toBe(t.updateTitle);
  act(() => { container.querySelector<HTMLInputElement>('.setup-consent input')!.click(); });
  act(() => { button(t.update)!.click(); });
  host({ type: 'setup.blockers', apps: ['FocusGuard UI'] });
  expect(container.textContent).toContain('FocusGuard UI');
  act(() => { button(t.blockersClose)!.click(); });
  expect(sent.at(-1)).toEqual({ type: 'setup.blockers', close: true });
  host({ type: 'setup.log', line: 'Copying files' });
  expect(container.querySelector('[role="status"]')?.textContent).toBe('Copying files');
  host({ type: 'setup.done', operation: 'install', ok: true, exitCode: 0 });
  expect(container.querySelector('h1')?.textContent).toBe(t.doneTitle);
  act(() => { button(t.open)!.click(); });
  expect(sent.at(-1)).toEqual({ type: 'setup.open' });
});

it('explains a failure with its code and offers the details and a retry', () => {
  render();
  host({ type: 'setup.state', version: '0.1.142', installed: false });
  act(() => { container.querySelector<HTMLInputElement>('.setup-consent input')!.click(); });
  act(() => { button(t.install)!.click(); });
  host({ type: 'setup.log', line: 'FocusGuard has an active session.' });
  host({ type: 'setup.done', operation: 'install', ok: false, exitCode: 1 });
  expect(container.querySelector('[role="alert"]')?.textContent).toBe(t.failedLead.replace('{code}', '1'));
  act(() => { button(t.showDetails)!.click(); });
  expect(container.querySelector('pre')?.textContent).toContain('active session');
  act(() => { button(t.retry)!.click(); });
  expect(container.querySelector('h1')?.textContent).toBe(t.installTitle);
});

it('keeps the completed installer available when the app window failed to open and allows retry', () => {
  render();
  host({ type: 'setup.done', operation: 'install', ok: true, exitCode: 0 });
  act(() => button(t.open)!.click());
  expect(button(t.opening)!.disabled).toBe(true);
  host({ type: 'setup.openState', opening: false, failed: true });
  expect(container.querySelector('[role=alert]')?.textContent).toBe(t.openFailed);
  expect(button(t.open)!.disabled).toBe(false);
  expect(container.querySelector('h1')?.textContent).toBe(t.doneTitle);
  act(() => button(t.open)!.click());
  expect(sent.at(-1)).toEqual({ type: 'setup.open' });
});

it('confirms uninstall with its consequences before asking the host', () => {
  render();
  host({ type: 'setup.state', version: '0.1.142', installed: true });
  act(() => { button(t.uninstall)!.click(); });
  expect(container.textContent).toContain(t.uninstallLead);
  expect(sent.some(message => message.type === 'setup.uninstall')).toBe(false);
  act(() => { button(t.uninstallConfirm)!.click(); });
  expect(sent.at(-1)).toEqual({ type: 'setup.uninstall' });
});
