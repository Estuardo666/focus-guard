"use strict";

(() => {
  const state = document.getElementById("state");
  const webview = window.chrome && window.chrome.webview;
  if (!webview) return;

  let documentToken = null;
  let bridgeUnavailableLabel = "";
  webview.addEventListener("message", (event) => {
    const message = event.data;
    if (!message || typeof message !== "object") return;

    if (message.type === "host.initialize" && message.version === 1) {
      documentToken = message.documentToken;
      bridgeUnavailableLabel = message.bridgeUnavailableLabel;
      document.getElementById("title").textContent = message.title;
      document.getElementById("description").textContent = message.description;
      document.title = message.title;
      state.textContent = message.readyLabel;
      webview.postMessage({
        version: 1,
        requestId: crypto.randomUUID().replaceAll("-", ""),
        documentToken,
        method: "status.get",
        params: {}
      });
      return;
    }

    if (message.version !== 1 || message.documentToken !== documentToken) return;
    if (message.error === "AuthorizationUnavailable") {
      state.textContent = bridgeUnavailableLabel;
    } else if (message.error) {
      state.textContent = message.error;
    } else if (message.status && typeof message.status.sessionState === "string") {
      state.textContent = message.status.sessionState;
    }
  });
})();
