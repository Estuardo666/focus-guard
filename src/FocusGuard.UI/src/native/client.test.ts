import { afterEach, describe, expect, it, vi } from "vitest";
import { createNativeStatusClient } from "./client";
import type { WebViewTransport } from "./protocol";

class FakeWebView extends EventTarget implements WebViewTransport {
  readonly posted: unknown[] = [];

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  send(data: unknown): void {
    const event = new Event("message");
    Object.defineProperty(event, "data", { value: data });
    this.dispatchEvent(event);
  }
}

const tokenA = "1234567890abcdef1234567890abcdef";
const tokenB = "abcdef1234567890abcdef1234567890";

function initialize(webview: FakeWebView, token = tokenA): void {
  webview.send({
    type: "host.initialize",
    version: 1,
    documentToken: token,
    title: "FocusGuard",
    description: "Status shell",
    readyLabel: "Ready",
    bridgeUnavailableLabel: "Unavailable",
  });
}

function currentRequest(webview: FakeWebView): Record<string, unknown> {
  const request = webview.posted.at(-1);
  expect(request).toMatchObject({ version: 1, method: "status.get", params: {} });
  expect((request as Record<string, unknown>).requestId).toMatch(/^[0-9a-f]{32}$/);
  return request as Record<string, unknown>;
}

function status(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    serviceReady: true,
    extensionReady: true,
    sessionState: "Active",
    remainingSeconds: 120,
    sessionId: "10000000-0000-4000-8000-000000000001",
    profileId: "20000000-0000-4000-8000-000000000002",
    startUtc: "2026-10-04T12:00:00+00:00",
    endUtc: "2026-10-04T12:02:00+00:00",
    strictMode: false,
    enforcementConnected: true,
    mode: "SimpleTimer",
    ...overrides,
  };
}

function reply(
  webview: FakeWebView,
  request: Record<string, unknown>,
  result: { error?: string; status?: Record<string, unknown> } = { status: status() },
): void {
  webview.send({
    version: 1,
    requestId: request.requestId,
    documentToken: request.documentToken,
    error: result.error ?? null,
    status: result.error ? null : result.status,
  });
}

afterEach(() => vi.useRealTimers());

