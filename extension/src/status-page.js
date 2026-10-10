/*
 * Block page behaviour. Two jobs: keep the "how long is left / how far in am I" copy
 * honest second by second, and let one line be written down so the urge can be dropped
 * without ending the session.
 */

import { releaseBlockedNavigationIfAllowed } from "./navigation-enforcement.js";

const CAPTURE_MESSAGE = "focusGuardCapture";
const RING_CIRCUMFERENCE = 2 * Math.PI * 50;

const elements = {
  ringRegion: document.querySelector("#ringRegion"),
  ringProgress: document.querySelector("#ringProgress"),
  ringValue: document.querySelector("#ringValue"),
  ringUnit: document.querySelector("#ringUnit"),
  headline: document.querySelector("#headline"),
  context: document.querySelector("#context"),
  backButton: document.querySelector("#backButton"),
  captureButton: document.querySelector("#captureButton"),
  captureForm: document.querySelector("#captureForm"),
  captureInput: document.querySelector("#captureInput"),
  captureSave: document.querySelector("#captureSave"),
  captureCancel: document.querySelector("#captureCancel"),
  captureFeedback: document.querySelector("#captureFeedback"),
  autoClose: document.querySelector("#autoClose"),
  mode: document.querySelector("#mode")
};

function message(key, substitutions) {
  return chrome.i18n.getMessage(key, substitutions);
}

/** The blocked address, already stripped of query and fragment by the enforcer. */
function blockedUrl() {
  const raw = new URLSearchParams(window.location.search).get("u");
  if (!raw) {
    return null;
  }

  try {
    const parsed = new URL(raw);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed : null;
  } catch {
    return null;
  }
}

function formatClock(date) {
  return date.toLocaleTimeString(chrome.i18n.getUILanguage(), {
    hour: "2-digit",
    minute: "2-digit"
  });
}

function formatMinutes(totalMinutes) {
  if (totalMinutes < 1) {
    return message("blockedLessThanMinute");
  }

  return totalMinutes === 1
    ? message("blockedOneMinute")
    : message("blockedMinutes", [String(totalMinutes)]);
}

const target = blockedUrl();
let session = null;
let captureSent = false;

document.documentElement.lang = chrome.i18n.getUILanguage();
document.title = message("blockedTitle");
elements.headline.textContent = target
  ? message("blockedHeadline", [target.host])
  : message("blockedHeadlineGeneric");
elements.backButton.textContent = message("blockedBack");
elements.captureButton.textContent = message("blockedCapture");
elements.captureInput.placeholder = message("blockedCapturePlaceholder");
elements.captureSave.textContent = message("blockedCaptureSave");
elements.captureCancel.textContent = message("blockedCaptureCancel");
elements.autoClose.textContent = message("blockedAutoClose");
elements.ringUnit.textContent = message("blockedMinutesUnit");
elements.ringProgress.style.strokeDasharray = String(RING_CIRCUMFERENCE);
elements.ringProgress.style.strokeDashoffset = String(RING_CIRCUMFERENCE);

// Pre-filling with the domain means most captures need nothing but Enter.
if (target) {
  elements.captureInput.value = target.host;
}

// Only useful while a session is running; without one there is nothing to defer to.
elements.captureButton.hidden = true;

async function loadSession() {
  const stored = await chrome.storage.local.get("focusGuardPolicy");
  const policy = stored?.focusGuardPolicy?.policy;
  if (!policy) {
    session = null;
    return;
  }

  const startUtc = new Date(policy.startUtc);
  const endUtc = new Date(policy.endUtc);
  if (Number.isNaN(startUtc.valueOf()) || Number.isNaN(endUtc.valueOf()) || endUtc <= startUtc) {
    session = null;
    return;
  }

  session = { startUtc, endUtc, isStrict: policy.isStrict === true };
}

