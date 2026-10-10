# Threat Model

## Assets

- integrity of active session policy
- correct expiration and release
- availability of Windows and recovery tools
- privacy of local browsing and application data
- authenticity of desktop/service/extension communication

## Intended adversary

The primary adversary is the consenting user acting impulsively with ordinary desktop access.

The product is not designed to defeat a determined local administrator, offline disk access, another operating system, or source-code modification.

## Major risks

### Permanent lockout

Mitigations:

- finite sessions
- authoritative service expiration
- recovery code
- delayed elevated recovery
- automatic fail-open for corrupt active policy
- recovery drills before strict persistence ships

### Clock manipulation

Mitigations:

- UTC timestamps
- `TimeProvider`
- monotonic elapsed-time measurements
- persisted boot/restart reconciliation
- tests for forward and backward wall-clock jumps

Cycle plans are generated from a UTC start and immutable durations. The plan's
phase boundaries and final end time are validated before activation; the UI
does not advance phases independently. A future service implementation must
reconcile the persisted plan after restart and fail open if its integrity
cannot be verified.

### Unauthorized policy changes

Mitigations:

- immutable snapshots
- restrictive ACLs
- signed/integrity-checked policy
- authenticated named pipes
- input validation
- audit events

Cycle policies bind the full phase plan, both normal and break rule sets, and
the service-authorized phase boundary into their integrity hash. Normal focus
rules are suspended only while the service has authorized the current break;
the exact phase end restores them automatically. On restart, consumers begin with focus enforcement and are
relaxed only after valid authority is recovered. Corrupt strict snapshots are
quarantined and fail open.

### Extension removal

Mitigations:

- heartbeat health
- degraded warning
- configurable strict-session browser grace period
- no claims of perfect enforcement

### Privacy overcollection

Mitigations:

- only matched URL data crosses extension boundary
- no query strings by default
- aggregate counters
- local-only logs
- no telemetry by default

### Dangerous system blocking

Mitigations:

- protected process exclusions
- no system-tool blocking in MVP
- no kernel driver
- no process injection
- no security software interference

### Weekly schedule replay and local workflow privacy (FG-163)

Mitigations: validate finite normal-mode activation snapshots and time zones; reject
nested unknown IPC fields; bound schedules, storage and history responses; persist each
occurrence claim before activation; never replace an active session or extend the
original deadline. A missing/corrupt schedule file starts no sessions. Existing
ProgramData ACLs apply; schedules add no privileged OS tasks. Enable is explicit and
shows rule counts, enforcement state and exact start/end. Claimed normal sessions are
not restored on reboot. Clock rollback cannot replay an already claimed occurrence.
An administrator restarting the service ends an active normal session; a graceful stop
records it as interrupted in history and audit so the loss is traceable; a crash or killed
process is recorded on the next start from the normal-session journal (accurate to 10 s).
The journal is only writable by SYSTEM, LocalService and Administrators.

Objectives/reflections are optional, bounded and local to the user's journal. They
do not reach browser/process policy consumers. Installed-app discovery reads local
shortcuts and icons without launching executables. Compact collapse/close cannot alter
service enforcement, and strict consequences remain visible even when collapsed.

## Security release gate

Strict persistence may not ship until automated tests demonstrate:

- reboot recovery
- automatic expiration
- policy corruption recovery
- wall-clock manipulation handling
- recovery-code validation
- delayed recovery
- protected-process exclusions
