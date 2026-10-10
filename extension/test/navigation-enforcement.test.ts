import assert from "node:assert/strict";
import test from "node:test";
import { performance } from "node:perf_hooks";
import { computePolicyHash } from "../src/policy-integrity.js";
import {
  evaluateConfirmedNavigation,
  installNavigationEnforcement,
  PHASE_BOUNDARY_ALARM,
  releaseBlockedNavigationIfAllowed,
  schedulePhaseBoundaryAlarm
} from "../src/navigation-enforcement.js";

async function stored() {
  const policy = {
    schemaVersion: 1, policyId: "11111111-1111-1111-1111-111111111111", policyHash: "",
    policy: {
      sessionId: "22222222-2222-2222-2222-222222222222",
      profileId: { value: "33333333-3333-3333-3333-333333333333" },
      startUtc: "2020-01-01T12:00:00+00:00", endUtc: "2099-01-01T13:00:00+00:00",
      isStrict: false, enforcementEnabled: true, applicationRules: [], websiteRules: [
        { id: "44444444-4444-4444-4444-444444444444", action: "Block", pattern: "example.com" },
        { id: "55555555-5555-5555-5555-555555555555", action: "Allow", pattern: "example.com/work/*" },
        { id: "66666666-6666-6666-6666-666666666666", action: "Block", pattern: "facebook.com" },
        { id: "77777777-7777-7777-7777-777777777777", action: "Block", pattern: "instagram.com" }
      ]
    }
  };
  policy.policyHash = await computePolicyHash(policy);
  return { focusGuardPolicy: policy, focusGuardEnforcementConfirmed: true };
}

