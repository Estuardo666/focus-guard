import { verifyPolicy } from "./policy-integrity.js";

export const PHASE_BOUNDARY_ALARM = "focusguard-phase-boundary";

const INTERNAL_PREFIXES = [
  "about:", "chrome:", "chrome-extension:", "devtools:", "edge:", "edge-extension:"
];

function normalizedUrl(url) {
  const parsed = new URL(url);
  return `${parsed.protocol.toLowerCase()}//${parsed.host.toLowerCase()}${parsed.pathname}`;
}

function compileMatcher(pattern) {
  const normalizedPattern = pattern.trim().toLowerCase();
  const expanded = normalizedPattern.includes("://")
    ? normalizedPattern
    : `*://${normalizedPattern}${normalizedPattern.includes("/") ? "" : "/*"}`;
  const match = /^([^:]+:\/\/)([^/]+)(\/.*)?$/.exec(expanded);
  const host = match?.[2];
  if (match && host && !host.includes("*") && !host.startsWith("www.")) {
    return new RegExp(
      `^${escapeGlob(match[1])}(?:www\\.)?${escapeGlob(host)}${escapeGlob(match[3] ?? "")}$`,
      "i"
    );
  }
  return new RegExp(`^${escapeGlob(expanded)}$`, "i");
}

function escapeGlob(value) {
  return value.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replaceAll("*", ".*");
}

let cachedPolicyHash = null;
let cachedEffectiveBreakState = null;
let cachedWebsiteRules = Object.freeze([]);
let cachedVerifiedPolicyHash = null;

function currentAuthorizedPhase(policy) {
  const phase = policy.cyclePlan?.phases?.find((item) => item.index === policy.authorizedPhaseIndex);
  if (!phase) return null;
  const phaseEnd = new Date(policy.authorizedPhaseEndUtc ?? phase.endUtc);
  return Number.isFinite(phaseEnd.valueOf()) && phaseEnd.valueOf() === new Date(phase.endUtc).valueOf()
    ? { phase, phaseEnd }
    : null;
}

function isAuthorizedBreak(policy, now) {
  const current = currentAuthorizedPhase(policy);
  return current !== null && ["ShortBreak", "LongBreak"].includes(current.phase.kind) &&
    current.phaseEnd > now && new Date(current.phase.startUtc) <= now;
}

export function schedulePhaseBoundaryAlarm(browserApi, policy, now = new Date()) {
  const current = currentAuthorizedPhase(policy);
  if (current === null || current.phaseEnd <= now) {
    void browserApi.alarms.clear(PHASE_BOUNDARY_ALARM);
    return null;
  }

  browserApi.alarms.create(PHASE_BOUNDARY_ALARM, { when: current.phaseEnd.valueOf() });
  return current.phaseEnd;
}

function compiledRules(delivery, now) {
  const breakState = isAuthorizedBreak(delivery.policy, now);
  if (delivery.policyHash !== cachedPolicyHash || breakState !== cachedEffectiveBreakState) {
    cachedPolicyHash = delivery.policyHash;
    cachedEffectiveBreakState = breakState;
    const rules = breakState
      ? (delivery.policy.breakWebsiteRules ?? [])
      : delivery.policy.websiteRules;
    cachedWebsiteRules = Object.freeze(rules.map((rule) => Object.freeze({
      action: rule.action.toLowerCase(),
      matcher: compileMatcher(rule.pattern)
    })));
  }
  return cachedWebsiteRules;
}

export async function evaluateConfirmedNavigation(url, stored, now = new Date()) {
  if (typeof url !== "string" || INTERNAL_PREFIXES.some((prefix) => url.toLowerCase().startsWith(prefix))) {
    return "allow";
  }
  const delivery = stored?.focusGuardPolicy;
  const integrityValid = delivery?.policyHash === cachedVerifiedPolicyHash ||
    (delivery && await verifyPolicy(delivery));
  if (integrityValid && delivery.policyHash !== cachedVerifiedPolicyHash) {
    cachedVerifiedPolicyHash = delivery.policyHash;
  }
  if (stored?.focusGuardEnforcementConfirmed !== true ||
      !delivery || !integrityValid ||
      delivery.policy.enforcementEnabled !== true ||
      new Date(delivery.policy.startUtc) > now || new Date(delivery.policy.endUtc) <= now) {
    return "allow";
  }
  let candidate;
  try { candidate = normalizedUrl(url); } catch { return "allow"; }
  const matched = compiledRules(delivery, now).filter((rule) => rule.matcher.test(candidate));
  if (matched.some((rule) => rule.action === "allow")) return "allow";
  return matched.some((rule) => rule.action === "block") ? "block" : "allow";
}

export async function releaseBlockedNavigationIfAllowed(
  url,
  stored,
  navigate,
  now = new Date()
) {
  if (await evaluateConfirmedNavigation(url, stored, now) !== "allow") {
    return false;
  }

  navigate(url);
  return true;
}

/** @param {any} browserApi @param {(sanitizedUrl: string) => void} [onBlocked] */
export function installNavigationEnforcement(browserApi, onBlocked = () => {}) {
  browserApi.webNavigation.onBeforeNavigate.addListener((details) => {
    if (details.frameId !== 0) return;
    const storedKeys = [
      "focusGuardPolicy", "focusGuardEnforcementConfirmed"
    ];
    void browserApi.storage.local.get(storedKeys).then(async (stored) => {
      if (await evaluateConfirmedNavigation(details.url, stored) === "block") {
        // Integrity verification and rule matching are asynchronous. A cycle can enter
        // its break after this navigation reads the focus policy but before the redirect
        // runs. Revalidate only the blocking path so an obsolete focus decision cannot
        // fight the block page's break release and create a redirect loop.
        const latest = await browserApi.storage.local.get(storedKeys);
        if (await evaluateConfirmedNavigation(details.url, latest) !== "block") {
          return;
        }
        const sanitized = normalizedUrl(details.url);
        onBlocked(sanitized);
        // The block page names the domain and pre-fills the capture field with it, so it
        // is handed the address minus query and fragment — never the raw URL.
        const blocked = `${browserApi.runtime.getURL("status.html")}?u=${encodeURIComponent(sanitized)}`;
        await browserApi.tabs.update(details.tabId, { url: blocked });
      }
    });
  });
}