async function releaseIfAllowed() {
  if (!target) {
    return false;
  }

  const stored = await chrome.storage.local.get([
    "focusGuardPolicy",
    "focusGuardEnforcementConfirmed"
  ]);
  return releaseBlockedNavigationIfAllowed(
    target.toString(),
    stored,
    (url) => window.location.replace(url));
}

function renderSessionOver() {
  elements.ringRegion.hidden = true;
  elements.context.textContent = message("blockedSessionOver");
  elements.backButton.textContent = message("blockedContinue");
  elements.captureButton.hidden = true;
  elements.captureForm.hidden = true;
  elements.mode.textContent = "";
}

function render(now = new Date()) {
  if (!session) {
    elements.ringRegion.hidden = true;
    elements.context.textContent = message("blockedMessage");
    elements.mode.textContent = "";
    return;
  }

  const remainingMs = session.endUtc - now;
  if (remainingMs <= 0) {
    renderSessionOver();
    return;
  }

  const totalMs = session.endUtc - session.startUtc;
  const elapsedMs = Math.max(0, now - session.startUtc);
  const remainingMinutes = Math.ceil(remainingMs / 60_000);
  const elapsedMinutes = Math.floor(elapsedMs / 60_000);

  elements.ringRegion.hidden = false;
  elements.ringValue.textContent = String(remainingMinutes);
  elements.ringRegion.setAttribute("aria-label", formatMinutes(remainingMinutes));
  elements.ringProgress.style.strokeDashoffset = String(
    RING_CIRCUMFERENCE * (1 - Math.min(1, elapsedMs / totalMs))
  );

  // Two facts do the persuading: how little is left, and how much would be thrown away.
  // The elapsed count is passed as a bare number so each locale can phrase it without
  // repeating the word "minutes" twice in one sentence.
  elements.context.textContent = message("blockedContext", [
    formatClock(session.endUtc),
    formatMinutes(remainingMinutes),
    String(elapsedMinutes)
  ]);

  // Hiding an exit that exists is what makes people distrust this kind of app.
  elements.mode.textContent = session.isStrict
    ? message("blockedFooterStrict", [formatClock(session.endUtc)])
    : message("blockedFooterNormal");

  elements.captureButton.hidden = captureSent || !target || !elements.captureForm.hidden;
}

function showFeedback(text, state) {
  elements.captureFeedback.textContent = text;
  elements.captureFeedback.dataset.state = state;
  elements.captureFeedback.hidden = false;
}

elements.backButton.addEventListener("click", () => {
  if (window.history.length > 1) {
    window.history.back();
    return;
  }

  window.close();
});

elements.captureButton.addEventListener("click", () => {
  elements.captureForm.hidden = false;
  elements.captureButton.hidden = true;
  elements.captureInput.focus();
  elements.captureInput.select();
});

elements.captureCancel.addEventListener("click", () => {
  elements.captureForm.hidden = true;
  elements.captureButton.hidden = false;
});

elements.captureForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!target || captureSent) {
    return;
  }

  elements.captureSave.disabled = true;
  try {
    const response = await chrome.runtime.sendMessage({
      type: CAPTURE_MESSAGE,
      url: target.toString(),
      text: elements.captureInput.value
    });
    if (response?.accepted === true) {
      captureSent = true;
      elements.captureForm.hidden = true;
      showFeedback(message("blockedCaptureSaved"), "ok");
      return;
    }

    showFeedback(message("blockedCaptureFailed"), "error");
  } catch {
    showFeedback(message("blockedCaptureFailed"), "error");
  } finally {
    elements.captureSave.disabled = false;
  }
});

await loadSession();
if (!await releaseIfAllowed()) {
  render();
}
setInterval(() => render(), 1_000);

// The policy is refreshed by the service worker heartbeat, so following it lets the page
// notice the session ending without polling the service itself.
chrome.storage.onChanged.addListener((changes, areaName) => {
  if (areaName === "local" &&
      (changes.focusGuardPolicy || changes.focusGuardEnforcementConfirmed)) {
    void loadSession().then(async () => {
      if (!await releaseIfAllowed()) {
        render();
      }
    });
  }
});
