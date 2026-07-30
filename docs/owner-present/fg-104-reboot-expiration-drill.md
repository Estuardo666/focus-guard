# FG-104 owner-present reboot-expiration drill

## Authorization and scope

The owner explicitly authorized FG-104 on 2026-07-28 with Codex already
running as administrator. This drill may:

- publish and restage the exact `FocusGuard.Development` service;
- change only that service from manual start to automatic start for the drill;
- create only `%ProgramData%\FocusGuard\Development\RebootExpirationDrill`;
- restart Windows once;
- restore manual start and remove the drill directory during rollback.

It must not enable real application or website blocking, strict persistence,
scheduled tasks, recovery-code behavior, browser policy, security-tool blocking,
or ACL/account changes.

## Plan

1. Add a development-only durable drill worker and deterministic tests.
2. The armed state represents a synthetic restriction marker with a finite UTC
   expiration. It records the pre-reboot boot identifier.
3. On service startup, validate the bounded JSON state. Fail open by releasing
   malformed or expired state. For valid state, wait until its finite expiration,
   then record a released result.
4. Build, test, publish, stop the exact service, restage the reviewed publish,
   and start it.
5. Arm a short drill, set only `FocusGuard.Development` to automatic start, and
   restart Windows.
6. After restart, prove a different boot was observed, the service started
   automatically, and the marker released no later than the bounded polling
   allowance after expiration.
7. Restore manual start, remove only the drill directory, restart the service,
   and run the full verification suite.

## Acceptance criteria

- All preflight formatting, build, and automated tests pass.
- The service remains `LocalService` at the exact ProgramData binary path.
- The durable state has a schema version, unique drill ID, UTC timestamps,
  finite duration, and pre-reboot boot identifier.
- Automated tests cover pre-expiration retention, expiration release,
  malformed-state fail-open, and restart observation.
- A real Windows restart changes the observed boot identifier.
- SCM reports the service running automatically after that restart.
- The durable result records `Released`, `RestartObserved=true`, and a release
  time at or after the exact expiration.
- Rollback restores manual start and removes only the drill state.
- No real enforcement or recovery tool is activated.

## Recovery and rollback

If the service fails after reboot, an administrator can run:

```powershell
sc.exe config "FocusGuard.Development" start= demand
sc.exe start "FocusGuard.Development"
```

The synthetic marker never drives enforcement. If its files are corrupt or the
service cannot read them, the worker fails open and records or treats the marker
as released. Final cleanup removes only:

`C:\ProgramData\FocusGuard\Development\RebootExpirationDrill`

The existing FG-102 uninstall script remains the full service rollback.

## Security, UX, and recovery implications

No user application, website, browser, recovery utility, or Windows security
tool is blocked. The drill does not claim that strict persistence exists. There
is no desktop UX change. The result is machine-local and contains identifiers
and timestamps only, with no secrets or browsing data.
