# FG-112 per-user process agent plan

## Summary

Owner authorized a per-user process agent instead of elevating the service to
`LocalSystem`.

## Plan

1. Add bounded process-agent heartbeat/acknowledgement contracts.
2. Add a service relay with a finite lease and atomic browser/agent confirmation.
3. Implement the interactive agent using existing evaluator, controller, SHA cache,
   protected-process policy, cancellation, and sanitized audit messages.
4. Package and register the agent reversibly under HKCU startup.
5. Test loss/reconnection, service restart, expiration, and real process blocking.

## Security implications

The service stays `LocalService`. The agent receives no recovery credential and
cannot mutate session state. Every message is bounded and validated. Failure of the
agent or service releases enforcement.

## UX implications

Desktop remains configuration/status only. Existing enforcement health becomes
degraded if the user-session agent is absent.

## Recovery implications

Killing or failing to start the agent fails open. Installer rollback restores the
previous agent binary and startup value. Finite expiration remains service-owned.
