export const BRIDGE_PROTOCOL_VERSION = 1;
export const MAXIMUM_RESPONSE_BYTES = 8 * 1024;
export const MAXIMUM_REMAINING_SECONDS = 30 * 24 * 60 * 60;

export type BridgePhase = Readonly<{ index: number; kind: 'Focus' | 'ShortBreak' | 'LongBreak'; focusCycleNumber: number; remainingSeconds: number; endUtc: string; startUtc: string | null }>;

export type BridgeStatus = Readonly<{
  serviceReady: true;
  extensionReady: boolean;
  sessionState: string;
  remainingSeconds: number;
  sessionId: string | null;
  profileId: string | null;
  startUtc: string | null;
  endUtc: string | null;
  strictMode: boolean;
  enforcementConnected: boolean;
  mode: "SimpleTimer" | "FocusCycle";
  currentPhase?: BridgePhase | null;
  nextPhase?: BridgePhase | null;
  finalEndUtc?: string | null;
}>;

export type ConnectionSnapshot = Readonly<{
  phase: "waiting" | "loading" | "ready" | "stale" | "unavailable" | "unauthorized";
  status: BridgeStatus | null;
  updatedAt: number | null;
  error: string | null;
}>;

export type WebViewTransport = Pick<EventTarget, "addEventListener" | "removeEventListener"> & {
  postMessage(message: unknown): void;
};

export type HostInitialization = Readonly<{
  type: "host.initialize";
  version: 1;
  documentToken: string;
}>;

export type HostWindowPresentation = Readonly<{ compact: boolean; alwaysOnTop: boolean; detailsExpanded: boolean }>;
export type HostWindowStateMessage = Readonly<{
  type: "host.windowState";
  version: 1;
  documentToken: string;
  compact: boolean;
  alwaysOnTop: boolean;
  detailsExpanded: boolean;
}>;

export type BridgeResponse = Readonly<{
  version: 1;
  requestId: string;
  documentToken: string;
  error: string | null;
  status: BridgeStatus | null;
}>;

