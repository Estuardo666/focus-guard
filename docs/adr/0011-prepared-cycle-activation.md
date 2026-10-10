# ADR 0011: Prepare and activate finite cycle sessions

## Status

Accepted for FG-141.

## Decision

Cycle activation uses a versioned two-step control protocol. The service
validates and compiles the complete plan during preparation, returns its UTC
phase boundaries and exact end time, and retains the preparation for no more
than two minutes. Activation consumes the preparation exactly once.

The service derives current and next phase status from the immutable plan and
UTC time. A normal client may request a skip only by naming the current break
phase index; the service retains the elapsed part of that break in history,
removes only its remaining duration and shifts all later boundaries earlier.
Repeating the same request returns the original result. Strict mode
does not accept phase-skipping requests.

## Consequences

Desktop can show a review of the actual service plan before any restriction is
applied. A disconnected or stale client cannot extend a session by holding a
phase open. Durable plan signing, break authorization, and restart recovery
remain separate release work in FG-142.
