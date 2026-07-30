# ADR 0002: The service is authoritative

## Status

Accepted.

## Decision

The Windows Service owns active session state, policy activation, expiration, and enforcement health.

## Consequences

Closing or crashing the desktop UI cannot end an active session. The service must recover state after restart and release expired policies independently.
