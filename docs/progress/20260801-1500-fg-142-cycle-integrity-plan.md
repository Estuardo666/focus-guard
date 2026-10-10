# FG-142 plan — strict cycle persistence and enforcement

## Scope

- Bind the complete cycle plan and break exceptions to policy integrity.
- Apply break exceptions only during a service-authorized break phase.
- Recover strict cycle state without allowing a disconnected consumer to
  prolong relaxed enforcement.
- Cover snapshot round-trip, effective-rule transitions, and tamper detection.

## Implications

- Security: focus rules remain the safe default; invalid snapshots fail open
  through the existing recovery path and are quarantined.
- UX: consumers can expose a degraded state when break authorization is not
  available without changing the authoritative session clock.
- Recovery: restart reconciliation restores the plan and reauthorizes only the
  phase valid at the current UTC boundary.

## Acceptance

The full plan, both rule collections, and phase authorization are integrity
protected; authorized Allow exceptions are active only during the matching
break; strict snapshots round-trip these values; and existing extension,
monitoring, service, and full-suite tests remain green.
