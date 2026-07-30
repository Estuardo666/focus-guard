# Enforcement recovery, audit, and limits

## Opt-in and health gate

Application and website enforcement is off by default for every activation. A
session that explicitly enables it remains `Activating` until at least one
requested channel acknowledges the exact immutable policy ID and SHA-256 hash.
Each channel is authorized only by its own acknowledgement and heartbeat. The
aggregate state is `Connected` only while every requested channel is current; a
missing channel produces `Degraded`, releases that channel, and leaves separately
acknowledged channels active as defined by ADR 0009.

## Automatic release and recovery

The Windows service owns the finite UTC expiration. Expiration, a permitted
normal-session stop, or successful delayed elevated recovery withdraws the
delivery and causes application and website evaluation to fail open. If the
Desktop is unavailable, use the existing elevated recovery utility and recovery
code. If the extension or native host is unavailable, browsing is allowed while
the service reports degradation. Safe Mode and offline administrator recovery
remain documented installer-level escape hatches for a determined administrator.

Protected Windows logon, FocusGuard recovery, security-health, Task Manager,
Registry Editor, disk-recovery, and accessibility executables are excluded even
when a broad application rule matches them.

## Audit and privacy

Application outcomes are structured events containing the process ID, result,
sanitized error code, and UTC time; command lines and secrets are not logged.
Website evaluation stays inside the extension. Native messaging carries health,
policy identity, the confirmed rule snapshot, and a normalized URL only for a
matched block audit—never unrelated browsing history, query parameters, or
fragments. The block redirect is the extension's local status page and does not
embed the attempted URL.

## Honest limits

- Process enforcement polls, so an application may appear briefly before close.
- Graceful close is requested first; termination follows after two seconds if
  the exact process instance is still running.
- Hash rules require reading accessible executable files; inaccessible processes
  fail open for that scan.
- Chrome/Edge Manifest V3 listeners work only while the extension is installed
  and enabled. Extension loss is visible degradation, not tamper-proof blocking.
- FocusGuard does not resist a determined administrator, Safe Mode, offline disk
  edits, another operating system, or recompilation of this open-source project.
- No kernel driver, injection, HTTPS interception, security-tool blocking,
  surveillance, remote administration, or destructive anti-uninstall is used.
