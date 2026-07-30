export function createNativePortClient({
  browserApi,
  hostName,
  markUnavailable,
  onReconnect,
  responseTimeoutMilliseconds = 3_000,
  reconnectDelayMilliseconds = 1_000
}) {
  let nativePort = null;
  let reconnectTimer = null;
  const pendingResponses = new Map();

  function connect() {
    if (nativePort) return nativePort;
    try {
      const port = browserApi.runtime.connectNative(hostName);
      nativePort = port;
      port.onMessage.addListener((response) => {
        const pending = pendingResponses.get(response?.correlationId);
        if (!pending) return;
        pendingResponses.delete(response.correlationId);
        clearTimeout(pending.timeoutId);
        pending.resolve(response);
      });
      port.onDisconnect.addListener(() => {
        // Reading lastError acknowledges Chromium's disconnect report and keeps
        // expected native-host restarts out of the extension error console.
        void browserApi.runtime.lastError;
        if (nativePort !== port) return;
        nativePort = null;
        for (const pending of pendingResponses.values()) {
          clearTimeout(pending.timeoutId);
          pending.reject(new Error("nativeHostDisconnected"));
        }
        pendingResponses.clear();
        markUnavailable("nativeHostDisconnected");
        clearTimeout(reconnectTimer);
        reconnectTimer = setTimeout(onReconnect, reconnectDelayMilliseconds);
      });
      return port;
    } catch {
      markUnavailable("nativeHostUnavailable");
      return null;
    }
  }

  function query(request) {
    return new Promise((resolve, reject) => {
      const port = connect();
      if (!port) {
        reject(new Error("nativeHostUnavailable"));
        return;
      }
      const timeoutId = setTimeout(() => {
        pendingResponses.delete(request.correlationId);
        reject(new Error("nativeHostTimeout"));
      }, responseTimeoutMilliseconds);
      pendingResponses.set(request.correlationId, { resolve, reject, timeoutId });
      try {
        port.postMessage(request);
      } catch (error) {
        clearTimeout(timeoutId);
        pendingResponses.delete(request.correlationId);
        reject(error);
      }
    });
  }

  return Object.freeze({ query });
}
