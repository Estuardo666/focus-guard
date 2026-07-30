# FG-106 owner-present elevated delayed recovery

## Authorization and scope

The owner authorized FG-106 on 2026-07-28 with Codex running as administrator.
This milestone may add an independent recovery executable, DPAPI-protected
development key provisioning, restrictive ACLs under the exact FG-106
development directory, a synthetic non-enforcing recovery drill, and service
restart/update needed to verify it.

It does not authorize UAC automation, credential prompts, real application or
website blocking, browser policy, scheduled tasks, security-tool blocking, or
anti-uninstall behavior.

## Plan

1. Generate a high-entropy recovery code and store only a versioned PBKDF2-SHA256
   salt/hash record in the strict snapshot.
2. Add an independent console recovery utility that refuses non-elevated use,
   reads the code without command-line exposure, works offline, and never loads
   the desktop application.
3. After validation, atomically write an HMAC-authenticated pending request with
   exact request and eligibility UTC timestamps.
4. Have the service remain authoritative: reconcile UTC and monotonic time,
   survive restart, and release only after the configured delay.
5. Record sanitized atomic audit events for request, rejection, release, and
   fail-open outcomes; never log the code, hash, salt, or policy contents.
6. Test generation, constant-time validation behavior, wrong/malformed codes,
   default delay, early refusal, delayed release, restart, corruption, and
   idempotence.
7. Run full preflight before any machine mutation. Use only a synthetic empty
   policy for the owner-present drill, then remove the exact drill directory and
   restore the previous service binary/configuration.

## Acceptance criteria

- Generated codes contain at least 128 bits of cryptographic entropy.
- Only a random salt and PBKDF2-SHA256 hash are persisted.
- Recovery requires an administrator token and never accepts a code argument.
- Wrong or malformed codes do not create a pending request.
- Default delay is exactly 15 minutes and bounded to 1 minute–24 hours.
- Pending requests are authenticated, session-bound, finite, atomic, and
  idempotent.
- The service, not the utility, performs release after the exact eligibility
  time and records it.
- Request/release survives coordinator/service restart and works without
  desktop or network.
- Corrupt request fails closed for recovery while corrupt active policy retains
  FG-105 fail-open behavior.
- Rollback removes only the verified FG-106 development state and leaves the
  service healthy.

## Security, UX, and recovery implications

The code is shown once and must be saved before strict activation. Recovery is
deliberately not immediate; the utility must display the exact eligibility time.
Audit data contains session identifiers, UTC timestamps, outcome, and reason
only. The utility is a protected-process exclusion and must never be blocked.

If the development drill fails, stop `FocusGuard.Development`, remove only
`C:\ProgramData\FocusGuard\Development\StrictPersistence`, restore the backed-up
service files, and restart the service. Safe Mode documentation is updated as
part of this checkpoint; no Safe Mode or boot configuration is modified.
