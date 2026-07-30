import assert from "node:assert/strict";
import test from "node:test";
import { performance } from "node:perf_hooks";
import {
  compileWebsiteRules,
  evaluateWebsiteRules,
  isBrowserInternal,
  normalizePattern,
  normalizeUrl,
  sanitizeUrlForLog
} from "../src/rule-engine.ts";

test("normalizes bare domains, subdomains, paths, and scheme wildcards", () => {
  assert.equal(normalizePattern(" example.com "), "*://example.com/*");
  assert.equal(normalizePattern("*.example.com"), "*://*.example.com/*");
  assert.equal(normalizePattern("example.com/path/*"), "*://example.com/path/*");
  assert.equal(normalizePattern("*://*.example.com/*"), "*://*.example.com/*");
});

test("normalizes URL host casing and removes query and fragment", () => {
  assert.equal(normalizeUrl("HTTPS://Example.COM/Work/?tracking=1#top"), "https://example.com/Work");
  assert.equal(sanitizeUrlForLog("https://example.com/?secret=1"), "https://example.com/");
  assert.equal(sanitizeUrlForLog("not a url"), null);
});

test("blocks matching domains and paths while defaulting unrelated URLs to allow", () => {
  const rules = [
    { action: "block" as const, pattern: "example.com" },
    { action: "block" as const, pattern: "*://video.example/path/*" }
  ];

  assert.equal(evaluateWebsiteRules("https://example.com/?query=private", rules), "block");
  assert.equal(evaluateWebsiteRules("http://video.example/path/today", rules), "block");
  assert.equal(evaluateWebsiteRules("https://other.example.net", rules), "allow");
});

test("bare domains match canonical www redirects without matching other subdomains", () => {
  const rules = [
    { action: "block" as const, pattern: "facebook.com" },
    { action: "block" as const, pattern: "instagram.com" }
  ];

  assert.equal(evaluateWebsiteRules("https://www.facebook.com/", rules), "block");
  assert.equal(evaluateWebsiteRules("https://www.instagram.com/accounts/login/", rules), "block");
  assert.equal(evaluateWebsiteRules("https://business.facebook.com/", rules), "allow");
});

test("explicit allow takes precedence over a matching block", () => {
  const compiled = compileWebsiteRules([
    { action: "block", pattern: "example.com" },
    { action: "allow", pattern: "example.com/work/*" }
  ]);

  assert.equal(evaluateWebsiteRules("https://example.com/work/today", compiled), "allow");
  assert.equal(evaluateWebsiteRules("https://example.com/private", compiled), "block");
});

test("browser-internal pages always fail open", () => {
  const rules = [{ action: "block" as const, pattern: "*://*/*" }];

  assert.equal(isBrowserInternal("chrome://settings"), true);
  assert.equal(isBrowserInternal("edge://extensions"), true);
  assert.equal(evaluateWebsiteRules("chrome://settings", rules), "allow");
  assert.equal(evaluateWebsiteRules("edge://extensions", rules), "allow");
});

test("query-bearing rule input is rejected and malformed URLs fail open", () => {
  assert.throws(() => normalizePattern("example.com/?tracking=1"), /exclude query parameters/);
  assert.equal(evaluateWebsiteRules("not a URL", [{ action: "block", pattern: "example.com" }]), "allow");
});

test("10,000 compiled rules evaluate below the 10 ms median budget", () => {
  const compiled = compileWebsiteRules(Array.from({ length: 10_000 }, (_, index) => ({
    action: "block" as const,
    pattern: `site${index}.example`
  })));
  const samples: number[] = [];
  for (let index = 0; index < 5; index++) {
    evaluateWebsiteRules("https://site9999.example/path", compiled);
  }
  for (let index = 0; index < 31; index++) {
    const started = performance.now();
    assert.equal(evaluateWebsiteRules("https://site9999.example/path", compiled), "block");
    samples.push(performance.now() - started);
  }
  samples.sort((left, right) => left - right);
  const median = samples[Math.floor(samples.length / 2)];
  assert.ok(median < 10, `10,000-rule median was ${median.toFixed(3)} ms`);
});
