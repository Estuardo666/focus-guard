# Recovery design

No real strict persistence may be enabled until this document contains a tested recovery procedure.

Required future sections:

- recovery-code generation and hashing
- elevated delayed recovery
- service failure recovery
- policy corruption behavior
- Safe Mode instructions
- uninstall behavior during and outside strict sessions
- reboot-expiration drill
- rollback verification

## Tested reboot-expiration drill

FG-104 validated the development service's reboot reconciliation with a
synthetic, non-enforcing durable restriction marker. The owner-present procedure,
acceptance criteria, and exact rollback are in
`docs/owner-present/fg-104-reboot-expiration-drill.md`.

On 2026-07-28:

- a finite marker was armed to expire at `2026-07-28T16:21:45.2330728Z`;
- Windows restarted and produced a different boot identifier;
- `FocusGuard.Development` started automatically as `LocalService`;
- the service recorded `Released` at `2026-07-28T16:21:46.1944545Z`;
- the result recorded `RestartObserved=true` and reason `Expired`;
- rollback restored manual service start and removed only the drill directory.

This proves the bounded development reconciliation mechanism, not production
strict persistence. Real strict enforcement remains prohibited until FG-105 and
the remaining recovery sections and release-gate tests are complete.

## Strict-persistence fail-open behavior

FG-105 added a versioned, bounded, HMAC-SHA256-authenticated active-policy
snapshot. The service loads it only when a separately provisioned DPAPI
`LocalMachine` integrity key exists. Missing or unreadable key material leaves
strict persistence inactive. Invalid, corrupt, oversized, expired, or
integrity-failing active state is never enforced; it is released or quarantined.

The service reconciles wall-clock UTC with monotonic elapsed time. A backward
wall-clock change cannot extend an already loaded session, and a forward change
past the exact end releases it. The persisted trusted UTC observation also
clamps backward movement across service restarts.

Production key provisioning and strict activation remain disabled until FG-106
implements and tests recovery-code validation and elevated delayed recovery.
For the development host, an administrator can fail open by stopping
`FocusGuard.Development` and removing only:

`C:\ProgramData\FocusGuard\Development\StrictPersistence`

The service can then be restarted. FG-105 machine verification did not create
this directory, provision a key, arm a policy, or perform real enforcement.

## Recovery-code and delayed recovery

Before strict activation, FocusGuard generates a 160-bit random recovery code,
formats it for transcription, and shows it once. The machine snapshot stores
only a random 128-bit salt and a PBKDF2-SHA256 hash (210,000 iterations). The
plain code is not written to policy, request, audit, or command-line arguments.
Activation must require explicit acknowledgement that the code was saved.

`FocusGuard.Recovery.exe` is independent of the desktop application and network.
It must be launched from an elevated administrator console. It refuses recovery
codes passed as arguments and reads the code without console echo. A valid code
creates an atomic, HMAC-authenticated, session-bound request with exact request
and eligibility UTC timestamps. Repeating the request is idempotent.

The utility does not release restrictions. The Windows service remains
authoritative, waits for the configured delay (15 minutes by default, bounded
from 1 minute to 24 hours), survives restart, and removes both active policy and
pending request only after eligibility. Request, rejection, and release use
sanitized local audit events that exclude codes, salts, hashes, and policy
contents.

### Safe Mode recovery

FocusGuard does not block Safe Mode, Windows Recovery Environment, Task Manager,
Registry Editor, security tools, disk recovery tools, or accessibility tools.
If the normal elevated utility cannot run, an administrator may boot Windows
Safe Mode using Microsoft's normal recovery UI, stop
`FocusGuard.Development`, and rename the exact development state directory:

`C:\ProgramData\FocusGuard\Development\StrictPersistence`

Renaming preserves diagnostic evidence while failing open on the next normal
service start. Do not modify boot configuration, accounts, unrelated ACLs, or
Windows recovery/security tools.

## Production uninstall recovery

The production bundle installs the service and recovery utility together under
`C:\Program Files\FocusGuard` and stores machine state under
`C:\ProgramData\FocusGuard`. The service runs as `LocalService`; the recovery
utility remains available independently of the desktop application.

`scripts\uninstall-focusguard.ps1` refuses to stop or remove the installation
when either `StrictPersistence\active-policy.json` or
`StrictPersistence\recovery-request.json` exists. The administrator must run
the elevated recovery utility, wait until the exact delayed release time, and
then retry uninstall. The script never bypasses the delay or deletes strict
state while it is present. Without `-PurgeData`, uninstall preserves
`%ProgramData%` for audit/recovery review.

### FG-106 owner-present drill

On 2026-07-28 a synthetic strict snapshot with zero application and zero website
rules was armed. The elevated utility accepted the code through its hidden
prompt, recorded a request at approximately `2026-07-28T16:40:05Z`, and reported
local eligibility at 11:41:05 (America/Guayaquil). Before eligibility, the
policy remained active. At `2026-07-28T16:41:06Z`, the service removed the
policy and request and recorded `RecoveryReleased / DelayElapsed`.

The service remained running as `LocalService`; ACLs were limited to SYSTEM,
Administrators, and LocalService. Audit inspection found no recovery code or
hash. Cleanup removed only the strict-persistence drill directory and the final
service binary matched the reviewed publish.