const now = new Date("2030-01-01T12:30:00Z");
test("blocks confirmed matches and gives allow precedence", async () => {
  const state = await stored();
  assert.equal(await evaluateConfirmedNavigation("https://example.com/private?q=secret", state, now), "block");
  assert.equal(await evaluateConfirmedNavigation("https://example.com/work/today?q=secret", state, now), "allow");
  assert.equal(await evaluateConfirmedNavigation("https://www.facebook.com/", state, now), "block");
  assert.equal(await evaluateConfirmedNavigation("https://www.instagram.com/direct/", state, now), "block");
  assert.equal(await evaluateConfirmedNavigation("https://business.facebook.com/", state, now), "allow");
});
test("authorized break replaces focus rules until its exact end", async () => {
  const state = await stored();
  const policy: any = state.focusGuardPolicy.policy;
  policy.websiteRules = [{ id: crypto.randomUUID(), action: "Block", pattern: "music.example" }];
  policy.breakWebsiteRules = [{ id: crypto.randomUUID(), action: "Block", pattern: "music.example" }];
  policy.cyclePlan = {
    configuration: {
      focusDuration: "00:30:00",
      shortBreakDuration: "00:10:00",
      longBreakDuration: "00:20:00",
      cyclesBeforeLongBreak: 4,
      terminationMode: "CycleCount",
      cycleCount: 2,
      totalDuration: null
    },
    phases: [
      { index: 0, kind: "Focus", startUtc: "2030-01-01T12:00:00+00:00", endUtc: "2030-01-01T12:30:00+00:00", focusCycleNumber: 1 },
      { index: 1, kind: "ShortBreak", startUtc: "2030-01-01T12:30:00+00:00", endUtc: "2030-01-01T12:40:00+00:00", focusCycleNumber: 1 },
      { index: 2, kind: "Focus", startUtc: "2030-01-01T12:40:00+00:00", endUtc: "2030-01-01T13:10:00+00:00", focusCycleNumber: 2 }
    ],
    endUtc: "2030-01-01T13:10:00+00:00"
  };
  policy.authorizedPhaseIndex = 1;
  policy.authorizedPhaseEndUtc = "2030-01-01T12:40:00+00:00";
  state.focusGuardPolicy.policyHash = await computePolicyHash(state.focusGuardPolicy);

  // The break list is the exception list: what it names stays blocked through the
  // pause, and everything else in the focus list is released for its duration.
  assert.equal(await evaluateConfirmedNavigation("https://music.example", state, new Date("2030-01-01T12:35:00Z")), "block");
  policy.websiteRules.push({ id: crypto.randomUUID(), action: "Block", pattern: "social.example" });
  state.focusGuardPolicy.policyHash = await computePolicyHash(state.focusGuardPolicy);
  assert.equal(await evaluateConfirmedNavigation("https://social.example", state, new Date("2030-01-01T12:35:00Z")), "allow");
  assert.equal(await evaluateConfirmedNavigation("https://music.example", state, new Date("2030-01-01T12:45:00Z")), "block");
  assert.equal(await evaluateConfirmedNavigation("https://social.example", state, new Date("2030-01-01T12:45:00Z")), "block");
});
test("phase policy schedules an exact MV3 wake-up and clears stale boundaries", async () => {
  const state = await stored();
  const policy: any = state.focusGuardPolicy.policy;
  policy.cyclePlan = {
    phases: [
      { index: 0, kind: "Focus", startUtc: "2030-01-01T12:00:00Z", endUtc: "2030-01-01T12:30:00Z" }
    ]
  };
  policy.authorizedPhaseIndex = 0;
  policy.authorizedPhaseEndUtc = "2030-01-01T12:30:00Z";
  const created: any[] = [];
  const cleared: string[] = [];
  const browserApi: any = {
    alarms: {
      create(name: string, options: any) { created.push({ name, options }); },
      clear(name: string) { cleared.push(name); return Promise.resolve(true); }
    }
  };

  assert.equal(
    schedulePhaseBoundaryAlarm(browserApi, policy, new Date("2030-01-01T12:29:00Z"))?.toISOString(),
    "2030-01-01T12:30:00.000Z");
  assert.deepEqual(created, [{
    name: PHASE_BOUNDARY_ALARM,
    options: { when: Date.parse("2030-01-01T12:30:00Z") }
  }]);

  assert.equal(schedulePhaseBoundaryAlarm(
    browserApi,
    policy,
    new Date("2030-01-01T12:30:00Z")), null);
  assert.deepEqual(cleared, [PHASE_BOUNDARY_ALARM]);
});
test("an already-blocked tab returns to its URL when the break policy allows it", async () => {
  const state = await stored();
  const policy: any = state.focusGuardPolicy.policy;
  policy.websiteRules = [{ id: crypto.randomUUID(), action: "Block", pattern: "social.example" }];
  policy.breakWebsiteRules = [];
  policy.cyclePlan = {
    configuration: {
      focusDuration: "00:30:00",
      shortBreakDuration: "00:10:00",
      longBreakDuration: "00:20:00",
      cyclesBeforeLongBreak: 4,
      terminationMode: "CycleCount",
      cycleCount: 2,
      totalDuration: null
    },
    phases: [
      { index: 1, kind: "ShortBreak", startUtc: "2030-01-01T12:30:00Z", endUtc: "2030-01-01T12:40:00Z", focusCycleNumber: 1 }
    ],
    endUtc: "2030-01-01T12:40:00Z"
  };
  policy.authorizedPhaseIndex = 1;
  policy.authorizedPhaseEndUtc = "2030-01-01T12:40:00Z";
  state.focusGuardPolicy.policyHash = await computePolicyHash(state.focusGuardPolicy);
  const navigated: string[] = [];

  assert.equal(await releaseBlockedNavigationIfAllowed(
    "https://social.example/",
    state,
    (url: string) => navigated.push(url),
    new Date("2030-01-01T12:35:00Z")), true);
  assert.deepEqual(navigated, ["https://social.example/"]);
});
test("fails open for internal, degraded, invalid, and expired policy", async () => {
  const state = await stored();
  assert.equal(await evaluateConfirmedNavigation("chrome://settings", state, now), "allow");
  assert.equal(await evaluateConfirmedNavigation("https://example.com", { ...state, focusGuardEnforcementConfirmed: false }, now), "allow");
  const invalid = structuredClone(state); invalid.focusGuardPolicy.policyHash = "0".repeat(64);
  assert.equal(await evaluateConfirmedNavigation("https://example.com", invalid, now), "allow");
  assert.equal(await evaluateConfirmedNavigation("https://example.com", state, new Date("2099-01-01T13:00:00Z")), "allow");
});

