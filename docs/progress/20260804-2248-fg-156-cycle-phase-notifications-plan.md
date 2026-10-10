# FG-156 plan — Cycle phase notifications above the taskbar clock

## Goal

Show one localized FocusGuard corner notification whenever an authoritative
focus or short-break boundary is observed. A transition combines the ending
and starting phase in one card so adjacent phases never create overlapping
notifications.

## Acceptance criteria

1. Starting a cycle session announces the first focus phase above the taskbar
   clock.
2. Focus-to-short-break and short-break-to-focus transitions produce one card
   that identifies both the completed and newly active phase.
3. The final focus phase and a terminal short break are announced as complete.
4. Repeated status polls, countdown-only updates, simple timers, and long-break
   boundaries do not create duplicate or unrelated notices.
5. The existing Notifications setting exposes a persistent, enabled-by-default
   cycle-phase notification preference.
6. English and Spanish resources, placement, transition detection, persistence,
   and accessibility contracts are covered by automated tests.

## Implementation plan

1. Add a small stateful transition detector that consumes only validated,
   service-authoritative `StatusResponse` values and emits at most one semantic
   transition per boundary.
2. Raise the transition from `FocusViewModel`, render it through a non-activating
   Dark Zen corner card, and reuse the working-area bottom-right placement.
3. Wire the existing break-notification setting to a persisted Desktop setting
   and replace the placeholder notification copy.
4. Add focused Desktop tests, run formatting, Release build, focused and full
   tests, refresh graphify, then rebuild and validate the standalone installer.

## Security, UX, and recovery implications

- Security: the Desktop never advances phases; it only describes the service's
  current phase and never changes enforcement or break authorization.
- UX: one calm card per boundary avoids a two-toast burst, does not steal focus,
  stays above the taskbar clock, and can be disabled in Settings.
- Recovery: a Desktop reconnect may announce the currently active focus/short
  break once, but repeated polls are deduplicated. Restart reconciliation and
  restriction release remain owned by the service.

