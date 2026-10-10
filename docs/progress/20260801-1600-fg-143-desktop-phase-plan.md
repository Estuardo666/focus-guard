# FG-143 plan — Desktop and tray phase status slice

## Scope

- Surface the service-reported current phase and next transition in Desktop.
- Show cycle status in the tray flyout using the same ViewModel state.
- Expose normal-mode break skipping only when the authoritative status permits it.
- Keep strict sessions without an actionable skip control.

## Implications

- Security: the UI never advances time or infers a break; it only renders IPC
  status and sends the current phase index back to the service.
- UX: phase names are textual and the action disappears outside an authorized
  normal break. The tray remains useful without opening the main window.
- Recovery: a refreshed status replaces stale phase presentation immediately;
  a disconnected or invalid response follows the existing degraded state.

## Acceptance

Active cycle status is represented in both surfaces, the skip action is gated
by phase and strict state, localized strings exist in English and Spanish, and
the ViewModel regression suite remains green.
