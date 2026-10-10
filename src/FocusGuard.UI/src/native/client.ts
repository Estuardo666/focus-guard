import {
  BRIDGE_PROTOCOL_VERSION,
  parseBridgeResponse,
  parseHostInitialization,
  type BridgeStatus,
  type ConnectionSnapshot,
  type WebViewTransport,
} from "./protocol";

const BOOTSTRAP_TIMEOUT_MS = 4_000;
const REQUEST_TIMEOUT_MS = 4_000;
const POLL_INTERVAL_MS = 2_000;
const REQUEST_WINDOW_MS = 60_000;
const MAXIMUM_REQUESTS_PER_WINDOW = 60;
const MINIMUM_REQUEST_GAP_MS = 1_000;

type MessageListener = (event: MessageEvent<unknown>) => void;

type PendingRequest = {
  requestId: string;
  documentToken: string;
  timer: ReturnType<typeof setTimeout>;
  promise: Promise<void>;
  resolve: () => void;
};

export type NativeStatusClient = {
  subscribe(listener: () => void): () => void;
  getSnapshot(): ConnectionSnapshot;
  start(): void;
  stop(): void;
  refresh(): Promise<void>;
};

function defaultWebViewTransport(): WebViewTransport | null {
  const hostGlobal = globalThis as typeof globalThis & {
    chrome?: { webview?: WebViewTransport };
  };
  return hostGlobal.chrome?.webview ?? null;
}

