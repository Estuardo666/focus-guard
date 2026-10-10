import { verifyPolicy } from "./policy-integrity.js";
import {
  installNavigationEnforcement,
  PHASE_BOUNDARY_ALARM,
  schedulePhaseBoundaryAlarm
} from "./navigation-enforcement.js";
import { createNativePortClient } from "./native-port-client.js";

const HOST_NAME = "com.focusguard.native";
const HEARTBEAT_ALARM = "focusguard-health";
const HEARTBEAT_PERIOD_MINUTES = 1;
const HEARTBEAT_INTERVAL_MILLISECONDS = 2_000;

function markNativeUnavailable(errorCode = "nativeHostUnavailable") {
  void chrome.storage.local.set({ focusGuardEnforcementConfirmed: false });
  void chrome.storage.local.set({
    focusGuardHealth: { receivedAtUtc: new Date().toISOString(), errorCode }
  });
}

const nativeClient = createNativePortClient({
  browserApi: chrome,
  hostName: HOST_NAME,
  markUnavailable: markNativeUnavailable,
  onReconnect: () => { void sendHealth("heartbeat"); }
});

// A policy is only enforced once the service has seen the extension acknowledge that
// exact delivery, which costs one heartbeat. Cycle phase boundaries issue a fresh
// delivery, so waiting for the next scheduled beat would leave every break and every
// return to focus unenforced for the whole interval. Acknowledging immediately closes
// the handshake in a round trip instead.
let acknowledgedPolicyId = null;
let acknowledging = false;

async function acknowledgeNewPolicyOnce(policyId) {
  if (policyId === acknowledgedPolicyId || acknowledging) return;
  acknowledgedPolicyId = policyId;
  acknowledging = true;
  try {
    await sendHealth("heartbeat");
  } finally {
    acknowledging = false;
  }
}

async function sendHealth(type, blockedUrl = null, captureText = null) {
  const stored = await chrome.storage.local.get("focusGuardPolicyAck");
  const acknowledgement = stored.focusGuardPolicyAck;
  const request = {
    protocolVersion: 1,
    type,
    correlationId: crypto.randomUUID(),
    sentAtUtc: new Date().toISOString(),
    acknowledgedPolicyId: acknowledgement?.policyId ?? null,
    acknowledgedPolicyHash: acknowledgement?.policyHash ?? null,
    blockedUrl,
    captureText
  };

  try {
    const response = await nativeClient.query(request);
    const valid =
      response?.protocolVersion === 1 &&
      response?.type === "healthStatus" &&
      response?.correlationId === request.correlationId &&
      typeof response?.serviceReady === "boolean" &&
      typeof response?.extensionReady === "boolean";
    let storedPolicyId = null;
    if (valid) {
      if (response.policy) {
        const integrityValid = await verifyPolicy(response.policy);
        if (integrityValid && new Date(response.policy.policy.endUtc) > new Date()) {
          storedPolicyId = response.policy.policyId;
          await chrome.storage.local.set({
            focusGuardPolicy: response.policy,
            focusGuardPolicyAck: {
              policyId: response.policy.policyId,
              policyHash: response.policy.policyHash
            }
          });
          schedulePhaseBoundaryAlarm(chrome, response.policy.policy);
        } else {
          await chrome.storage.local.remove(["focusGuardPolicy", "focusGuardPolicyAck"]);
          void chrome.alarms.clear(PHASE_BOUNDARY_ALARM);
        }
      } else {
        await chrome.storage.local.remove(["focusGuardPolicy", "focusGuardPolicyAck"]);
        void chrome.alarms.clear(PHASE_BOUNDARY_ALARM);
      }
    }
    const confirmed = valid && response.serviceReady === true &&
      response.extensionReady === true && response.sessionState === "Active" &&
      response.enforcementConfirmed === true &&
      await verifyPolicy(response.policy) &&
      new Date(response.policy.policy.endUtc) > new Date();
    await chrome.storage.local.set({ focusGuardEnforcementConfirmed: confirmed });
    await chrome.storage.local.set({
      focusGuardHealth: valid
        ? { receivedAtUtc: new Date().toISOString(), response }
        : { receivedAtUtc: new Date().toISOString(), errorCode: "invalidResponse" }
    });
    if (confirmed) {
      acknowledgedPolicyId = storedPolicyId;
    } else if (storedPolicyId !== null) {
      void acknowledgeNewPolicyOnce(storedPolicyId);
    }
    // A capture is only actually stored when the service was under this policy, which is
    // the same condition as a confirmed enforcement round trip.
    return confirmed;
  } catch {
    await chrome.storage.local.set({ focusGuardEnforcementConfirmed: false });
    await chrome.storage.local.set({
      focusGuardHealth: {
        receivedAtUtc: new Date().toISOString(),
        errorCode: "nativeHostUnavailable"
      }
    });
    return false;
  }
}

installNavigationEnforcement(chrome, (sanitizedUrl) => sendHealth("audit", sanitizedUrl));

// The block page cannot reach the native host itself, so it hands the capture here.
// Only this extension's own pages can send it: an external site has no runtime access.
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request?.type !== "focusGuardCapture") {
    return false;
  }

  const sameExtension = sender.id === chrome.runtime.id;
  const url = typeof request.url === "string" ? request.url : null;
  if (!sameExtension || url === null) {
    sendResponse({ accepted: false });
    return false;
  }

  const text = typeof request.text === "string" && request.text.trim().length > 0
    ? request.text.slice(0, 240)
    : null;
  sendHealth("capture", url, text)
    .then((accepted) => sendResponse({ accepted }))
    .catch(() => sendResponse({ accepted: false }));
  return true;
});

chrome.runtime.onInstalled.addListener(() => {
  void chrome.alarms.create(HEARTBEAT_ALARM, {
    delayInMinutes: 0.05,
    periodInMinutes: HEARTBEAT_PERIOD_MINUTES
  });
  void sendHealth("heartbeat");
});

chrome.runtime.onStartup.addListener(() => {
  void sendHealth("heartbeat");
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === HEARTBEAT_ALARM || alarm.name === PHASE_BOUNDARY_ALARM) {
    void sendHealth("heartbeat");
  }
});

void sendHealth("heartbeat");
setInterval(() => { void sendHealth("heartbeat"); }, HEARTBEAT_INTERVAL_MILLISECONDS);
