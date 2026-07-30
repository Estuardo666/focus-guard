export type RuleAction = "allow" | "block";

export interface WebsiteRule {
  readonly action: RuleAction;
  readonly pattern: string;
}

export interface CompiledWebsiteRule {
  readonly action: RuleAction;
  readonly pattern: string;
  readonly matcher: RegExp;
}

const BROWSER_INTERNAL_PREFIXES = [
  "about:",
  "chrome:",
  "chrome-extension:",
  "devtools:",
  "edge:",
  "edge-extension:"
] as const;

export function normalizePattern(pattern: string): string {
  if (typeof pattern !== "string" || pattern.trim().length === 0) {
    throw new Error("Website rule pattern is required.");
  }

  const normalized = pattern.trim().toLowerCase();
  if (normalized.includes("?") || normalized.includes("#")) {
    throw new Error("Website rule patterns must exclude query parameters and fragments.");
  }

  const withScheme = normalized.includes("://")
    ? normalized
    : `*://${normalized}`;
  return withScheme.endsWith("/*") || withScheme.endsWith("/")
    ? withScheme.replace(/\/$/, "")
    : `${withScheme}/*`;
}

export function normalizeUrl(url: string): string {
  if (typeof url !== "string" || url.trim().length === 0) {
    throw new Error("URL is required.");
  }

  const trimmed = url.trim();
  const queryOrFragment = trimmed.search(/[?#]/);
  const withoutQuery = queryOrFragment >= 0 ? trimmed.slice(0, queryOrFragment) : trimmed;
  const parsed = new URL(withoutQuery);
  const path = parsed.pathname === "/" ? "/" : parsed.pathname.replace(/\/$/, "");
  return `${parsed.protocol.toLowerCase()}//${parsed.host.toLowerCase()}${path}`;
}

export function isBrowserInternal(url: string): boolean {
  const normalized = url.trim().toLowerCase();
  return BROWSER_INTERNAL_PREFIXES.some((prefix) => normalized.startsWith(prefix));
}

export function compileWebsiteRules(rules: readonly WebsiteRule[]): readonly CompiledWebsiteRule[] {
  return Object.freeze(rules.map((rule) => {
    if (rule.action !== "allow" && rule.action !== "block") {
      throw new Error("Website rule action is invalid.");
    }

    const pattern = normalizePattern(rule.pattern);
    return Object.freeze({
      action: rule.action,
      pattern,
      matcher: compileMatcher(pattern)
    });
  }));
}

function compileMatcher(pattern: string): RegExp {
  const match = /^([^:]+:\/\/)([^/]+)(\/.*)?$/.exec(pattern);
  const host = match?.[2];
  if (match && host && !host.includes("*") && !host.startsWith("www.")) {
    return new RegExp(
      `^${escapeGlob(match[1])}(?:www\\.)?${escapeGlob(host)}${escapeGlob(match[3] ?? "")}$`,
      "i"
    );
  }

  return new RegExp(`^${escapeGlob(pattern)}$`, "i");
}

function escapeGlob(value: string): string {
  return value.replace(/[|\\{}()[\]^$+?.]/g, "\\$&").replace(/\*/g, ".*");
}

export function evaluateWebsiteRules(
  url: string,
  rules: readonly WebsiteRule[] | readonly CompiledWebsiteRule[]
): RuleAction {
  if (isBrowserInternal(url)) {
    return "allow";
  }

  let normalizedUrl: string;
  try {
    normalizedUrl = normalizeUrl(url);
  } catch {
    return "allow";
  }

  const compiled = rules.length > 0 && "matcher" in rules[0]
    ? rules as readonly CompiledWebsiteRule[]
    : compileWebsiteRules(rules as readonly WebsiteRule[]);
  const matching = compiled.filter((rule) => rule.matcher.test(normalizedUrl));
  return matching.some((rule) => rule.action === "allow") ? "allow"
    : matching.some((rule) => rule.action === "block") ? "block"
      : "allow";
}

export function sanitizeUrlForLog(url: string): string | null {
  try {
    return normalizeUrl(url);
  } catch {
    return null;
  }
}
