# FG-158 plan — exact MV3 cycle phase alarms

## Symptom

Website rules remain blocked after the Desktop reports that a cycle entered a
break.

## Root cause

The installed service and extension contain the correct break-rule selection
and phase handshake, but the MV3 service worker has no durable wake-up at the
authoritative phase boundary. Its two-second JavaScript interval stops when
Chromium suspends the worker, while the periodic alarm can deliver the new
policy up to one minute late and a navigation can be evaluated against the old
focus policy before that refresh completes.

## Implementation plan

1. Derive the current authorized phase end from the integrity-validated policy.
2. Schedule a one-shot Chromium alarm for that exact boundary whenever a policy
   is stored.
3. On that alarm, request and acknowledge the new phase policy immediately.
4. Add regression coverage for valid, expired, and malformed boundaries.
5. Run formatting, build, full tests, package validation, graph refresh, and
   regenerate the versioned standalone installer.

## Security, UX, and recovery implications

- Security: the alarm carries no authority; it only wakes the extension. The
  service-signed policy and exact acknowledgement remain mandatory.
- UX: URL blocking releases at break entry and returns at focus entry without a
  reload, wait, or user action.
- Recovery: if Chromium or the native host is unavailable at the boundary, the
  existing periodic heartbeat and reconnect path retry without extending a
  stale break authorization.

## Acceptance criteria

- A current focus or break policy schedules one alarm at its exact UTC end.
- The phase alarm requests the next authoritative policy.
- Past, missing, or invalid phase ends do not schedule unsafe alarms.
- All existing application, browser, integrity, and package tests pass.

## Checkpoint

### Summary

The extension now schedules a durable one-shot MV3 alarm at every signed phase
end. The alarm wakes the service worker, fetches the next service-authoritative
policy, and completes the existing acknowledgement handshake. A tab already on
the FocusGuard block screen returns to its sanitized original URL as soon as
the break policy permits it.

### Files changed

- `extension/src/navigation-enforcement.js`
- `extension/src/native-health.js`
- `extension/src/status-page.js`
- `extension/test/navigation-enforcement.test.ts`
- `extension/manifest.json`
- ADR, task log, checkpoint, and generated graph files.

### Tests added

- Exact future phase-end alarm scheduling and stale-alarm clearing.
- Automatic release of an already-blocked tab under an authorized break policy.

### Commands executed

- Focused extension regression test before and after implementation.
- `npm test` and `npm run check` under `extension`.
- `dotnet build FocusGuard.slnx --no-restore`.
- `dotnet test FocusGuard.slnx --no-build --no-restore`.
- `scripts/build-installer.ps1 -Version 0.1.8`.
- `scripts/test-installer.ps1`.
- `scripts/test-browser-extension-package.ps1 -Version 0.1.8`.
- `graphify update .`.

### Results

- Build: passed with zero warnings and zero errors.
- .NET: 359 passed, zero failed.
- Extension: 18 passed, zero failed; TypeScript check passed.
- Chrome/Edge package content and hashes passed.
- Standalone installer `FocusGuard-0.1.8-Setup.exe` was regenerated; its build
  verified the embedded Desktop payload hash.

### UX implications

URL access changes at the displayed phase boundary. A block screen already
open no longer makes the user retry or press Back when the break begins.

### Security implications

The local alarm does not authorize a break. It only requests the new policy;
integrity validation and exact service acknowledgement remain mandatory.

### Limitations

The source and standalone package are verified, but `0.1.8` was not installed
because installation changes the machine and requires explicit owner approval.

### Next task

Install `0.1.8` with the owner present and run a real focus→break→focus URL
transition drill under FG-145.