function secureGuid(): string {
  const cryptoProvider = globalThis.crypto;
  if (!cryptoProvider) throw new Error("Secure randomness unavailable");
  if (typeof cryptoProvider.randomUUID === "function") {
    return cryptoProvider.randomUUID().replaceAll("-", "").toLowerCase();
  }

  const bytes = cryptoProvider.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function currentDocument(): Document | null {
  return typeof document === "undefined" ? null : document;
}

function monotonicNow(): number {
  return globalThis.performance.now();
}

export function createNativeStatusClient(webview?: WebViewTransport | null): NativeStatusClient {
  const transport = webview === undefined ? defaultWebViewTransport() : webview;
  const listeners = new Set<() => void>();
  const sentAt: number[] = [];
  let snapshot: ConnectionSnapshot = Object.freeze({
    phase: "waiting",
    status: null,
    updatedAt: null,
    error: null,
  });
  let started = false;
  let documentToken: string | null = null;
  let pending: PendingRequest | null = null;
  let bootstrapTimer: ReturnType<typeof setTimeout> | null = null;
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let lastRequestFinishedAt: number | null = null;
  let messageListener: MessageListener | null = null;
  let visibilityDocument: Document | null = null;

  const publish = (next: ConnectionSnapshot): void => {
    if (
      snapshot.phase === next.phase && snapshot.status === next.status &&
      snapshot.updatedAt === next.updatedAt && snapshot.error === next.error
    ) return;
    snapshot = Object.freeze(next);
    for (const listener of listeners) listener();
  };

  const fail = (error: string): void => {
    const phase = error === "Unauthorized"
      ? "unauthorized"
      : error === "AuthorizationUnavailable"
        ? "unavailable"
        : snapshot.status
          ? "stale"
          : "unavailable";
    publish({ ...snapshot, phase, error });
  };

  const cancelPending = (): void => {
    if (!pending) return;
    const current = pending;
    pending = null;
    clearTimeout(current.timer);
    current.resolve();
  };

  const finishFailure = (request: PendingRequest, error: string): void => {
    if (pending !== request) return;
    pending = null;
    clearTimeout(request.timer);
      lastRequestFinishedAt = monotonicNow();
    fail(error);
    request.resolve();
  };

  const ensurePolling = (): void => {
    if (pollTimer !== null) return;
    pollTimer = setInterval(() => {
      if (visibilityDocument?.visibilityState === "hidden") return;
      if (pending || (lastRequestFinishedAt !== null && monotonicNow() - lastRequestFinishedAt < POLL_INTERVAL_MS)) return;
      void refresh();
    }, POLL_INTERVAL_MS);
  };

  const onVisibilityChange = (): void => {
    if (visibilityDocument?.visibilityState === "hidden" && snapshot.status) {
      publish({ ...snapshot, phase: "stale", error: "ViewHidden" });
    } else if (visibilityDocument?.visibilityState === "visible") void refresh();
  };

  const acceptInitialization = (token: string): void => {
    if (bootstrapTimer !== null) {
      clearTimeout(bootstrapTimer);
      bootstrapTimer = null;
    }
    const isNewDocument = documentToken !== token;
    if (isNewDocument) {
      cancelPending();
      documentToken = token;
      publish({
        ...snapshot,
        phase: snapshot.status ? "stale" : "loading",
        error: null,
      });
    }
    ensurePolling();
    void refresh();
  };

  const onMessage: MessageListener = (event): void => {
    if (!started) return;
    const initialization = parseHostInitialization(event.data);
    if (initialization) {
      acceptInitialization(initialization.documentToken);
      return;
    }

    const response = parseBridgeResponse(event.data);
    const current = pending;
    if (
      !response || !current ||
      response.requestId !== current.requestId ||
      response.documentToken !== current.documentToken ||
      response.documentToken !== documentToken
    ) return;

    pending = null;
    clearTimeout(current.timer);
    lastRequestFinishedAt = monotonicNow();
    if (response.error !== null) {
      fail(response.error);
    } else {
      const status: BridgeStatus = response.status!;
      const hidden = visibilityDocument?.visibilityState === "hidden";
      publish({ phase: hidden ? "stale" : "ready", status, updatedAt: Date.now(), error: hidden ? "ViewHidden" : null });
    }
    current.resolve();
  };

  async function refresh(): Promise<void> {
    if (!started || !transport || !documentToken) return;
    if (pending) return pending.promise;

    const now = monotonicNow();
    while (sentAt.length && now - sentAt[0]! >= REQUEST_WINDOW_MS) sentAt.shift();
    if (
      sentAt.length >= MAXIMUM_REQUESTS_PER_WINDOW ||
      (sentAt.length > 0 && now - sentAt[sentAt.length - 1]! < MINIMUM_REQUEST_GAP_MS)
    ) return;

    let requestId: string;
    try {
      requestId = secureGuid();
    } catch {
      fail("SecureRandomUnavailable");
      return;
    }

    let resolve!: () => void;
    const promise = new Promise<void>((done) => { resolve = done; });
    const request: PendingRequest = {
      requestId,
      documentToken,
      timer: setTimeout(() => finishFailure(request, "RequestTimeout"), REQUEST_TIMEOUT_MS),
      promise,
      resolve,
    };
    pending = request;
    sentAt.push(now);
    if (!snapshot.status && snapshot.phase !== "loading") {
      publish({ ...snapshot, phase: "loading", error: null });
    }

    try {
      transport.postMessage({
        version: BRIDGE_PROTOCOL_VERSION,
        requestId,
        documentToken,
        method: "status.get",
        params: {},
      });
    } catch {
      finishFailure(request, "BridgePostFailed");
    }
    return promise;
  }

  return {
    subscribe(listener): () => void {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    getSnapshot(): ConnectionSnapshot {
      return snapshot;
    },

    start(): void {
      if (started) return;
      started = true;
      if (!transport) {
        fail("WebViewBridgeUnavailable");
        return;
      }

      messageListener = onMessage;
      try {
        transport.addEventListener("message", messageListener as EventListener);
      } catch {
        messageListener = null;
        fail("WebViewBridgeUnavailable");
        return;
      }

      visibilityDocument = currentDocument();
      visibilityDocument?.addEventListener("visibilitychange", onVisibilityChange);
      publish({ ...snapshot, phase: snapshot.status ? "stale" : "waiting", error: null });
      bootstrapTimer = setTimeout(() => {
        bootstrapTimer = null;
        if (!documentToken) fail("BridgeInitializationTimeout");
      }, BOOTSTRAP_TIMEOUT_MS);
    },

    stop(): void {
      if (!started) return;
      started = false;
      if (bootstrapTimer !== null) clearTimeout(bootstrapTimer);
      if (pollTimer !== null) clearInterval(pollTimer);
      bootstrapTimer = null;
      pollTimer = null;
      if (messageListener && transport) {
        transport.removeEventListener("message", messageListener as EventListener);
      }
      messageListener = null;
      visibilityDocument?.removeEventListener("visibilitychange", onVisibilityChange);
      visibilityDocument = null;
      documentToken = null;
      cancelPending();
      publish({
        ...snapshot,
        phase: snapshot.status ? "stale" : "waiting",
        error: snapshot.status ? "ClientStopped" : null,
      });
    },

    refresh,
  };
}
