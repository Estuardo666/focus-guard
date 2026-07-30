# FG-108 Desktop-to-service activation

## Scope

Connect the installed Desktop application to a dedicated, versioned named-pipe
control endpoint owned by the Windows service. The service becomes authoritative
for activation, status, normal stop, expiration, and strict persistence.

The current `Deep Work` profile and its displayed 12 website / 4 application
counts remain mock UI data. FG-108 sends an empty immutable enforcement policy
and must state clearly that rule enforcement is not yet connected.

## Plan

1. Extend bounded IPC contracts for status, activation, normal stop, strict
   recovery credential, exact UTC timestamps, and sanitized failures.
2. Add a restrictive Desktop control pipe and client with bounded messages,
   timeouts, correlation checks, and cancellation.
3. Generate strict recovery material in Desktop, show the plain code once, and
   send only the salted PBKDF2 credential after saved-code and typed
   confirmation acknowledgements.
4. Make the service own session state and expiration. Strict activation writes
   an integrity-protected empty policy under the production state root.
5. Update the Focus screen for service health, active timer, normal stop,
   strict review, one-time code display, and honest enforcement limitations.
6. Test malformed IPC, authorization boundaries, activation idempotence,
   expiration, normal stop, strict persistence, and no plain-code transport.
7. Run format, build, full tests, and a non-enforcing owner-present installed
   service drill before updating installed binaries.

## Acceptance criteria

- Desktop no longer claims a simulator service when the production service is
  reachable.
- Status and remaining time come from the service and survive Desktop restart.
- Normal sessions can be stopped; strict sessions cannot use normal stop.
- Strict code is displayed once before activation; only its credential crosses
  IPC and is persisted.
- Strict sessions are finite, use the configured 15-minute recovery delay, and
  persist an authenticated empty policy.
- Invalid, oversized, unknown-version, wrong-correlation, and unauthorized
  messages fail safely.
- UI always states that application and website enforcement remain unconnected.
- No real application or website blocking is enabled by this milestone.

## Security, UX, and recovery implications

The Desktop is configuration/status only; the service owns time and release.
The control pipe permits local authenticated users but rejects network access.
Strict activation remains opt-in and finite. The code is never accepted through
arguments, logs, policy, or IPC. The installed recovery utility remains
independent of Desktop and network.

Rollback restores the FG-107 installed binaries and restarts `FocusGuard`.
Any synthetic strict state created by the drill contains zero rules and must be
released or removed only through the documented recovery cleanup.
