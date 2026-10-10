import assert from "node:assert/strict";
import test from "node:test";
import { computePolicyHash, verifyPolicy } from "../src/policy-integrity.js";

function delivery() {
  return {
    schemaVersion: 1,
    policyId: "11111111-1111-1111-1111-111111111111",
    policyHash: "",
    policy: {
      sessionId: "22222222-2222-2222-2222-222222222222",
      profileId: { value: "33333333-3333-3333-3333-333333333333" },
      startUtc: "2030-01-01T12:00:00+00:00",
      endUtc: "2030-01-01T13:00:00+00:00",
      isStrict: false,
      enforcementEnabled: true,
      applicationRules: [{
        id: "44444444-4444-4444-4444-444444444444",
        action: "Block",
        matchKind: "FileName",
        value: "chat.exe"
      }],
      websiteRules: []
    }
  };
}

test("acknowledges only the exact immutable policy hash", async () => {
  const original = delivery();
  original.policyHash = await computePolicyHash(original);
  assert.equal(
    original.policyHash,
    "6070D18C319F2FB9CDB748F90FD3E98716D8815DBF716EE6C46DE32E4BE126B2"
  );
  assert.equal(await verifyPolicy(original), true);

  const stale = structuredClone(original);
  stale.policy.applicationRules[0].value = "mutated.exe";
  assert.equal(await verifyPolicy(stale), false);
});

test("cycle-count policy hash matches the service integrity contract", async () => {
  const cycleDelivery = {
    schemaVersion: 1,
    policyId: "aaaaaaaa-1111-1111-1111-111111111111",
    policyHash: "",
    policy: {
      sessionId: "bbbbbbbb-2222-2222-2222-222222222222",
      profileId: { value: "cccccccc-3333-3333-3333-333333333333" },
      startUtc: "2030-01-01T12:00:00+00:00",
      endUtc: "2030-01-01T12:25:00+00:00",
      isStrict: false,
      enforcementEnabled: true,
      applicationRules: [],
      websiteRules: [{
        id: "dddddddd-4444-4444-4444-444444444444",
        action: "Block",
        pattern: "example.com"
      }],
      cyclePlan: {
        configuration: {
          focusDuration: "00:25:00",
          shortBreakDuration: "00:05:00",
          longBreakDuration: "00:15:00",
          cyclesBeforeLongBreak: 4,
          terminationMode: "CycleCount",
          cycleCount: 1,
          totalDuration: null
        },
        phases: [{
          index: 0,
          kind: "Focus",
          startUtc: "2030-01-01T12:00:00+00:00",
          endUtc: "2030-01-01T12:25:00+00:00",
          focusCycleNumber: 1
        }],
        endUtc: "2030-01-01T12:25:00+00:00"
      },
      breakApplicationRules: [],
      breakWebsiteRules: [],
      authorizedPhaseIndex: -1,
      authorizedPhaseEndUtc: null
    }
  };

  cycleDelivery.policyHash = await computePolicyHash(cycleDelivery);

  assert.equal(
    cycleDelivery.policyHash,
    "D7E663D452D0DDC6215D9C963252FC34CB0A8483B673782519ECAB7F8FD6D0D4"
  );
  assert.equal(await verifyPolicy(cycleDelivery), true);
});
