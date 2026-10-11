import { useEffect, useRef, useState } from 'react';
import { CheckIcon, Cross2Icon, ExclamationTriangleIcon, LockClosedIcon, MinusIcon, ReloadIcon } from '@radix-ui/react-icons';
import { parseHostMessage, type PageMessage, type SetupOperation, type SetupTransport } from './bridge';
import { setupCopy, type SetupCopy } from './copy';

type Screen =
  | { name: 'welcome' }
  | { name: 'confirmUninstall' }
  | { name: 'blockers'; apps: readonly string[] }
  | { name: 'working'; operation: SetupOperation }
  | { name: 'done'; operation: SetupOperation }
  | { name: 'failed'; operation: SetupOperation; exitCode: number };

/**
 * The FocusGuard installer, in the app's zen language. The page never decides anything that matters on its own:
 * it shows consequences, collects the explicit consent, and asks the elevated host to act.
 */
export function SetupApp({ transport, language }: { transport: SetupTransport | null; language: 'es' | 'en' }) {
  const t = setupCopy[language];
  const [screen, setScreen] = useState<Screen>({ name: 'welcome' });
  const [version, setVersion] = useState<string | null>(null);
  const [installed, setInstalled] = useState(false);
  const [consent, setConsent] = useState(false);
  const [log, setLog] = useState<string[]>([]);
  const [showLog, setShowLog] = useState(false);
  const operation = useRef<SetupOperation>('install');
  const send = (message: PageMessage) => { try { transport?.postMessage(message); } catch { /* the host window is closing */ } };

  useEffect(() => {
    if (!transport) return;
    const onMessage = (event: Event) => {
      const message = parseHostMessage((event as MessageEvent<unknown>).data);
      if (!message) return;
      switch (message.type) {
        case 'setup.state': setVersion(message.version); setInstalled(message.installed); break;
        case 'setup.blockers': setScreen({ name: 'blockers', apps: message.apps }); break;
        case 'setup.log': setLog(lines => [...lines.slice(-399), message.line]); break;
        case 'setup.done':
          setScreen(message.ok ? { name: 'done', operation: message.operation } : { name: 'failed', operation: message.operation, exitCode: message.exitCode });
          if (message.ok) setInstalled(message.operation === 'install');
          break;
      }
    };
    transport.addEventListener('message', onMessage);
    transport.postMessage({ type: 'setup.hello' } satisfies PageMessage);
    return () => transport.removeEventListener('message', onMessage);
  }, [transport]);

  const start = (kind: SetupOperation) => {
    operation.current = kind;
    setLog([]);
    setShowLog(false);
    setScreen({ name: 'working', operation: kind });
    send(kind === 'install' ? { type: 'setup.install', consent: true } : { type: 'setup.uninstall' });
  };

  const lastLine = [...log].reverse().find(line => line.trim().length > 0) ?? t.preparing;
  return <div className="setup-shell" data-screen={screen.name}>
    <div className="atmosphere" aria-hidden="true"><span/><span/><span/></div>
    <header className="setup-titlebar" onMouseDown={event => { if (event.button === 0 && event.target === event.currentTarget) send({ type: 'setup.drag' }); }}>
      <span className="setup-brand"><span className="setup-brand-orb" aria-hidden="true"/>FocusGuard</span>
      <span className="setup-window-buttons">
        <button type="button" aria-label={t.minimize} onClick={() => send({ type: 'setup.minimize' })}><MinusIcon aria-hidden="true"/></button>
        <button type="button" aria-label={t.close} disabled={screen.name === 'working'} onClick={() => send({ type: 'setup.close' })}><Cross2Icon aria-hidden="true"/></button>
      </span>
    </header>
    <main className="setup-stage" key={screen.name}>
      {screen.name === 'welcome' && <section className="setup-card" aria-labelledby="setup-title">
        <div className="setup-orb" aria-hidden="true"><span/></div>
        <h1 id="setup-title">{installed ? t.updateTitle : t.installTitle}</h1>
        <p className="setup-lead">{t.installLead}</p>
        {version && version !== '0.0.0' && <p className="setup-version">{t.version.replace('{version}', version)}</p>}
        <label className="setup-consent">
          <input type="checkbox" checked={consent} onChange={event => setConsent(event.target.checked)}/>
          <span><strong>{t.consentTitle}</strong><small>{t.consentDetail}</small></span>
        </label>
        <p className="setup-note"><LockClosedIcon aria-hidden="true"/>{t.adminNote}</p>
        <div className="setup-actions">
          <button type="button" className="setup-primary" disabled={!consent} onClick={() => start('install')}>{installed ? t.update : t.install}</button>
          {installed && <button type="button" className="setup-quiet" onClick={() => setScreen({ name: 'confirmUninstall' })}>{t.uninstall}</button>}
        </div>
      </section>}

      {screen.name === 'confirmUninstall' && <section className="setup-card" aria-labelledby="setup-title">
        <div className="setup-icon" data-tone="warning" aria-hidden="true"><ExclamationTriangleIcon/></div>
        <h1 id="setup-title">{t.uninstallTitle}</h1>
        <p className="setup-lead">{t.uninstallLead}</p>
        <div className="setup-actions">
          <button type="button" className="setup-primary setup-danger" onClick={() => start('uninstall')}>{t.uninstallConfirm}</button>
          <button type="button" className="setup-secondary" onClick={() => setScreen({ name: 'welcome' })}>{t.cancel}</button>
        </div>
      </section>}

      {screen.name === 'blockers' && <section className="setup-card" aria-labelledby="setup-title">
        <div className="setup-icon" data-tone="warning" aria-hidden="true"><ExclamationTriangleIcon/></div>
        <h1 id="setup-title">{t.blockersTitle}</h1>
        <p className="setup-lead">{t.blockersLead}</p>
        <ul className="setup-blockers">{screen.apps.map(app => <li key={app}>{app}</li>)}</ul>
        <div className="setup-actions">
          <button type="button" className="setup-primary" onClick={() => { setScreen({ name: 'working', operation: operation.current }); send({ type: 'setup.blockers', close: true }); }}>{t.blockersClose}</button>
          <button type="button" className="setup-secondary" onClick={() => { setScreen({ name: 'welcome' }); send({ type: 'setup.blockers', close: false }); }}>{t.cancel}</button>
        </div>
      </section>}

      {screen.name === 'working' && <section className="setup-card" aria-labelledby="setup-title" aria-busy="true">
        <div className="setup-orb setup-orb-working" aria-hidden="true"><span/><i/></div>
        <h1 id="setup-title">{screen.operation === 'install' ? t.installing : t.uninstalling}</h1>
        <p className="setup-lead setup-live" role="status" aria-live="polite">{lastLine}</p>
        <p className="setup-note">{t.workingNote}</p>
        <LogPanel log={log} open={showLog} toggle={() => setShowLog(value => !value)} t={t}/>
      </section>}

      {screen.name === 'done' && <section className="setup-card" aria-labelledby="setup-title">
        <div className="setup-medal" aria-hidden="true"><span className="setup-burst">{Array.from({ length: 12 }, (_, index) => <i key={index} style={{ '--i': index } as React.CSSProperties}/>)}</span><span className="setup-medal-core"><CheckIcon/></span></div>
        <h1 id="setup-title">{screen.operation === 'install' ? t.doneTitle : t.removedTitle}</h1>
        <p className="setup-lead">{screen.operation === 'install' ? t.doneLead : t.removedLead}</p>
        <div className="setup-actions">
          {screen.operation === 'install' && <button type="button" className="setup-primary" autoFocus onClick={() => send({ type: 'setup.open' })}>{t.open}</button>}
          <button type="button" className={screen.operation === 'install' ? 'setup-secondary' : 'setup-primary'} onClick={() => send({ type: 'setup.close' })}>{t.exit}</button>
        </div>
      </section>}

      {screen.name === 'failed' && <section className="setup-card" aria-labelledby="setup-title">
        <div className="setup-icon" data-tone="error" aria-hidden="true"><ExclamationTriangleIcon/></div>
        <h1 id="setup-title">{t.failedTitle}</h1>
        <p className="setup-lead" role="alert">{(screen.operation === 'install' ? t.failedLead : t.failedUninstallLead).replace('{code}', String(screen.exitCode))}</p>
        <LogPanel log={log} open={showLog} toggle={() => setShowLog(value => !value)} t={t}/>
        <div className="setup-actions">
          <button type="button" className="setup-primary" onClick={() => setScreen({ name: 'welcome' })}><ReloadIcon aria-hidden="true"/>{t.retry}</button>
          <button type="button" className="setup-secondary" onClick={() => send({ type: 'setup.close' })}>{t.exit}</button>
        </div>
      </section>}
    </main>
    <footer className="setup-footer">{t.footer}</footer>
  </div>;
}

function LogPanel({ log, open, toggle, t }: { log: readonly string[]; open: boolean; toggle(): void; t: SetupCopy }) {
  const panel = useRef<HTMLPreElement>(null);
  useEffect(() => { if (open && panel.current) panel.current.scrollTop = panel.current.scrollHeight; }, [log, open]);
  return <div className="setup-log">
    <button type="button" className="setup-quiet" aria-expanded={open} onClick={toggle}>{open ? t.hideDetails : t.showDetails}</button>
    {open && <pre ref={panel} tabIndex={0} aria-label={t.details}>{log.join('\n')}</pre>}
  </div>;
}
