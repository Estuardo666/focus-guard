# FG-138 plan — continuously recover the installed ProcessAgent

## Acceptance criteria

1. Desktop checks the fixed sibling `FocusGuard.ProcessAgent.exe` immediately
   and periodically for as long as Desktop remains open, including while hidden
   in the notification area.
2. A missing agent is relaunched without creating a duplicate when it is
   already running.
3. Discovery or launch failures remain fail-open and do not terminate Desktop
   or disable the service-authoritative finite session timer.
4. Automated tests prove that monitoring repeats after the initial check and
   continues after a failed launch attempt.

## Implementation plan

1. Add a small cancellable supervisor around the existing fixed-path startup
   check.
2. Start it with the Desktop lifetime and cancel it during application exit.
3. Add lifecycle regression tests, then run formatting, build, focused tests,
   the full suite, and installer contract tests.
4. Update `TASKS.md`, write the completion checkpoint, and refresh graphify.

## Implications

- Security: the supervisor can launch only the fixed executable beside Desktop;
  no configurable command, arguments, elevation, or remote input is accepted.
- UX: a crashed or externally stopped application-enforcement agent recovers
  while Desktop is open instead of leaving the next session degraded.
- Recovery: failures remain visible through degraded health and are retried;
  the Windows Service remains authoritative for expiration and release.

