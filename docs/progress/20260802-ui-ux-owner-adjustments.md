# FG-153 — Focus UI/UX owner adjustments

## 1. Summary

Refined the Focus workspace from the owner's screenshot review: Timer/Cycles no
longer resets scroll, the timer animates only changed characters and scales long
values, profiles are selected in place, preset edges have safe padding, the
primary action precedes enforcement controls, mode content fades in, and primary
navigation is permanently visible in the top bar.

## 2. Files changed

- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml`
- `src/FocusGuard.Desktop/FocusWorkspaceView.axaml.cs`
- `src/FocusGuard.Desktop/MainWindow.axaml`
- `src/FocusGuard.Desktop/Styles/Controls.axaml`
- `tests/FocusGuard.Desktop.Tests/DesktopLifecycleTests.cs`
- `tests/FocusGuard.Desktop.Tests/DesignSystemTests.cs`
- `TASKS.md`

## 3. Tests added or updated

- Scroll-reset policy now proves Timer/Cycles and enforcement changes preserve offset.
- Timer digit sizing covers short and extended values.
- Design-system tests require the in-place profile selector and stable digit host.

## 4. Commands executed

- `dotnet format FocusGuard.slnx --no-restore`
- `dotnet build FocusGuard.slnx --no-restore -warnaserror`
- `dotnet test FocusGuard.slnx --no-restore --no-build`
- `graphify update .`

## 5. Results

- Build: passed, 0 warnings, 0 errors.
- Tests: passed, 340 total across all test projects.
- Automated UI inspection was unavailable because `orca` is not installed; no machine changes were made.

## 6. UX implications

- Mode and toggle changes no longer disorient the user by jumping to the top.
- Stable timer characters reduce visual noise; long values retain inner-ring padding.
- Profile switching and primary navigation require fewer steps.
- Duration and cycle cards retain visible left/right borders.

## 7. Security implications

Profile switching remains disabled during active sessions through
`CanNavigateToEditors`. Enforcement and strict-mode commands and warnings are
unchanged.

## 8. Limitations

Pixel-level automated screenshot comparison was not available in this environment.
Schedule remains visible but disabled because its product section is not implemented.

## 9. Next task

Continue FG-144 cycle history, localization, and accessibility coverage in queue order.
