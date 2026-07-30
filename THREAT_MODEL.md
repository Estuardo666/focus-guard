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

### Unauthorized policy changes

Mitigations:

- immutable snapshots
- restrictive ACLs
- signed/integrity-checked policy
- authenticated named pipes
- input validation
- audit events

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

## Security release gate

Strict persistence may not ship until automated tests demonstrate:

- reboot recovery
- automatic expiration
- policy corruption recovery
- wall-clock manipulation handling
- recovery-code validation
- delayed recovery
- protected-process exclusions