test("blocked navigation reports only normalized URL before redirect", async () => {
  const state = await stored();
  let listener: ((details: { frameId: number; tabId: number; url: string }) => void) | undefined;
  let audited: string | undefined;
  let redirected: unknown;
  const browserApi: any = {
    webNavigation: { onBeforeNavigate: { addListener(value: typeof listener) { listener = value; } } },
    storage: { local: { async get() { return state; } } },
    runtime: { getURL() { return "chrome-extension://focusguard/status.html"; } },
    tabs: { async update(tabId: number, value: { url: string }) { redirected = { tabId, value }; } }
  };
  installNavigationEnforcement(browserApi, (url: string) => { audited = url; });

  listener!({ frameId: 0, tabId: 7, url: "https://user:password@EXAMPLE.com/private?q=secret#fragment" });
  await new Promise((resolve) => setTimeout(resolve, 20));

  assert.equal(audited, "https://example.com/private");
  // The block page needs the domain to name it and to pre-fill the capture field, so the
  // normalized URL rides along -- credentials, query and fragment must not.
  assert.deepEqual(redirected, {
    tabId: 7,
    value: {
      url: "chrome-extension://focusguard/status.html?u=https%3A%2F%2Fexample.com%2Fprivate"
    }
  });
  const forwarded = (redirected as { value: { url: string } }).value.url;
  assert.ok(!forwarded.includes("password"));
  assert.ok(!forwarded.includes("secret"));
  assert.ok(!forwarded.includes("fragment"));
});

test("stale focus evaluation cannot redirect after break policy is stored", async () => {
  const focusState = await stored();
  const breakState = structuredClone(focusState);
  const policy: any = breakState.focusGuardPolicy.policy;
  const current = Date.now();
  const breakStart = new Date(current - 60_000).toISOString();
  const breakEnd = new Date(current + 60_000).toISOString();
  policy.breakWebsiteRules = [];
  policy.cyclePlan = {
    configuration: {
      focusDuration: "00:30:00",
      shortBreakDuration: "00:10:00",
      longBreakDuration: "00:20:00",
      cyclesBeforeLongBreak: 4,
      terminationMode: "CycleCount",
      cycleCount: 2,
      totalDuration: null
    },
    phases: [{
      index: 1,
      kind: "ShortBreak",
      startUtc: breakStart,
      endUtc: breakEnd,
      focusCycleNumber: 1
    }],
    endUtc: breakEnd
  };
  policy.authorizedPhaseIndex = 1;
  policy.authorizedPhaseEndUtc = breakEnd;
  breakState.focusGuardPolicy.policyHash = await computePolicyHash(breakState.focusGuardPolicy);

  let listener: ((details: { frameId: number; tabId: number; url: string }) => void) | undefined;
  let reads = 0;
  const redirects: unknown[] = [];
  const browserApi: any = {
    webNavigation: { onBeforeNavigate: { addListener(value: typeof listener) { listener = value; } } },
    storage: {
      local: {
        async get() {
          reads++;
          return reads === 1 ? focusState : breakState;
        }
      }
    },
    runtime: { getURL() { return "chrome-extension://focusguard/status.html"; } },
    tabs: { async update(tabId: number, value: { url: string }) { redirects.push({ tabId, value }); } }
  };
  installNavigationEnforcement(browserApi);

  listener!({ frameId: 0, tabId: 9, url: "https://example.com/private" });
  await new Promise((resolve) => setTimeout(resolve, 20));

  assert.equal(reads, 2);
  assert.deepEqual(redirects, []);
});

test("real navigation path evaluates 10,000 cached rules below the 10 ms median budget", async () => {
  const state = await stored();
  state.focusGuardPolicy.policy.websiteRules = Array.from({ length: 10_000 }, (_, index) => ({
    id: crypto.randomUUID(),
    action: "Block",
    pattern: `site${index}.example`
  }));
  state.focusGuardPolicy.policyHash = await computePolicyHash(state.focusGuardPolicy);
  const target = "https://site9999.example/private?secret=excluded";
  await evaluateConfirmedNavigation(target, state, now);
  const samples: number[] = [];
  for (let index = 0; index < 31; index++) {
    const started = performance.now();
    assert.equal(await evaluateConfirmedNavigation(target, state, now), "block");
    samples.push(performance.now() - started);
  }
  samples.sort((left, right) => left - right);
  const median = samples[Math.floor(samples.length / 2)];
  assert.ok(median < 10, `real navigation median was ${median.toFixed(3)} ms`);
});