describe("native status transport", () => {
  it("marks a hidden view stale, including late replies, until a fresh visible reading arrives", () => {
    vi.useFakeTimers();
    const descriptor = Object.getOwnPropertyDescriptor(document, "visibilityState");
    let visibility: DocumentVisibilityState = "visible";
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => visibility });
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    try {
      client.start(); initialize(webview); reply(webview, currentRequest(webview));
      const confirmed = client.getSnapshot().status;
      vi.advanceTimersByTime(2000);
      visibility = "hidden";
      document.dispatchEvent(new Event("visibilitychange"));
      expect(client.getSnapshot()).toMatchObject({ phase: "stale", error: "ViewHidden", status: confirmed });
      reply(webview, currentRequest(webview), { status: status({ remainingSeconds: 119 }) });
      expect(client.getSnapshot().phase).toBe("stale");
      vi.advanceTimersByTime(10000);
      expect(webview.posted).toHaveLength(2);
      visibility = "visible";
      document.dispatchEvent(new Event("visibilitychange"));
      expect(webview.posted).toHaveLength(3);
      expect(client.getSnapshot().phase).toBe("stale");
      reply(webview, currentRequest(webview), { status: status({ remainingSeconds: 110 }) });
      expect(client.getSnapshot()).toMatchObject({ phase: "ready", error: null, status: { remainingSeconds: 110, sessionId: confirmed!.sessionId } });
    } finally {
      client.stop();
      if (descriptor) Object.defineProperty(document, "visibilityState", descriptor);
      else Reflect.deleteProperty(document, "visibilityState");
    }
  });

  it("registers before bootstrap and accepts a correlated status response", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-04T12:00:00Z"));
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();

    expect(client.getSnapshot().phase).toBe("waiting");
    initialize(webview);
    const request = currentRequest(webview);
    expect(request.documentToken).toBe(tokenA);
    reply(webview, request);

    expect(client.getSnapshot()).toMatchObject({
      phase: "ready",
      status: { sessionState: "Active", remainingSeconds: 120 },
      updatedAt: Date.now(),
      error: null,
    });
    client.stop();
  });

  it("keeps the confirmed snapshot ready while the next background poll is pending", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    const notify = vi.fn();
    client.subscribe(notify);
    client.start();
    client.start();
    initialize(webview);
    reply(webview, currentRequest(webview));
    const confirmed = client.getSnapshot();
    expect(confirmed.phase).toBe("ready");
    notify.mockClear();

    vi.advanceTimersByTime(2_000);

    expect(webview.posted).toHaveLength(2);
    expect(client.getSnapshot()).toBe(confirmed);
    expect(client.getSnapshot().phase).toBe("ready");
    expect(notify).not.toHaveBeenCalled();

    reply(webview, currentRequest(webview), { status: status({ remainingSeconds: 119 }) });
    expect(client.getSnapshot()).toMatchObject({ phase: "ready", status: { remainingSeconds: 119 } });
    expect(notify).toHaveBeenCalledTimes(1);
    client.stop();
  });

  it("continues polling on monotonic time after a wall-clock rollback and marks a timed-out reading stale", () => {
    vi.useFakeTimers({ toFake: ["Date", "setTimeout", "clearTimeout", "setInterval", "clearInterval", "performance"] });
    vi.setSystemTime(new Date("2026-10-04T12:00:00Z"));
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    reply(webview, currentRequest(webview));
    const confirmed = client.getSnapshot();
    const monotonicBeforeRollback = performance.now();
    vi.setSystemTime(new Date("2020-10-04T12:00:00Z"));
    expect(performance.now()).toBe(monotonicBeforeRollback);

    vi.advanceTimersByTime(2_000);
    expect(webview.posted).toHaveLength(2);
    vi.advanceTimersByTime(4_000);

    expect(client.getSnapshot()).toMatchObject({
      phase: "stale",
      status: confirmed.status,
      updatedAt: confirmed.updatedAt,
      error: "RequestTimeout",
    });
    expect(webview.posted).toHaveLength(2);
    client.stop();
  });

  it("ignores malformed replies and replies with a mismatched id or document token", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    const request = currentRequest(webview);

    webview.send({ ...request, error: null, status: status() });
    webview.send({
      version: 1,
      requestId: "30000000000040008000000000000003",
      documentToken: tokenA,
      error: null,
      status: status(),
    });
    webview.send({
      version: 1,
      requestId: request.requestId,
      documentToken: tokenB,
      error: null,
      status: status(),
    });
    webview.send({
      version: 1,
      requestId: request.requestId,
      documentToken: tokenA,
      error: null,
      status: status({ remainingSeconds: 30 * 24 * 60 * 60 + 1 }),
    });
    webview.send({
      version: 1,
      requestId: request.requestId,
      documentToken: tokenA,
      error: null,
      status: { ...status(), extra: "x".repeat(9_000) },
    });
    expect(client.getSnapshot().status).toBeNull();
    expect(webview.posted).toHaveLength(1);

    vi.advanceTimersByTime(4_000);
    expect(client.getSnapshot()).toMatchObject({ phase: "unavailable", error: "RequestTimeout", status: null });
    client.stop();
  });

  it("keeps the last valid status and marks it stale after a request timeout", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    const firstRequest = currentRequest(webview);
    reply(webview, firstRequest);
    const before = client.getSnapshot();

    vi.advanceTimersByTime(2_000);
    const pollRequest = currentRequest(webview);
    vi.advanceTimersByTime(4_000);

    expect(client.getSnapshot()).toMatchObject({
      phase: "stale",
      status: before.status,
      updatedAt: before.updatedAt,
      error: "RequestTimeout",
    });
    expect(currentRequest(webview)).toBe(pollRequest);
    client.stop();
  });

  it("reports a missing WebView bridge and a missing initialization message", () => {
    vi.useFakeTimers();
    const missing = createNativeStatusClient(null);
    missing.start();
    expect(missing.getSnapshot()).toMatchObject({ phase: "unavailable", error: "WebViewBridgeUnavailable" });
    missing.stop();

    const webview = new FakeWebView();
    const delayed = createNativeStatusClient(webview);
    delayed.start();
    vi.advanceTimersByTime(4_000);
    expect(delayed.getSnapshot()).toMatchObject({ phase: "unavailable", error: "BridgeInitializationTimeout" });
    delayed.stop();
  });

  it("discards an old document request and refreshes with the new token", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    const firstRequest = currentRequest(webview);
    reply(webview, firstRequest);

    vi.advanceTimersByTime(2_000);
    const oldDocumentRequest = currentRequest(webview);
    initialize(webview, tokenB);
    vi.advanceTimersByTime(1_000);
    void client.refresh();
    const newDocumentRequest = currentRequest(webview);
    expect(newDocumentRequest.documentToken).toBe(tokenB);

    reply(webview, oldDocumentRequest, { status: status({ remainingSeconds: 1 }) });
    expect(client.getSnapshot().status?.remainingSeconds).toBe(120);
    reply(webview, newDocumentRequest, { status: status({ remainingSeconds: 90 }) });
    expect(client.getSnapshot()).toMatchObject({ phase: "ready", status: { remainingSeconds: 90 } });
    client.stop();
  });

  it("maps native authorization outcomes without inventing an idle status", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    reply(webview, currentRequest(webview), { error: "Unauthorized" });
    expect(client.getSnapshot()).toMatchObject({ phase: "unauthorized", status: null, error: "Unauthorized" });

    vi.advanceTimersByTime(2_000);
    reply(webview, currentRequest(webview), { error: "AuthorizationUnavailable" });
    expect(client.getSnapshot()).toMatchObject({ phase: "unavailable", status: null, error: "AuthorizationUnavailable" });
    client.stop();
  });

  it("validates inactive status as an explicit native response", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    client.start();
    initialize(webview);
    reply(webview, currentRequest(webview), {
      status: status({
        sessionState: "Inactive",
        remainingSeconds: 0,
        sessionId: null,
        profileId: null,
        startUtc: null,
        endUtc: null,
        strictMode: false,
        enforcementConnected: false,
        mode: "SimpleTimer",
      }),
    });
    expect(client.getSnapshot()).toMatchObject({ phase: "ready", status: { sessionState: "Inactive" } });
    client.stop();
  });

  it("stops listeners and polling, and can be started again", () => {
    vi.useFakeTimers();
    const webview = new FakeWebView();
    const client = createNativeStatusClient(webview);
    const notify = vi.fn();
    const unsubscribe = client.subscribe(notify);
    client.start();
    initialize(webview);
    reply(webview, currentRequest(webview));
    const postedCount = webview.posted.length;

    client.stop();
    expect(client.getSnapshot()).toMatchObject({ phase: "stale", error: "ClientStopped" });
    const updatesAtStop = notify.mock.calls.length;
    vi.advanceTimersByTime(10_000);
    expect(webview.posted).toHaveLength(postedCount);
    expect(notify).toHaveBeenCalledTimes(updatesAtStop);

    client.start();
    initialize(webview, tokenB);
    reply(webview, currentRequest(webview));
    expect(client.getSnapshot()).toMatchObject({ phase: "ready", error: null });
    unsubscribe();
    client.stop();
  });
});
