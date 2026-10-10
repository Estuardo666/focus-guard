# ADR 0010: Immutable finite focus-cycle plans

## Status

Accepted for FG-139.

## Decision

Pomodoro is represented as one finite session with an immutable ordered list of
phases. The platform-independent domain builder receives a UTC start time and
validated cycle configuration, then produces all phase boundaries and the
exact session end time deterministically.

Cycle-count plans end after the final focus phase. Total-duration plans end at
the requested instant and may contain a partial final focus or break phase.
Long breaks replace the break after each configured completed group, except
when that focus phase is the final phase of a cycle-count plan. The first
version permits at most 24 complete focus cycles.

## Consequences

The service can persist, sign, and recover one complete plan instead of
reconstructing user intent after activation. Desktop code can preview the same
plan without becoming authoritative. Simple timers remain the existing single
interval session path and do not acquire cycle semantics.
