# ADR 0008: Per-user process enforcement agent

## Status

Accepted for FG-112 by owner authorization on 2026-07-28. The coupled browser and
process lease behavior below is superseded by ADR 0009; the per-user agent and
least-privilege decisions remain accepted.

## Context

The authoritative Windows service intentionally runs as `LocalService`. Real
owner-present drills proved that this identity cannot inspect or terminate an
interactive user's processes, even with `PROCESS_QUERY_LIMITED_INFORMATION`.
Changing the service to `LocalSystem` would broaden the impact of every service or
IPC defect and contradict the installed least-privilege boundary.

## Decision

Install a separate `FocusGuard.ProcessAgent` executable that starts in each
interactive user's session and enforces only that user's processes. The agent:

- receives only a bounded, versioned immutable policy from the service;
- acknowledges the exact policy ID and SHA-256 hash;
- enforces only while the service confirms both the browser and agent leases;
- fails open immediately on pipe failure, timeout, policy mismatch, expiration, or
  service-instance change;
- never accepts policy or commands from Desktop;
- does not persist policy, secrets, arbitrary CLR objects, or browsing data;
- keeps protected-process exclusions and sanitized audit behavior;
- is registered by the installer in the current user's `HKCU\...\Run` key and has
  a reversible uninstall/rollback path.

The service remains authoritative for session state and expiration. The browser
and process agent are peer enforcement channels: loss of either revokes
confirmation for both.

## Consequences

Application blocking now occurs with the same user token as the target, avoiding
service elevation. A user can terminate the agent; that is expected to release both
channels and surface degraded health. Strict mode remains resistant to casual UI
closure and reboot after the startup registration is validated, but does not claim
resistance to a determined administrator.
