# ADR 0009: Independent enforcement-channel leases

## Status

Accepted for FG-114 by owner request on 2026-07-29. This supersedes the coupled
lease decision in ADR 0008; the remaining per-user agent design stays accepted.

## Context

Chrome and Edge development extensions are currently loaded unpackaged and do not
reliably remain registered after reboot. Coupling browser and process leases caused
the authoritative service to revoke valid application enforcement whenever the
extension heartbeat was absent. That made a browser packaging limitation disable
an unrelated protection channel.

## Decision

The service tracks process-agent and browser-extension acknowledgements
independently:

- each channel must acknowledge the exact immutable policy ID and SHA-256 hash;
- each channel enforces only while its own heartbeat and acknowledgement are
  current;
- loss of a browser lease releases browser enforcement but does not revoke the
  process-agent lease;
- loss of a process lease releases application enforcement but does not revoke the
  browser lease;
- a session becomes active when at least one requested channel has acknowledged;
- aggregate enforcement health is connected only when every channel requested by
  the policy is acknowledged; otherwise the active session is visibly degraded;
- expiration, recovery, invalid integrity, and authoritative service loss still
  release every channel.

## Consequences

Application rules can remain enforced after reboot while an unpackaged extension
is absent. Website blocking still makes no claim of protection without its exact
extension acknowledgement. A mixed app-and-website policy may therefore be active
and degraded: available channels keep enforcing while missing channels fail open.

IPC endpoints must not use aggregate health to authorize channel-specific actions
or audit events. The process endpoint uses process connection state and the native
messaging endpoint uses browser connection state.
