/**
 * The page side of the installer bridge. The installer runs elevated, so the page can only ask for a handful of
 * fixed actions; the host validates every message again and owns all decisions (consent, which processes, scripts).
 */
export type SetupOperation = 'install' | 'uninstall';
export type HostMessage =
  | Readonly<{ type: 'setup.state'; version: string; installed: boolean }>
  | Readonly<{ type: 'setup.blockers'; apps: readonly string[] }>
  | Readonly<{ type: 'setup.log'; line: string }>
  | Readonly<{ type: 'setup.done'; operation: SetupOperation; ok: boolean; exitCode: number }>;
export type PageMessage =
  | Readonly<{ type: 'setup.hello' }>
  | Readonly<{ type: 'setup.install'; consent: true }>
  | Readonly<{ type: 'setup.uninstall' }>
  | Readonly<{ type: 'setup.blockers'; close: boolean }>
  | Readonly<{ type: 'setup.open' }>
  | Readonly<{ type: 'setup.close' }>
  | Readonly<{ type: 'setup.minimize' }>
  | Readonly<{ type: 'setup.drag' }>;

export type SetupTransport = Pick<EventTarget, 'addEventListener' | 'removeEventListener'> & { postMessage(message: unknown): void };

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.length <= max;

/** Accepts only the four host messages, with exact fields and bounded text. */
export function parseHostMessage(value: unknown): HostMessage | null {
  if (!isRecord(value)) return null;
  switch (value.type) {
    case 'setup.state':
      return exact(value, ['type', 'version', 'installed']) && text(value.version, 32) && /^\d+(\.\d+){1,3}$/.test(value.version) && typeof value.installed === 'boolean'
        ? { type: 'setup.state', version: value.version, installed: value.installed } : null;
    case 'setup.blockers':
      return exact(value, ['type', 'apps']) && Array.isArray(value.apps) && value.apps.length <= 16 && value.apps.every(app => text(app, 120) && app.length > 0)
        ? { type: 'setup.blockers', apps: value.apps as string[] } : null;
    case 'setup.log':
      return exact(value, ['type', 'line']) && text(value.line, 2000) ? { type: 'setup.log', line: value.line } : null;
    case 'setup.done':
      return exact(value, ['type', 'operation', 'ok', 'exitCode']) && (value.operation === 'install' || value.operation === 'uninstall')
        && typeof value.ok === 'boolean' && Number.isSafeInteger(value.exitCode)
        ? { type: 'setup.done', operation: value.operation, ok: value.ok, exitCode: value.exitCode as number } : null;
    default: return null;
  }
}

export function defaultSetupTransport(): SetupTransport | null {
  const hostGlobal = globalThis as typeof globalThis & { chrome?: { webview?: SetupTransport } };
  return hostGlobal.chrome?.webview ?? null;
}
