# FG-134 — Tray flyout plan

## Goal

Replace the tray icon's left-click behaviour with a compact FocusGuard status
flyout based on the owner reference. Keep the native tray menu as an accessible
and recovery-friendly fallback.

## Relevant specification

- The Desktop configures and reports status; it never enforces policy directly.
- Active strict state and recovery information must remain visible.
- UI copy is localized and final controls use semantic tokens.
- Closing the Desktop hides it in the notification area instead of ending
  enforcement.

## Smallest vertical slice

1. Add a borderless, taskbar-free flyout that shares the authoritative
   `FocusViewModel` already owned by `MainWindow`.
2. Show protection state, active profile, remaining time, and saved website and
   application rule counts.
3. Use the selected profile accent for the remaining time and row icons.
4. Wire Open FocusGuard, Settings, and Exit FocusGuard to the existing lifecycle.
5. Anchor the flyout inside the primary screen work area and close it on
   deactivation or Escape.
6. Add localized strings and automated structure/lifecycle tests.

## UX implications

The tray click becomes a glanceable status surface rather than unexpectedly
opening the full application. The native context menu remains available for
platform-standard keyboard and recovery access. Inactive state is explicit and
does not fabricate a countdown.

## Security and recovery implications

The flyout is read-only except for navigation and process exit. It cannot stop,
shorten, replace, or mutate an active policy. Exit only closes the Desktop
process; the authoritative service remains responsible for session expiration.
The finite end time remains available through the shared status view model.

## Acceptance criteria

1. Tray left-click toggles one flyout instance.
2. The flyout uses real shared session/profile state.
3. Remaining time uses `ProfileAccentBrush`.
4. Open and Settings restore the main window to the expected screen.
5. The flyout closes on deactivation and Escape.
6. Localization, formatting, build, and Desktop tests pass.