const DOCUMENT_TOKEN = /^(?!0{32})[0-9a-f]{32}$/i;
const REQUEST_ID = /^(?!0{32})[0-9a-f]{32}$/i;
const GUID = /^(?!0{8}-0{4}-0{4}-0{4}-0{12})[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SESSION_STATES = new Set([
  "Draft",
  "Activating",
  "Active",
  "Expired",
  "Stopped",
  "RecoveryPending",
  "Recovered",
  "Failed",
]);
const INITIALIZATION_KEYS = ["type", "version", "documentToken", "title", "description", "readyLabel", "bridgeUnavailableLabel"];
const WINDOW_STATE_KEYS = ["type", "version", "documentToken", "compact", "alwaysOnTop", "detailsExpanded"];
const RESPONSE_KEYS = ["version", "requestId", "documentToken", "error", "status"];
const STATUS_KEYS = [
  "serviceReady",
  "extensionReady",
  "sessionState",
  "remainingSeconds",
  "sessionId",
  "profileId",
  "startUtc",
  "endUtc",
  "strictMode",
  "enforcementConnected",
  "mode",
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const actual = Object.keys(value);
  return actual.length === expected.length && expected.every((key) => Object.hasOwn(value, key));
}

function boundedRecord(value: unknown): Record<string, unknown> | null {
  try {
    let json: string;
    if (typeof value === "string") {
      if (value.length > MAXIMUM_RESPONSE_BYTES) return null;
      json = value;
    } else {
      const encoded = JSON.stringify(value);
      if (typeof encoded !== "string") return null;
      json = encoded;
      if (json.length > MAXIMUM_RESPONSE_BYTES) return null;
    }
    if (new TextEncoder().encode(json).byteLength > MAXIMUM_RESPONSE_BYTES) return null;
    const parsed: unknown = typeof value === "string" ? JSON.parse(json) : value;
    return isRecord(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function isShortString(value: unknown, maximum: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maximum;
}

export function utcMilliseconds(value: unknown): number | null {
  if (typeof value !== "string") return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(?:Z|\+00:00)$/.exec(value);
  if (!match) return null;
  const [, yearText, monthText, dayText, hourText, minuteText, secondText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);
  const second = Number(secondText);
  if (month < 1 || month > 12 || hour > 23 || minute > 59 || second > 59) return null;
  const check = new Date(0);
  check.setUTCFullYear(year, month - 1, day);
  check.setUTCHours(hour, minute, second, 0);
  if (check.getUTCFullYear() !== year || check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) ? milliseconds : null;
}

function isBridgeStatus(value: unknown): value is BridgeStatus {
  if (!isRecord(value) || !(hasExactKeys(value, STATUS_KEYS) || hasExactKeys(value, [...STATUS_KEYS, 'currentPhase', 'nextPhase', 'finalEndUtc']))) return false;
  if (Object.hasOwn(value, 'currentPhase') && !validPhaseProjection(value)) return false;
  if (
    value.serviceReady !== true ||
    typeof value.extensionReady !== "boolean" ||
    typeof value.sessionState !== "string" ||
    !Number.isSafeInteger(value.remainingSeconds) ||
    (value.remainingSeconds as number) < 0 ||
    (value.remainingSeconds as number) > MAXIMUM_REMAINING_SECONDS ||
    typeof value.strictMode !== "boolean" ||
    typeof value.enforcementConnected !== "boolean" ||
    (value.mode !== "SimpleTimer" && value.mode !== "FocusCycle")
  ) {
    return false;
  }

  if (value.sessionId === null) {
    return value.sessionState === "Inactive" &&
      value.remainingSeconds === 0 &&
      value.profileId === null &&
      value.startUtc === null &&
      value.endUtc === null &&
      value.strictMode === false &&
      value.enforcementConnected === false &&
      value.mode === "SimpleTimer";
  }

  if (
    typeof value.sessionId !== "string" || !GUID.test(value.sessionId) ||
    typeof value.profileId !== "string" || !GUID.test(value.profileId) ||
    !SESSION_STATES.has(value.sessionState)
  ) {
    return false;
  }

  const start = utcMilliseconds(value.startUtc);
  const end = utcMilliseconds(value.endUtc);
  return start !== null && end !== null && start < end;
}

function validPhaseProjection(status: Record<string, unknown>): boolean {
  const start = utcMilliseconds(status.startUtc), end = utcMilliseconds(status.endUtc);
  const phase = (value: unknown): value is BridgePhase | null => {
    if (value === null) return true;
    if (!isRecord(value) || !hasExactKeys(value, ['index', 'kind', 'focusCycleNumber', 'remainingSeconds', 'endUtc', 'startUtc'])
      || !Number.isSafeInteger(value.index) || (value.index as number) < 0 || (value.index as number) > 48
      || !['Focus', 'ShortBreak', 'LongBreak'].includes(value.kind as string)
      || !Number.isSafeInteger(value.focusCycleNumber) || (value.focusCycleNumber as number) < 1 || (value.focusCycleNumber as number) > 25
      || !Number.isSafeInteger(value.remainingSeconds) || (value.remainingSeconds as number) < 0 || (value.remainingSeconds as number) > MAXIMUM_REMAINING_SECONDS) return false;
    const phaseEnd = utcMilliseconds(value.endUtc), phaseStart = utcMilliseconds(value.startUtc);
    return start !== null && end !== null && phaseEnd !== null && phaseEnd <= end && phaseEnd > start
      && (value.startUtc === null || phaseStart !== null && phaseStart >= start && phaseStart < phaseEnd);
  };
  if (!phase(status.currentPhase) || !phase(status.nextPhase)) return false;
  if (status.mode === 'SimpleTimer' && (status.currentPhase !== null || status.nextPhase !== null || status.finalEndUtc !== null)) return false;
  const final = utcMilliseconds(status.finalEndUtc);
  if (status.finalEndUtc !== null && (final === null || end === null || final < end)) return false;
  return !(status.currentPhase && status.nextPhase && (status.nextPhase.index <= status.currentPhase.index
    || status.nextPhase.startUtc !== null && Date.parse(status.nextPhase.startUtc) < Date.parse(status.currentPhase.endUtc)));
}

export function parseHostInitialization(value: unknown): HostInitialization | null {
  const message = boundedRecord(value);
  if (!message || !hasExactKeys(message, INITIALIZATION_KEYS)) return null;
  if (
    message.type !== "host.initialize" ||
    message.version !== BRIDGE_PROTOCOL_VERSION ||
    typeof message.documentToken !== "string" || !DOCUMENT_TOKEN.test(message.documentToken) ||
    !isShortString(message.title, 256) ||
    !isShortString(message.description, 1024) ||
    !isShortString(message.readyLabel, 256) ||
    !isShortString(message.bridgeUnavailableLabel, 256)
  ) {
    return null;
  }
  return { type: "host.initialize", version: 1, documentToken: message.documentToken };
}

export function parseHostWindowState(value: unknown): HostWindowStateMessage | null {
  const message = boundedRecord(value);
  if (!message || !hasExactKeys(message, WINDOW_STATE_KEYS)) return null;
  if (message.type !== "host.windowState" || message.version !== BRIDGE_PROTOCOL_VERSION
    || typeof message.documentToken !== "string" || !DOCUMENT_TOKEN.test(message.documentToken)
    || typeof message.compact !== "boolean" || typeof message.alwaysOnTop !== "boolean"
    || typeof message.detailsExpanded !== "boolean") return null;
  return { type: "host.windowState", version: 1, documentToken: message.documentToken,
    compact: message.compact, alwaysOnTop: message.alwaysOnTop, detailsExpanded: message.detailsExpanded };
}

export function parseBridgeResponse(value: unknown): BridgeResponse | null {
  const message = boundedRecord(value);
  if (!message || !hasExactKeys(message, RESPONSE_KEYS)) return null;
  if (
    message.version !== BRIDGE_PROTOCOL_VERSION ||
    typeof message.requestId !== "string" || !REQUEST_ID.test(message.requestId) ||
    typeof message.documentToken !== "string" || !DOCUMENT_TOKEN.test(message.documentToken)
  ) {
    return null;
  }

  if (message.error === null && isBridgeStatus(message.status)) {
    return {
      version: 1,
      requestId: message.requestId,
      documentToken: message.documentToken,
      error: null,
      status: Object.freeze({ ...message.status, ...(message.status.currentPhase === undefined ? {} : {
        currentPhase: message.status.currentPhase ? Object.freeze({ ...message.status.currentPhase }) : null,
        nextPhase: message.status.nextPhase ? Object.freeze({ ...message.status.nextPhase }) : null,
      }) }),
    };
  }
  if (isShortString(message.error, 64) && /^[A-Za-z][A-Za-z0-9]*$/.test(message.error) && message.status === null) {
    return {
      version: 1,
      requestId: message.requestId,
      documentToken: message.documentToken,
      error: message.error,
      status: null,
    };
  }
  return null;
}
