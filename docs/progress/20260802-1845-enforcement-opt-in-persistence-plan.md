# Enforcement opt-in persistence and active health — plan

## Root cause

`FocusViewModel.IsEnforcementEnabled` defaults to `false` on every Desktop
process start and is not represented in `DesktopSettings`. Reinstall correctly
restarts Desktop, but that restart silently loses the user's explicit blocking
choice. The next session is therefore sent to the service with
`EnforcementEnabled=false`, producing the observed `Timer only` state even
while the service, ProcessAgent, and browser extension are running.

The active workspace also shows only a compact protected/timer badge and no
long-form `StatusMessage`, hiding the existing Connected/Degraded explanation.

## Plan and acceptance criteria

1. Persist the explicit enforcement opt-in in per-user Desktop settings, with
   a safe `false` default for existing and missing settings.
2. Restore it before the first session can be started and save every explicit
   toggle change.
3. During an active session, display the service-authoritative status message;
   report timer-only as intentionally disabled rather than degraded.
4. Add regression coverage for settings round-trip, Desktop restart, activation
   request propagation, and active status wording.
5. Run formatting, build, full tests, regenerate the installer EXE, validate
   installer contracts/smoke test, refresh graphify, and update `TASKS.md`.

## Security, UX, and recovery implications

- The default remains off; blocking is never enabled without a prior explicit
  user choice.
- Persisting that choice makes a servicing restart preserve user intent.
- The service remains authoritative for enforcement health and expiration.
- No active session, policy, service, registry, ACL, or browser configuration
  is mutated while implementing or testing this fix.

