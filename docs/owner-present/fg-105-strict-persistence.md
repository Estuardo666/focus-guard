# FG-105 owner-present strict-persistence milestone

## Authorization and scope

The owner explicitly authorized FG-105 on 2026-07-28 and reported that Codex is
running as administrator. This milestone may implement and verify durable strict
policy state under `%ProgramData%\FocusGuard\Development\StrictPersistence`.

It does not authorize application or website blocking, browser policy,
scheduled tasks, elevated recovery, credential handling, or blocking recovery
and security tools. Production activation remains disabled until FG-106 closes
the recovery gate.

## Plan

1. Add a versioned, bounded, integrity-checked strict-policy snapshot store.
2. Persist snapshots atomically and fail open by quarantining unreadable,
   malformed, invalid, or integrity-failing active state.
3. Reconcile time from UTC plus monotonic elapsed time, clamp backward wall-clock
   movement, and expire immediately on forward movement past the finite end.
4. Add tests for restart recovery, expiration, corruption, forward/backward
   clock changes, bounds, and atomic replacement.
5. Run formatting, Release build, and all tests before staging the service.
6. Stage only the reviewed service, verify a non-enforcing finite development
   policy across service restart, then remove its state and restore the original
   service configuration.

## Acceptance criteria

- A strict snapshot contains a schema version, immutable policy, UTC timestamps,
  boot identifier, last trusted UTC observation, and HMAC-SHA256 integrity tag.
- Only strict, finite, currently active policies with valid rules are accepted.
- Payload and rule counts are bounded.
- Atomic save never exposes a partially written active snapshot.
- Corruption and invalid integrity fail open and preserve diagnostic evidence.
- Backward wall-clock changes cannot extend the session within a service boot;
  forward changes expire it.
- A new coordinator instance recovers a valid unexpired snapshot.
- Expiration removes active state without the desktop application.
- Full formatting, build, and automated tests pass.
- Machine verification performs no enforcement and rollback removes only the
  FG-105 development state.

## Security, UX, and recovery implications

The persisted snapshot is an enforcement input, so integrity failure must release
instead of guessing. HMAC key provisioning and restrictive machine ACLs are
required before production use; this milestone injects key material and does not
store it in the snapshot. No user-facing control or claim is added. The desktop
continues to describe strict activation as a simulator.

Until FG-106 is complete, production strict activation is deliberately gated
off. Administrator recovery for this development milestone is to stop the exact
development service and remove only:

`C:\ProgramData\FocusGuard\Development\StrictPersistence`

The service can then be restarted. No persisted state in that directory drives
real process, browser, network, registry, or security-tool enforcement.

## Rollback

Before machine mutation, record the exact service account, binary path, state,
and start mode. Stop only `FocusGuard.Development`, restore the prior published
binary if needed, remove only the verified FG-105 directory above, restore the
recorded start mode/state, and rerun the full verification suite.
