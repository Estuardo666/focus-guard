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
  assert.equal(await verifyPolicy(original), true);

  const stale = structuredClone(original);
  stale.policy.applicationRules[0].value = "mutated.exe";
  assert.equal(await verifyPolicy(stale), false);
});
