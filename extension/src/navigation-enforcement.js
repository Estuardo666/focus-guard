import { verifyPolicy } from "./policy-integrity.js";

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
let cachedWebsiteRules = Object.freeze([]);
let cachedVerifiedPolicyHash = null;

function compiledRules(delivery) {
  if (delivery.policyHash !== cachedPolicyHash) {
    cachedPolicyHash = delivery.policyHash;
    cachedWebsiteRules = Object.freeze(delivery.policy.websiteRules.map((rule) => Object.freeze({
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
  const matched = compiledRules(delivery).filter((rule) => rule.matcher.test(candidate));
  if (matched.some((rule) => rule.action === "allow")) return "allow";
  return matched.some((rule) => rule.action === "block") ? "block" : "allow";
}

/** @param {any} browserApi @param {(sanitizedUrl: string) => void} [onBlocked] */
export function installNavigationEnforcement(browserApi, onBlocked = () => {}) {
  browserApi.webNavigation.onBeforeNavigate.addListener((details) => {
    if (details.frameId !== 0) return;
    void browserApi.storage.local.get([
      "focusGuardPolicy", "focusGuardEnforcementConfirmed"
    ]).then(async (stored) => {
      if (await evaluateConfirmedNavigation(details.url, stored) === "block") {
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
