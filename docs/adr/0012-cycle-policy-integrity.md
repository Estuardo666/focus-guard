# ADR 0012: Integrity-bound cycle policies and asymmetric break authorization

## Status

Accepted for FG-142. The break-rule action semantics in this historical decision
were corrected by [ADR 0014](0014-block-only-break-exceptions.md); the phase
authorization and integrity requirements below remain in force.

## Decision

The enforcement policy integrity value covers the complete cycle plan, normal
rules, break exception rules, authorized phase index, and authorized phase
end. When the service-authorized phase is a current short or long break,
consumers select only the break exception set and therefore suspend ordinary
focus blocking while the configured exceptions remain blocked. At the phase end,
consumers select the normal rules again without
user action.

On restart, a recovered cycle starts with focus rules. The service may
reauthorize the current break after validating the snapshot and current UTC
boundary. A missing or stale relaxation authorization never extends a break;
the consumer returns to focus rules at the persisted phase boundary.

## Consequences

Tampering with a duration, phase, or exception invalidates the policy as a
whole. Application and browser channels can report different transition
states without allowing either channel to extend the session. The MV3
extension schedules a one-shot wake-up at the authorized phase end; the alarm
only requests the next signed policy and grants no relaxation itself. Full
reboot drills remain part of FG-145.
