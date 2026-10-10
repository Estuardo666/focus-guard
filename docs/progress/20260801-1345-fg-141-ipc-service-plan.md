# FG-141 — authoritative cycle IPC and service plan

## Acceptance criteria

1. Version 2 control messages prepare and activate simple or cycle sessions.
2. Preparation returns an immutable phase list, exact end time, and a two-minute
   expiry; it can be activated at most once.
3. Status exposes current phase, next phase, and total end time.
4. The service alone advances phases from UTC; Desktop does not calculate active
   phase state.
5. Normal mode can skip only the currently authorized break, advances remaining
   phases, and repeated requests for the same phase are idempotent.
6. Strict mode rejects skip and normal stop operations.
7. New IPC payload readers reject unknown top-level and nested JSON fields.

## Security, UX, and recovery implications

- Security: unactivated preparations apply no policy and expire after two
  minutes. Skip is accepted only for a normal-mode break whose phase index
  matches the request.
- UX: the service status contains enough data for Desktop and tray clients to
  render the current phase and next transition without owning a timer.
- Recovery: the in-memory slice remains fail-open and finite; strict durable
  plan recovery and channel authorization are deferred to FG-142.

## Implementation order

1. Add versioned cycle contracts and strict JSON shape validation.
2. Add preparation/activation/skip methods to the service interface.
3. Store prepared and active plans in the in-memory service and derive status.
4. Add contract and service transition tests.
