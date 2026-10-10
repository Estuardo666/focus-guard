export async function computePolicyHash(delivery) {
  const policy = delivery.policy;
  const canonical = {
    schemaVersion: delivery.schemaVersion,
    policyId: delivery.policyId,
    sessionId: policy.sessionId,
    profileId: policy.profileId?.value ?? policy.profileId,
    startUtc: policy.startUtc,
    endUtc: policy.endUtc,
    isStrict: policy.isStrict,
    enforcementEnabled: policy.enforcementEnabled,
    applicationRules: policy.applicationRules.map((rule) => ({
      id: rule.id,
      action: rule.action,
      matchKind: rule.matchKind,
      value: rule.value
    })),
    websiteRules: policy.websiteRules.map((rule) => ({
      id: rule.id,
      action: rule.action,
      pattern: rule.pattern
    })),
    cyclePlan: policy.cyclePlan ? {
      configuration: {
        focusDuration: policy.cyclePlan.configuration.focusDuration,
        shortBreakDuration: policy.cyclePlan.configuration.shortBreakDuration,
        longBreakDuration: policy.cyclePlan.configuration.longBreakDuration,
        cyclesBeforeLongBreak: policy.cyclePlan.configuration.cyclesBeforeLongBreak,
        terminationMode: policy.cyclePlan.configuration.terminationMode,
        cycleCount: policy.cyclePlan.configuration.cycleCount ?? null,
        totalDuration: policy.cyclePlan.configuration.totalDuration ?? null
      },
      phases: policy.cyclePlan.phases.map((phase) => ({
        index: phase.index,
        kind: phase.kind,
        startUtc: phase.startUtc,
        endUtc: phase.endUtc,
        focusCycleNumber: phase.focusCycleNumber
      })),
      endUtc: policy.cyclePlan.endUtc
    } : null,
    breakApplicationRules: (policy.breakApplicationRules ?? []).map((rule) => ({
      id: rule.id,
      action: rule.action,
      matchKind: rule.matchKind,
      value: rule.value
    })),
    breakWebsiteRules: (policy.breakWebsiteRules ?? []).map((rule) => ({
      id: rule.id,
      action: rule.action,
      pattern: rule.pattern
    })),
    authorizedPhaseIndex: policy.authorizedPhaseIndex ?? -1,
    authorizedPhaseEndUtc: policy.authorizedPhaseEndUtc ?? null
  };
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(JSON.stringify(canonical))
  );
  return Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, "0")
  ).join("").toUpperCase();
}

export async function verifyPolicy(delivery) {
  if (
    delivery?.schemaVersion !== 1 ||
    typeof delivery?.policyId !== "string" ||
    typeof delivery?.policyHash !== "string" ||
    !delivery?.policy
  ) {
    return false;
  }

  return (await computePolicyHash(delivery)) === delivery.policyHash;
}
